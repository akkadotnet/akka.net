// -----------------------------------------------------------------------
// <copyright file="TcpTransportTlsShutdownSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Akka.IO;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.IO
{
    public sealed class TcpTransportTlsShutdownSpec : AkkaSpec
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

        public TcpTransportTlsShutdownSpec(ITestOutputHelper output) : base(output)
        {
        }

        [Theory(DisplayName = "Should_Wait_For_Active_TLS_Read_Before_Shutdown_And_Preserve_Input_When_Half_Closed")]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Should_wait_for_active_tls_read_before_shutdown_and_preserve_input_when_half_closed(
            bool halfClose)
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await using var stream = new CoordinatedSslStream();
            await using var transport = new TcpTransportConnection(socket, stream);
            using var cancellation = new CancellationTokenSource(TestTimeout);
            await transport.InitializeAsync(cancellation.Token);
            transport.Start();
            await stream.ReadEntered.Task.WaitAsync(cancellation.Token);

            var shutdown = halfClose ? transport.ShutdownAsync() : transport.CloseAsync();
            try
            {
                await stream.ReadCanceled.Task.WaitAsync(cancellation.Token);
                stream.ShutdownCalls.Should().Be(0, "cancellation alone does not mean the TLS read has exited");
            }
            finally
            {
                stream.AllowReadExit.TrySetResult();
            }

            await shutdown.WaitAsync(cancellation.Token);
            stream.ShutdownCalls.Should().Be(1);

            if (halfClose)
            {
                await stream.ResumedReadEntered.Task.WaitAsync(cancellation.Token);
                stream.Reply.TrySetResult();
                var result = await transport.Input.ReadAsync(cancellation.Token);
                result.Buffer.ToArray().Should().Equal(CoordinatedSslStream.Payload);
                transport.Input.AdvanceTo(result.Buffer.End);
            }

            await transport.ReadCompleted.WaitAsync(cancellation.Token);
            transport.HasReadError.Should().BeFalse();
        }

        private sealed class CoordinatedSslStream : SslStream
        {
            public static readonly byte[] Payload = { 1, 2, 3, 4 };
            private int _readCalls;
            private int _activeReads;

            public CoordinatedSslStream() : base(new MemoryStream())
            {
            }

            public TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource ReadCanceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource AllowReadExit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource ResumedReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource Reply { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public int ShutdownCalls { get; private set; }

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                var readCall = Interlocked.Increment(ref _readCalls);
                Interlocked.Increment(ref _activeReads);
                try
                {
                    if (readCall == 1)
                    {
                        using var registration = cancellationToken.Register(() => ReadCanceled.TrySetResult());
                        ReadEntered.TrySetResult();
                        await ReadCanceled.Task;
                        await AllowReadExit.Task;
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    else if (readCall == 2)
                    {
                        ResumedReadEntered.TrySetResult();
                        await Reply.Task.WaitAsync(cancellationToken);
                        Payload.CopyTo(buffer);
                        return Payload.Length;
                    }

                    return 0;
                }
                finally
                {
                    Interlocked.Decrement(ref _activeReads);
                }
            }

            public override Task ShutdownAsync()
            {
                Volatile.Read(ref _activeReads).Should().Be(0, "TLS shutdown must run after the active read exits");
                ShutdownCalls++;
                return Task.CompletedTask;
            }
        }
    }
}
