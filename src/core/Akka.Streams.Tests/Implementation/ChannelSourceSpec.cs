//-----------------------------------------------------------------------
// <copyright file="ChannelSourceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Implementation
{
    public class ChannelSourceSpec : Akka.TestKit.Xunit2.TestKit
    {
        private readonly ActorMaterializer _materializer;

        public ChannelSourceSpec(ITestOutputHelper output) : base(output: output)
        {
            _materializer = Sys.Materializer();
        }

        [Fact]
        public void ChannelSource_must_complete_after_channel_completes()
        {
            var channel = Channel.CreateUnbounded<int>();
            var probe = this.CreateManualSubscriberProbe<int>();

            ChannelSource.FromReader<int>(channel)
                .To(Sink.FromSubscriber(probe))
                .Run(_materializer);

            var subscription = probe.ExpectSubscription();
            subscription.Request(2);

            channel.Writer.Complete();

            probe.ExpectComplete();
        }


        [Fact]
        public void ChannelSource_must_complete_after_channel_fails()
        {
            var channel = Channel.CreateUnbounded<int>();
            var probe = this.CreateManualSubscriberProbe<int>();
            var failure = new Exception("BOOM!");

            ChannelSource.FromReader<int>(channel)
                .To(Sink.FromSubscriber(probe))
                .Run(_materializer);

            var subscription = probe.ExpectSubscription();
            subscription.Request(2);

            channel.Writer.Complete(failure);

            probe.ExpectError().InnerException.Should().Be(failure);
        }

        [Fact]
        public async Task ChannelSource_must_read_incoming_events()
        {
            var tcs = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var channel = Channel.CreateBounded<int>(3);
            await channel.Writer.WriteAsync(1, tcs.Token);
            await channel.Writer.WriteAsync(2, tcs.Token);
            await channel.Writer.WriteAsync(3, tcs.Token);

            var probe = this.CreateManualSubscriberProbe<int>();

            ChannelSource.FromReader<int>(channel)
                .To(Sink.FromSubscriber(probe))
                .Run(_materializer);

            var subscription = probe.ExpectSubscription();
            subscription.Request(5);

            probe.ExpectNext(1);
            probe.ExpectNext(2);

            await channel.Writer.WriteAsync(4, tcs.Token);
            await channel.Writer.WriteAsync(5, tcs.Token);
            
            probe.ExpectNext(3);
            probe.ExpectNext(4);
            probe.ExpectNext(5);
        }

        /// <summary>
        /// Reproduces GitHub issue #7940: NullReferenceException when completing
        /// a ChannelReader while the stream is waiting for data.
        /// </summary>
        [Fact(DisplayName = "ChannelSource should not throw NRE when completing channel while waiting for data")]
        public async Task ChannelSource_should_not_throw_NRE_when_completing_channel_while_waiting_for_data()
        {
            // This test reproduces the race condition from #7940
            // Run multiple iterations to increase chance of hitting the race
            for (var iteration = 0; iteration < 20; iteration++)
            {
                var channel = Channel.CreateUnbounded<string>();
                var processed = new ConcurrentBag<string>();

                // Exactly matches the repro from the issue - using ImmutableArray.Create and Sink.Ignore<Done>
                var streamTask = ChannelSource.FromReader(channel.Reader)
                    .Select(ImmutableArray.Create)
                    .Select(s =>
                    {
                        foreach (var item in s) processed.Add(item);
                        return Done.Instance;
                    })
                    .ToMaterialized(Sink.Ignore<Done>(), Keep.Right)
                    .Run(_materializer);

                // Write some items
                var testInput = Enumerable.Range(1, 5).Select(i => i.ToString()).ToList();
                foreach (var item in testInput)
                    await channel.Writer.WriteAsync(item);

                // Wait 1 second for stream to process items and then wait for more data
                // This is the key to reproducing the race - the stream needs to be
                // waiting in WaitToReadAsync when we complete the writer (channel is empty)
                await Task.Delay(1000);

                // Complete the channel - this can cause NRE if there's a race
                // between OnReaderComplete and the async continuation of WaitToReadAsync
                channel.Writer.Complete();

                // Stream should complete cleanly without exceptions
                await streamTask;

                // Verify all items were processed
                processed.Count.Should().Be(5, $"iteration {iteration} failed");
            }
        }
    }
}
