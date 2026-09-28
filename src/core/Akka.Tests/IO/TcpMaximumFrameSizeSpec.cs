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
    /// #8657: maximum-frame-size is the output pipe's segment size, so it bounds the bytes
    /// per stream write. With the 4 KiB default, a 64 KiB write lands on the stream as 16
    /// separate 4 KiB writes.
    /// </summary>
    public class TcpMaximumFrameSizeSpec : AkkaSpec
    {
        private const int WriteSize = 64 * 1024;

        public TcpMaximumFrameSizeSpec(ITestOutputHelper output)
            : base(@"akka.loglevel = DEBUG
                     akka.io.tcp.trace-logging = true", output: output)
        {
        }

        [Fact(DisplayName = "Should_split_a_write_into_4KiB_stream_writes_When_using_the_default_maximum_frame_size")]
        public async Task Should_split_a_write_into_4KiB_stream_writes_When_using_the_default_maximum_frame_size()
        {
            using var pair = await ConnectedSocketPair.CreateAsync();
            await using var stream = new BlockingWriteStream();
            var bindHandler = CreateTestProbe();
            var handler = CreateTestProbe();
            var settings = TcpSettings.Create(Sys);

            var connection = Sys.ActorOf(Props.Create(() => new TcpIncomingConnection(
                settings, pair.Server, bindHandler.Ref, Array.Empty<Inet.SocketOption>(), false, stream)));
            await bindHandler.ExpectMsgAsync<Tcp.Connected>();
            bindHandler.Send(connection, new Tcp.Register(handler.Ref));

            handler.Send(connection, Tcp.Write.Create(new byte[WriteSize]));
            stream.ReleaseFirstWrite();

            await AwaitAssertAsync(() => stream.WriteSizes.Should().Equal(Enumerable.Repeat(4096, 16)));

            await WatchAsync(connection);
            handler.Send(connection, Tcp.Abort.Instance);
            await handler.ExpectMsgAsync<Tcp.Aborted>();
            await ExpectTerminatedAsync(connection);
        }
    }

    /// <summary>
    /// #8657: raising maximum-frame-size to match a write's size collapses it back to a
    /// single stream write -- restoring the v1.5 behavior that #8132 dropped.
    /// </summary>
    public class TcpMaximumFrameSize64kSpec : AkkaSpec
    {
        private const int WriteSize = 64 * 1024;

        public TcpMaximumFrameSize64kSpec(ITestOutputHelper output)
            : base(@"akka.loglevel = DEBUG
                     akka.io.tcp.trace-logging = true
                     akka.io.tcp.maximum-frame-size = 64k
                     akka.io.tcp.send-buffer-size = 64k
                     akka.io.tcp.receive-buffer-size = 64k", output: output)
        {
        }

        [Fact(DisplayName = "Should_send_a_write_as_one_stream_write_When_maximum_frame_size_matches_its_length")]
        public async Task Should_send_a_write_as_one_stream_write_When_maximum_frame_size_matches_its_length()
        {
            using var pair = await ConnectedSocketPair.CreateAsync();
            await using var stream = new BlockingWriteStream();
            var bindHandler = CreateTestProbe();
            var handler = CreateTestProbe();
            var settings = TcpSettings.Create(Sys);

            var connection = Sys.ActorOf(Props.Create(() => new TcpIncomingConnection(
                settings, pair.Server, bindHandler.Ref, Array.Empty<Inet.SocketOption>(), false, stream)));
            await bindHandler.ExpectMsgAsync<Tcp.Connected>();
            bindHandler.Send(connection, new Tcp.Register(handler.Ref));

            handler.Send(connection, Tcp.Write.Create(new byte[WriteSize]));
            stream.ReleaseFirstWrite();

            await AwaitAssertAsync(() => stream.WriteSizes.Should().Equal(WriteSize));

            await WatchAsync(connection);
            handler.Send(connection, Tcp.Abort.Instance);
            await handler.ExpectMsgAsync<Tcp.Aborted>();
            await ExpectTerminatedAsync(connection);
        }
    }
}
