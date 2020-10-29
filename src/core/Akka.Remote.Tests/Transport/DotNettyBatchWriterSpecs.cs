//-----------------------------------------------------------------------
// <copyright file="DotNettyBatchWriterSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Configuration;
using Akka.Remote.Transport.DotNetty;
using Akka.TestKit;
using DotNetty.Buffers;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Embedded;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Remote.Tests.Transport
{
    public class DotNettyBatchWriterSpecs : AkkaSpec
    {
        public class FlushLogger : ChannelHandlerAdapter
        {
            public ITestOutputHelper Helper;

            private TaskCompletionSource<Done> _tcs = new TaskCompletionSource<Done>();

            public Task Activated => _tcs.Task;

            public FlushLogger(ITestOutputHelper helper)
            {
                Helper = helper;
            }

            public override void ChannelActive(IChannelHandlerContext context)
            {
                _tcs.TrySetResult(Done.Instance);
                base.ChannelActive(context);
            }

            public override void Flush(IChannelHandlerContext context)
            {
                Helper.WriteLine($"[{DateTime.UtcNow}] flushed");

                base.Flush(context);
            }
        }

        public FlushLogger Flush { get; }

        public DotNettyBatchWriterSpecs(ITestOutputHelper helper) : base(helper)
        {
            Flush = new FlushLogger(helper);
        }

        [Fact]
        public void Bugfix4434_should_overwrite_default_BatchWriterSettings()
        {
            Config c = @"
                akka.remote.dot-netty.tcp{
                    batching{
                        enabled = false
                        max-pending-writes = 50
                        max-pending-bytes = 32k
                        flush-interval = 10ms
                    }
                }
            ";
            var s = DotNettyTransportSettings.Create(c.GetConfig("akka.remote.dot-netty.tcp"));

            s.BatchWriterSettings.EnableBatching.Should().BeFalse();
            s.BatchWriterSettings.FlushInterval.Should().NotBe(BatchWriterSettings.DefaultFlushInterval);
            s.BatchWriterSettings.MaxPendingBytes.Should().NotBe(BatchWriterSettings.DefaultMaxPendingBytes);
            s.BatchWriterSettings.MaxPendingWrites.Should().NotBe(BatchWriterSettings.DefaultMaxPendingWrites);
        }

        /// <summary>
        /// Stay below the write / count and write / byte threshold. Rely on the timer.
        /// </summary>
        [Fact]
        public async Task BatchWriter_should_succeed_with_timer()
        {
            var writer = new BatchWriter(new BatchWriterSettings());
            var ch = new EmbeddedChannel(Flush, writer);

            await Flush.Activated;

            /*
             * Run multiple iterations to ensure that the batching mechanism doesn't become stale
             */
            foreach (var n in Enumerable.Repeat(0, 3))
            {
                var ints = Enumerable.Range(0, 4).ToArray();
                foreach (var i in ints)
                {
                    _ = ch.WriteAsync(Unpooled.Buffer(1).WriteInt(i));
                }

                // force write tasks to run
                ch.RunPendingTasks();

                ch.Unsafe.OutboundBuffer.TotalPendingWriteBytes().Should().Be(ints.Length * 4);
                ch.OutboundMessages.Count.Should().Be(0);

                await AwaitAssertAsync(() =>
                {
                    ch.RunPendingTasks(); // force scheduled task to run
                    ch.OutboundMessages.Count.Should().Be(ints.Length);
                }, interval: 100.Milliseconds());

                // reset the outbound queue
                ch.OutboundMessages.Clear();
            }
        }

        /// <summary>
        /// Stay below the write / count and write / byte threshold. Rely on the timer.
        /// </summary>
        [Fact]
        public async Task BatchWriter_should_flush_messages_during_shutdown()
        {
            var writer = new BatchWriter(new BatchWriterSettings());
            var ch = new EmbeddedChannel(Flush, writer);

            await Flush.Activated;

            /*
             * Run multiple iterations to ensure that the batching mechanism doesn't become stale
             */

            var ints = Enumerable.Range(0, 10).ToArray();
            foreach (var i in ints)
            {
                _ = ch.WriteAsync(Unpooled.Buffer(1).WriteInt(i));
            }

            // force write tasks to run
            ch.RunPendingTasks();

            ch.Unsafe.OutboundBuffer.TotalPendingWriteBytes().Should().Be(ints.Length * 4);
            ch.OutboundMessages.Count.Should().Be(0);

            // close channels
            _ = ch.CloseAsync();

            await AwaitAssertAsync(() =>
            {
                ch.RunPendingTasks(); // force scheduled task to run
                ch.OutboundMessages.Count.Should().Be(ints.Length);
            }, interval: 100.Milliseconds());


        }
    }
}
