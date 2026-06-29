//-----------------------------------------------------------------------
// <copyright file="ChannelSinkSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Event;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Streams.Tests.Implementation
{
    public class ChannelSinkSpec : Akka.TestKit.Xunit.TestKit
    {
        private readonly ActorMaterializer _materializer;

        public ChannelSinkSpec(ITestOutputHelper output) : base(output: output)
        {
            _materializer = Sys.Materializer();
        }

        #region from writer

        [Fact]
        public Task ChannelSink_writer_when_isOwner_should_complete_channel_with_success_when_upstream_completes()
        {
            var probe = this.CreateManualPublisherProbe<int>();
            var channel = Channel.CreateBounded<int>(10);

            Source.FromPublisher(probe)
                .To(ChannelSink.FromWriter(channel.Writer, true))
                .Run(_materializer);

            var subscription = probe.ExpectSubscription();
            subscription.SendComplete();

            channel.Reader.Completion.Wait(1.Seconds()).Should().BeTrue();
            return Task.CompletedTask;
        }

        [Fact]
        public async Task ChannelSink_writer_isOwner_should_complete_channel_with_failure_when_upstream_fails()
        {
            var exception = new Exception("BOOM!");

            try
            {
                var probe = this.CreateManualPublisherProbe<int>();
                var channel = Channel.CreateBounded<int>(10);

                Source.FromPublisher(probe)
                    .To(ChannelSink.FromWriter(channel.Writer, true))
                    .Run(_materializer);

                var subscription = probe.ExpectSubscription();
                subscription.SendError(exception);

                await channel.Reader.Completion;
            }
            catch (Exception e)
            {
                e.Should().Be(exception);
            }
        }

        [Fact]
        public async Task ChannelSink_writer_when_NOT_owner_should_leave_channel_active()
        {
            var probe = this.CreateManualPublisherProbe<int>();
            var channel = Channel.CreateBounded<int>(10);

            Source.FromPublisher(probe)
                .To(ChannelSink.FromWriter(channel.Writer, false))
                .Run(_materializer);

            var subscription = probe.ExpectSubscription();
            subscription.SendComplete();

            channel.Reader.Completion.Wait(TimeSpan.FromSeconds(1)).Should().BeFalse();

            var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await channel.Writer.WriteAsync(11, cancel.Token);
            var value = await channel.Reader.ReadAsync(cancel.Token);
            value.Should().Be(11);
        }

        [Fact]
        public async Task ChannelSink_writer_NOT_owner_should_leave_channel_active()
        {
            var exception = new Exception("BOOM!");

            var probe = this.CreateManualPublisherProbe<int>();
            var channel = Channel.CreateBounded<int>(10);

            Source.FromPublisher(probe)
                .To(ChannelSink.FromWriter(channel.Writer, false))
                .Run(_materializer);

            var subscription = probe.ExpectSubscription();
            subscription.SendError(exception);

            channel.Reader.Completion.Wait(TimeSpan.FromSeconds(1)).Should().BeFalse();

            var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await channel.Writer.WriteAsync(11, cancel.Token);
            var value = await channel.Reader.ReadAsync(cancel.Token);
            value.Should().Be(11);
        }

        [Fact]
        public async Task ChannelSink_writer_should_propagate_elements_to_channel()
        {
            var probe = this.CreateManualPublisherProbe<int>();
            var channel = Channel.CreateBounded<int>(10);

            Source.FromPublisher(probe)
                .To(ChannelSink.FromWriter(channel.Writer, true))
                .Run(_materializer);

            var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var subscription = probe.ExpectSubscription();
            var n = subscription.ExpectRequest();

            Sys.Log.Info("Requested for {0} elements", n);

            var i = 1;

            for (; i <= n; i++)
                subscription.SendNext(i);

            for (int j = 0; j < n; j++)
            {
                var value = await channel.Reader.ReadAsync(cancel.Token);
                value.Should().Be(j + 1);
            }

            var m = subscription.ExpectRequest() + n;
            Sys.Log.Info("Requested for {0} elements", m - n);

            for (; i <= m; i++)
            {
                subscription.SendNext(i);
                var value = await channel.Reader.ReadAsync(cancel.Token);
                value.Should().Be(i);
            }
        }

        [Fact]
        public async Task ChannelSink_writer_should_deliver_final_element_when_channel_is_full_on_completion()
        {
            const int bufferSize = 4;
            const int elementCount = bufferSize + 1;

            var channel = Channel.CreateBounded<int>(new BoundedChannelOptions(bufferSize)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            });

            Source.From(Enumerable.Range(0, elementCount))
                .RunWith(ChannelSink.FromWriter(channel.Writer, isOwner: true), _materializer);

            var received = new List<int>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (await channel.Reader.WaitToReadAsync(cts.Token))
                while (channel.Reader.TryRead(out var item))
                    received.Add(item);

            received.Should().Equal(Enumerable.Range(0, elementCount));
        }

        #endregion

        #region as reader

        [Fact]
        public Task ChannelSink_reader_should_complete_channel_with_success_when_upstream_completes()
        {
            var probe = this.CreateManualPublisherProbe<int>();

            var reader = Source.FromPublisher(probe)
                .ToMaterialized(ChannelSink.AsReader<int>(10), Keep.Right)
                .Run(_materializer);

            var subscription = probe.ExpectSubscription();
            subscription.SendComplete();

            reader.Completion.Wait(1.Seconds()).Should().BeTrue();
            return Task.CompletedTask;
        }

        [Fact]
        public async Task ChannelSink_reader_should_complete_channel_with_failure_when_upstream_fails()
        {
            var exception = new Exception("BOOM!");

            try
            {
                var probe = this.CreateManualPublisherProbe<int>();

                var reader = Source.FromPublisher(probe)
                    .ToMaterialized(ChannelSink.AsReader<int>(10), Keep.Right)
                    .Run(_materializer);

                var subscription = probe.ExpectSubscription();
                subscription.SendError(exception);

                await reader.Completion;
            }
            catch (Exception e)
            {
                e.Should().Be(exception);
            }
        }

        [Fact]
        public async Task ChannelSink_reader_should_propagate_elements_to_channel()
        {
            var probe = this.CreateManualPublisherProbe<int>();

            var reader = Source.FromPublisher(probe)
                .ToMaterialized(ChannelSink.AsReader<int>(10), Keep.Right)
                .Run(_materializer);

            var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var subscription = probe.ExpectSubscription();
            var n = subscription.ExpectRequest();

            Sys.Log.Info("Requested for {0} elements", n);

            var i = 1;

            for (; i <= n; i++)
                subscription.SendNext(i);

            for (int j = 0; j < n; j++)
            {
                Sys.Log.Info("Request: {0}",j);
                var value = await reader.ReadAsync(cancel.Token);
                Sys.Log.Info("Received: {0}",value);
                value.Should().Be(j + 1);
            }

            var m = subscription.ExpectRequest() + n;
            Sys.Log.Info("Requested for {0} elements", m - n);

            for (; i <= m; i++)
            {
                subscription.SendNext(i);
                var value = await reader.ReadAsync(cancel.Token);
                value.Should().Be(i);
            }
        }

        [Fact]
        public async Task ChannelSink_reader_should_deliver_final_element_when_channel_is_full_on_completion()
        {
            const int bufferSize = 4;
            const int elementCount = bufferSize + 1; // 0..4 — last element (4) lands while the channel is full

            var reader = Source.From(Enumerable.Range(0, elementCount))
                .RunWith(
                    ChannelSink.AsReader<int>(bufferSize, singleReader: true, BoundedChannelFullMode.Wait),
                    _materializer);

            // Intentionally do not read anything yet: the source fills the channel (0..3), grabs the
            // last element (4) whose write blocks on the full channel, then eagerly completes upstream.
            var received = new List<int>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (await reader.WaitToReadAsync(cts.Token))
                while (reader.TryRead(out var item))
                    received.Add(item);

            received.Should().Equal(Enumerable.Range(0, elementCount));
        }

        [Fact]
        public async Task ChannelSink_reader_should_deliver_all_elements_to_a_slow_consumer()
        {
            const int elementCount = 30;
            const int bufferSize = 4;

            var reader = Source.From(Enumerable.Range(0, elementCount))
                .RunWith(
                    ChannelSink.AsReader<int>(bufferSize, singleReader: true, BoundedChannelFullMode.Wait),
                    _materializer);

            var received = new List<int>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (await reader.WaitToReadAsync(cts.Token))
                while (reader.TryRead(out var item))
                    received.Add(item);

            received.Should().Equal(Enumerable.Range(0, elementCount));
        }

        #endregion
    }
}
