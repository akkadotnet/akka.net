using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Remote.Transport.DotNetty;
using DotNetty.Buffers;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Embedded;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Transport
{
    public class DotNettyBatchWriterSpecs
    {
        class FlushHandler : ChannelHandlerAdapter
        {
            public ChannelOutboundBuffer Buffer { get; private set; }

            public override void Flush(IChannelHandlerContext context)
            {
                Buffer = context.Channel.Unsafe.OutboundBuffer;
            }
        }

        /// <summary>
        /// Stay below the write / count and write / byte threshold. Rely on the timer.
        /// </summary>
        [Fact]
        public async Task BatchWriter_should_succeed_with_timer()
        {
            var writer = new BatchWriter();
            var flushHandler = new FlushHandler();
            var ch = new EmbeddedChannel(writer);
            
            var ints = Enumerable.Range(0, 4).ToArray();
            foreach(var i in ints)
            {
                ch.WriteAsync(Unpooled.Buffer(1).WriteInt(i));
            }

            ch.Unsafe.OutboundBuffer.TotalPendingWriteBytes().Should().Be(0);
            ch.RunPendingTasks();
            await Task.Delay(writer.MaxPendingMillis * 2);
            ch.OutboundMessages.Count.Should().Be(ints.Length);
        }
    }
}
