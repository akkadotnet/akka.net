//-----------------------------------------------------------------------
// <copyright file="TcpMaximumFrameSizeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.IO
{
    /// <summary>
    /// #8657: maximum-frame-size bounds the bytes per socket send, as it did in v1.5.
    /// </summary>
    public sealed class TcpMaximumFrameSizeSpec : AkkaSpec
    {
        private const int WriteSize = 64 * 1024;

        public TcpMaximumFrameSizeSpec(ITestOutputHelper output) : base(output)
        {
        }

        [Theory(DisplayName = "Should_split_a_write_into_maximum_frame_size_sends_When_the_write_is_larger")]
        [InlineData(4 * 1024, 16)]
        [InlineData(64 * 1024, 1)]
        public async Task Should_split_a_write_into_maximum_frame_size_sends_When_the_write_is_larger(int maxFrameSize, int sends)
        {
            using var pair = await ConnectedSocketPair.CreateAsync();
            await using var stream = new BlockingWriteStream();
            var bindHandler = CreateTestProbe();
            var handler = CreateTestProbe();
            var settings = TcpSettings.Create(Sys) with
            {
                MaxFrameSizeBytes = maxFrameSize, SendBufferSize = maxFrameSize, ReceiveBufferSize = maxFrameSize
            };

            var connection = Sys.ActorOf(Props.Create(() => new TcpIncomingConnection(
                settings, pair.Server, bindHandler.Ref, Array.Empty<Inet.SocketOption>(), false, stream)));
            await bindHandler.ExpectMsgAsync<Tcp.Connected>();
            bindHandler.Send(connection, new Tcp.Register(handler.Ref));

            handler.Send(connection, Tcp.Write.Create(new byte[WriteSize]));
            stream.ReleaseFirstWrite();

            await AwaitAssertAsync(() => stream.WriteSizes.Should().Equal(Enumerable.Repeat(maxFrameSize, sends)));

            await WatchAsync(connection);
            handler.Send(connection, Tcp.Abort.Instance);
            await handler.ExpectMsgAsync<Tcp.Aborted>();
            await ExpectTerminatedAsync(connection);
        }
    }
}
