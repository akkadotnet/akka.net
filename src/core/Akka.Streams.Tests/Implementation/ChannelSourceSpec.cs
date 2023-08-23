//-----------------------------------------------------------------------
// <copyright file="ChannelSourceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
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
    }
}
