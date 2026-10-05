//-----------------------------------------------------------------------
// <copyright file="TcpTlsBackpressureSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Akka.IO;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using TcpListener = System.Net.Sockets.TcpListener;

namespace Akka.Tests.IO
{
    /// <summary>
    /// Exercises bounded queued writes and slow-reader flow control through the real TLS transport.
    /// </summary>
    public class TcpTlsBackpressureSpec : AkkaSpec
    {
        private const int WriteSize = 512 * 1024;
        private const int QueueLimit = WriteSize * 2;
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(15);

        private sealed class WriteAck : Tcp.Event
        {
            public WriteAck(int id) => Id = id;
            public int Id { get; }
        }

        private sealed class CountingMemoryOwner : IMemoryOwner<byte>
        {
            private byte[]? _buffer;
            private int _disposeCount;

            public CountingMemoryOwner(byte[] buffer) => _buffer = buffer;

            public Memory<byte> Memory => _buffer is null ? Memory<byte>.Empty : new Memory<byte>(_buffer);
            public int DisposeCount => Volatile.Read(ref _disposeCount);

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _buffer, null) is not null)
                    Interlocked.Increment(ref _disposeCount);
            }
        }

        public TcpTlsBackpressureSpec(ITestOutputHelper output)
            : base("akka.loglevel = DEBUG\nakka.io.tcp.trace-logging = true", output: output)
        {
        }

        [Fact(DisplayName = "Should_bound_owned_queued_writes_and_deliver_them_after_a_TLS_slow_reader_is_released")]
        public async Task Should_bound_owned_queued_writes_and_deliver_them_after_a_tls_slow_reader_is_released()
        {
            using var certificate = CreateCertificate();
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            var readerRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var peerAuthenticated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var peerClose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var peerPayload = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstPayload = CreatePayload(31, WriteSize);
            var secondPayload = CreatePayload(32, WriteSize);
            var expected = new byte[QueueLimit];
            firstPayload.CopyTo(expected, 0);
            secondPayload.CopyTo(expected, firstPayload.Length);
            var peerTask = ReadAfterReleaseAsync(listener, certificate, peerAuthenticated, readerRelease.Task,
                peerClose.Task, peerPayload, expected.Length);

            var settings = TcpSettings.Create(Sys) with { WriteCommandsQueueMaxSize = QueueLimit };
            var commander = CreateTestProbe();
            var connect = new Tcp.Connect(endpoint, options: new Inet.SocketOption[]
            {
                new Inet.SO.SendBufferSize(1024),
                new Inet.SO.PipeBufferSize(1024)
            }, timeout: TestTimeout)
            {
                TcpSettings = settings,
                Tls = new TlsClientSettings
                {
                    RequireMutualAuthentication = false,
                    TargetHost = "localhost",
                    HandshakeTimeout = TestTimeout,
                    CustomValidator = (_, _, _, _, _) => true
                }
            };

            commander.Send(Sys.Tcp(), connect);
            await commander.ExpectMsgAsync<Tcp.Connected>(TestTimeout);
            var connection = commander.LastSender;
            await peerAuthenticated.Task.WaitAsync(TestTimeout);

            var firstOwner = new CountingMemoryOwner(firstPayload);
            var secondOwner = new CountingMemoryOwner(secondPayload);
            var rejectedOwner = new CountingMemoryOwner(CreatePayload(33, 1));
            var handler = CreateTestProbe();
            var firstWrite = Tcp.Write.Create(OwnedSequenceSegment.Create(firstOwner, firstPayload.Length), new WriteAck(1));
            var secondWrite = Tcp.Write.Create(OwnedSequenceSegment.Create(secondOwner, secondPayload.Length), new WriteAck(2));
            var rejectedWrite = Tcp.Write.Create(OwnedSequenceSegment.Create(rejectedOwner, 1), new WriteAck(3));

            // Accumulate exactly the 1 MiB queue limit before registration. The final one-byte
            // owned write is rejected only because the earlier accepted writes occupy the cap.
            handler.Send(connection, firstWrite);
            handler.Send(connection, secondWrite);
            handler.Send(connection, rejectedWrite);

            var failed = await handler.ExpectMsgAsync<Tcp.CommandFailed>(TestTimeout);
            failed.Cmd.Should().BeSameAs(rejectedWrite);
            failed.Cause.Value.Should().BeOfType<IOException>();
            rejectedOwner.DisposeCount.Should().Be(1, "the bounded queue rejects ownership before registration");
            firstOwner.DisposeCount.Should().Be(0);
            secondOwner.DisposeCount.Should().Be(0);

            // The raw TLS peer is authenticated, but deliberately does not read application data
            // until this gate opens. Once registered, the 1 MiB accepted payload exceeds the
            // socket and pipe watermarks. The bounded no-message assertion confirms output acks
            // stay pending while the peer refuses to read.
            commander.Send(connection, new Tcp.Register(handler.Ref));
            await AwaitAssertAsync(() =>
            {
                firstOwner.DisposeCount.Should().Be(1);
                secondOwner.DisposeCount.Should().Be(1);
                return Task.CompletedTask;
            }, TestTimeout);
            await handler.ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
            readerRelease.TrySetResult();

            (await handler.ExpectMsgAsync<WriteAck>(TestTimeout)).Id.Should().Be(1);
            (await handler.ExpectMsgAsync<WriteAck>(TestTimeout)).Id.Should().Be(2);
            firstOwner.DisposeCount.Should().Be(1);
            secondOwner.DisposeCount.Should().Be(1);
            (await peerPayload.Task.WaitAsync(TestTimeout)).Should().Equal(expected,
                "the accepted owned writes must arrive in order and the rejected byte must never reach the TLS peer");

            await WatchAsync(connection);
            handler.Send(connection, Tcp.Abort.Instance);
            await handler.ExpectMsgAsync<Tcp.Aborted>(TestTimeout);
            await ExpectTerminatedAsync(connection, TestTimeout);
            peerClose.TrySetResult();
            await peerTask.WaitAsync(TestTimeout);
        }

        private static async Task ReadAfterReleaseAsync(
            TcpListener listener,
            X509Certificate2 certificate,
            TaskCompletionSource peerAuthenticated,
            Task readerRelease,
            Task peerClose,
            TaskCompletionSource<byte[]> peerPayload,
            int count)
        {
            using var cancellation = new CancellationTokenSource(TestTimeout);
            using var socket = await listener.AcceptSocketAsync(cancellation.Token);
            socket.ReceiveBufferSize = 16 * 1024;
            using var networkStream = new NetworkStream(socket, ownsSocket: false);
            using var ssl = new SslStream(networkStream, leaveInnerStreamOpen: true);
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate,
                EnabledSslProtocols = SslProtocols.Tls12
            }, cancellation.Token);
            peerAuthenticated.TrySetResult();

            // This is the coordinated slow-reader gate: handshake completes immediately while
            // application reads remain stopped until the test has filled/rejected from the queue.
            await readerRelease.WaitAsync(cancellation.Token);
            var received = new byte[count];
            var offset = 0;
            while (offset < received.Length)
            {
                var read = await ssl.ReadAsync(received.AsMemory(offset), cancellation.Token);
                if (read == 0)
                    throw new EndOfStreamException($"TLS peer closed after {offset} of {count} expected bytes");
                offset += read;
            }

            peerPayload.TrySetResult(received);
            await peerClose.WaitAsync(cancellation.Token);
        }

        private static byte[] CreatePayload(int seed, int length)
        {
            var bytes = new byte[length];
            var random = new Random(seed);
            random.NextBytes(bytes);
            return bytes;
        }

        private static X509Certificate2 CreateCertificate()
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            var san = new SubjectAlternativeNameBuilder();
            san.AddDnsName("localhost");
            request.CertificateExtensions.Add(san.Build());
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
            return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null);
        }
    }
}
