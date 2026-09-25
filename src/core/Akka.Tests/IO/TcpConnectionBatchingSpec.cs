//-----------------------------------------------------------------------
// <copyright file="TcpConnectionBatchingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.IO
{
    public class TcpConnectionBatchingSpec : AkkaSpec
    {
        private sealed class WriteAck : Tcp.Event
        {
            public WriteAck(int id)
            {
                Id = id;
            }

            public int Id { get; }
        }

        public TcpConnectionBatchingSpec(ITestOutputHelper output)
            : base(@"akka.loglevel = DEBUG
                     akka.io.tcp.trace-logging = true", output: output)
        {
        }

        [Fact]
        public async Task TcpConnection_should_batch_small_writes_that_arrive_while_a_previous_write_is_in_flight()
        {
            using var socketPair = await ConnectedSocketPair.CreateAsync();
            await using var stream = new BlockingWriteStream();
            var bindHandler = CreateTestProbe();
            var handler = CreateTestProbe();
            var settings = TcpSettings.Create(Sys);

            var connection = Sys.ActorOf(Props.Create(() => new TcpIncomingConnection(
                settings,
                socketPair.Server,
                bindHandler.Ref,
                Array.Empty<Inet.SocketOption>(),
                false,
                stream)));

            await bindHandler.ExpectMsgAsync<Tcp.Connected>();
            bindHandler.Send(connection, new Tcp.Register(handler.Ref));

            handler.Send(connection, Tcp.Write.Create(new byte[32].AsMemory(), new WriteAck(1)));
            await stream.FirstWriteStarted.WaitAsync(TimeSpan.FromSeconds(3));

            handler.Send(connection, Tcp.Write.Create(new byte[32].AsMemory(), new WriteAck(2)));
            handler.Send(connection, Tcp.Write.Create(new byte[32].AsMemory(), new WriteAck(3)));
            handler.Send(connection, Tcp.Write.Create(new byte[32].AsMemory(), new WriteAck(4)));

            // An ack fires once the bytes are in the output pipe and the pipe is under its pause
            // threshold, not when the stream write completes. These small writes stay under it.
            (await handler.ExpectMsgAsync<WriteAck>()).Id.Should().Be(1);
            (await handler.ExpectMsgAsync<WriteAck>()).Id.Should().Be(2);
            (await handler.ExpectMsgAsync<WriteAck>()).Id.Should().Be(3);
            (await handler.ExpectMsgAsync<WriteAck>()).Id.Should().Be(4);

            stream.ReleaseFirstWrite();

            // The write pump still batches at the stream level: the first write (32 bytes)
            // was already in-flight when writes #2-4 arrived, so those accumulate in the
            // pipe buffer and get flushed as a single 96-byte write to the stream.
            await AwaitAssertAsync(() =>
            {
                stream.WriteSizes.Should().Equal(32, 96);
                return Task.CompletedTask;
            }, TimeSpan.FromSeconds(3));

            await WatchAsync(connection);
            handler.Send(connection, Tcp.Abort.Instance);
            await handler.ExpectMsgAsync<Tcp.Aborted>();
            await ExpectTerminatedAsync(connection);
        }
    }
}
