// -----------------------------------------------------------------------
// <copyright file="ArteryShutdownStageSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using Akka.Remote.Artery;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    public sealed class ArteryShutdownStageSpec : AkkaSpec
    {
        public ArteryShutdownStageSpec(ITestOutputHelper output) : base(output) { }

        [Theory(DisplayName = "Shutdown tail markers follow existing bytes and retain ownership until emission or cancellation")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_PreserveTailOwnership_When_DownstreamBackpressures(bool cancel)
        {
            var peer = new UniqueAddress(new Address("akka", "peer", "127.0.0.1", 2552), 42);
            var local = new UniqueAddress(new Address("akka", "local", "127.0.0.1", 2551), 11);
            var registry = new AssociationRegistry();
            var association = registry.AssociationFor(peer.Address);
            registry.CompleteHandshake(peer.Address, peer);
            var drain = new ArteryShutdownDrain();
            var owner = new CountingOwner();
            var prepared = new TaskCompletionSource<Done>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stage = new ArteryShutdownStage(drain, association, ArteryStreamId.Ordinary, _ =>
            {
                var tail = OwnedSequenceSegment.Create(owner);
                prepared.TrySetResult(Done.Instance);
                return tail;
            });
            var materializer = ActorMaterializer.Create(Sys);
            var (source, sink) = this.SourceProbe<ReadOnlySequence<byte>>()
                .Via(stage).ToMaterialized(this.SinkProbe<ReadOnlySequence<byte>>(), Keep.Both).Run(materializer);
            ReadOnlySequence<byte> emitted = default;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await sink.RequestAsync(1);
                await source.SendNextAsync(new ReadOnlySequence<byte>(new byte[] { 0x13 }));
                (await sink.ExpectNextAsync()).ToArray().Should().Equal(0x13);
                drain.Begin(local, candidate => candidate.Equals(peer)).Should().ContainSingle();
                await source.SendCompleteAsync();
                await prepared.Task.WaitAsync(deadline.Token);
                owner.Returns.Should().Be(0, "the marker is still parked behind downstream demand");
                if (cancel)
                {
                    await sink.CancelAsync();
                    await AwaitAssertAsync(() => owner.Returns.Should().Be(1));
                }
                else
                {
                    await sink.RequestAsync(1);
                    emitted = await sink.ExpectNextAsync();
                    emitted.ToArray().Should().Equal(0x7F);
                    await sink.ExpectCompleteAsync();
                    owner.Returns.Should().Be(0, "emission transfers ownership to the downstream writer");
                    emitted.DisposeOwnedSegments();
                    owner.Returns.Should().Be(1);
                }
                drain.Outstanding.Should().Be(1, "local stage stop is not a peer receipt");
            }
            finally
            {
                emitted.DisposeOwnedSegments();
                await sink.CancelAsync();
                drain.Close();
                materializer.Shutdown();
            }
        }

        private sealed class CountingOwner : IMemoryOwner<byte>
        {
            private readonly byte[] _bytes = [0x7F];
            private int _returns;
            public Memory<byte> Memory => _bytes;
            internal int Returns => Volatile.Read(ref _returns);
            public void Dispose()
            {
                Interlocked.Increment(ref _returns);
                Array.Fill(_bytes, (byte)0xEE);
            }
        }
    }
}
