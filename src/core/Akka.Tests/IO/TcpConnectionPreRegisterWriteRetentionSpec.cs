//-----------------------------------------------------------------------
// <copyright file="TcpConnectionPreRegisterWriteRetentionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.IO
{
    /// <summary>
    /// Guards the buffer-lifetime invariant documented in <see cref="TcpConnection"/>'s
    /// EnqueueWrite / BufferSingleWriteBeforeRegister: TcpConnection must never retain a
    /// caller-owned <c>ReadOnlySequence&lt;byte&gt;</c> past the message-handler turn in which the
    /// Write was received. A Write that arrives before Register is buffered until Register flushes
    /// it - if the buffered command referenced the caller's memory as-is instead of a copy, a
    /// caller reusing a pooled buffer (the whole point of the ReadOnlySequence-based Write surface)
    /// could silently corrupt bytes that haven't been written to the socket yet.
    /// </summary>
    public class TcpConnectionPreRegisterWriteRetentionSpec : AkkaSpec
    {
        private sealed class WriteAck : Tcp.Event
        {
            public WriteAck(int id)
            {
                Id = id;
            }

            public int Id { get; }
        }

        public TcpConnectionPreRegisterWriteRetentionSpec(ITestOutputHelper output)
            : base(@"akka.loglevel = DEBUG
                     akka.io.tcp.trace-logging = true", output: output)
        {
        }

        [Fact]
        public async Task PreRegistration_write_must_be_copied_at_enqueue_not_retained_by_reference()
        {
            using var socketPair = await ConnectedSocketPair.CreateAsync();
            var bindHandler = CreateTestProbe();
            var handler = CreateTestProbe();
            var ackProbe = CreateTestProbe();
            var settings = TcpSettings.Create(Sys);

            // TestActorRef installs the CallingThreadDispatcher: Tell(...) synchronously drives
            // the actor's Receive handler to completion on this thread before returning. That
            // determinism is essential here - it lets us mutate the source buffer immediately
            // after Tell() returns and know for certain whether TcpConnection already copied the
            // bytes out (fixed) or is still holding a reference into our buffer (the hazard).
            var connection = new TestActorRef<TcpIncomingConnection>(Sys, Props.Create(() => new TcpIncomingConnection(
                settings,
                socketPair.Server,
                bindHandler.Ref,
                Array.Empty<Inet.SocketOption>(),
                false)));

            await bindHandler.ExpectMsgAsync<Tcp.Connected>();

            var original = new byte[256];
            new Random(12345).NextBytes(original);
            var poisonableBuffer = (byte[])original.Clone();

            // Send the write BEFORE Register - this is the pre-registration buffering path
            // (BufferWriteBeforeRegister -> BufferSingleWriteBeforeRegister). Because Tell() on a
            // TestActorRef runs synchronously, by the time it returns the actor has already
            // enqueued (and, if fixed, copied) the write.
            connection.Tell(Tcp.Write.Create(poisonableBuffer, new WriteAck(1)), ackProbe.Ref);

            // Poison the source buffer right away - simulates the caller returning a pooled
            // buffer to its pool, or otherwise reusing/mutating it, immediately after the Tell()
            // call returns and well before Register is ever sent.
            Array.Clear(poisonableBuffer, 0, poisonableBuffer.Length);

            // Now register the handler. This triggers FlushPendingRegistrationWrites, which
            // writes the buffered command to the transport.
            bindHandler.Send(connection, new Tcp.Register(handler.Ref));

            await ackProbe.ExpectMsgAsync<WriteAck>();

            var received = await ReceiveExactAsync(socketPair.Client, original.Length, TimeSpan.FromSeconds(5));
            received.Should().Equal(original,
                "a pre-registration write must be copied at enqueue time - the caller's buffer may be reused at any point after Tell() returns");

            await WatchAsync(connection);
            handler.Send(connection, Tcp.Abort.Instance);
            await handler.ExpectMsgAsync<Tcp.Aborted>();
            await ExpectTerminatedAsync(connection);
        }

        [Fact]
        public async Task Open_phase_write_must_be_copied_before_WriteAck_is_sent()
        {
            // Inverse guard: documents the WriteAck contract on the normal (post-Register) path.
            // By the time the caller observes WriteAck, EnqueueWrite has already synchronously
            // copied the data into the transport's pipe (see TcpTransportConnection.WriteAsync),
            // so mutating the source buffer right after the ack must NOT affect what's received.
            using var socketPair = await ConnectedSocketPair.CreateAsync();
            var bindHandler = CreateTestProbe();
            var handler = CreateTestProbe();
            var settings = TcpSettings.Create(Sys);

            var connection = Sys.ActorOf(Props.Create(() => new TcpIncomingConnection(
                settings,
                socketPair.Server,
                bindHandler.Ref,
                Array.Empty<Inet.SocketOption>(),
                false)));

            await bindHandler.ExpectMsgAsync<Tcp.Connected>();
            bindHandler.Send(connection, new Tcp.Register(handler.Ref));

            var original = new byte[256];
            new Random(54321).NextBytes(original);
            var poisonableBuffer = (byte[])original.Clone();

            handler.Send(connection, Tcp.Write.Create(poisonableBuffer, new WriteAck(1)));

            (await handler.ExpectMsgAsync<WriteAck>()).Id.Should().Be(1);
            Array.Clear(poisonableBuffer, 0, poisonableBuffer.Length);

            var received = await ReceiveExactAsync(socketPair.Client, original.Length, TimeSpan.FromSeconds(5));
            received.Should().Equal(original,
                "WriteAck must only be sent after the data has already been copied out of the caller's buffer");

            await WatchAsync(connection);
            handler.Send(connection, Tcp.Abort.Instance);
            await handler.ExpectMsgAsync<Tcp.Aborted>();
            await ExpectTerminatedAsync(connection);
        }

        private static async Task<byte[]> ReceiveExactAsync(Socket socket, int count, TimeSpan timeout)
        {
            using var stream = new NetworkStream(socket, ownsSocket: false);
            using var cts = new CancellationTokenSource(timeout);
            var buffer = new byte[count];
            var received = 0;
            while (received < count)
            {
                var n = await stream.ReadAsync(buffer.AsMemory(received, count - received), cts.Token);
                if (n == 0)
                    throw new IOException($"Socket closed after receiving {received} of {count} expected bytes.");
                received += n;
            }

            return buffer;
        }

        private sealed class ConnectedSocketPair : IDisposable
        {
            private ConnectedSocketPair(Socket client, Socket server)
            {
                Client = client;
                Server = server;
            }

            public Socket Client { get; }
            public Socket Server { get; }

            public static async Task<ConnectedSocketPair> CreateAsync()
            {
                using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                listener.Listen(1);

                var endpoint = (IPEndPoint)listener.LocalEndPoint!;
                var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                try
                {
                    var connectTask = client.ConnectAsync(endpoint);
                    var server = await listener.AcceptAsync();
                    await connectTask;
                    return new ConnectedSocketPair(client, server);
                }
                catch
                {
                    client.Dispose();
                    throw;
                }
            }

            public void Dispose()
            {
                Client.Dispose();
                Server.Dispose();
            }
        }
    }
}
