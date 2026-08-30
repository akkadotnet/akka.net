//-----------------------------------------------------------------------
// <copyright file="OwnedSequenceSegmentDisposalBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Buffers;
using Akka.Benchmarks.Configurations;
using Akka.Benchmarks.Remoting.Artery;
using Akka.IO;
using Akka.Serialization;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.IO
{
    /// <summary>
    /// Sizes <c>Akka.IO.OwnedSequenceSegmentExtensions.DisposeOwnedSegments</c>'s per-flush walk --
    /// the hot-path addition PR1 (the ownership-carrying-<c>Tcp.Write</c> mechanism) put on EVERY
    /// buffered write, whether or not it actually carries a pooled owner. Two chain shapes per
    /// segment count <c>N</c>:
    /// <list type="bullet">
    /// <item><description>
    /// <b>All-borrowed</b> (<see cref="Walk_AllBorrowed"/>): a chain of plain
    /// <see cref="System.Buffers.ReadOnlySequenceSegment{T}"/> links (<see cref="ChainedSegment"/>,
    /// reused from <see cref="ArteryFrameCorpus"/>) that do NOT implement
    /// <c>IOwnedSequenceSegment</c> -- the walk's <c>is IOwnedSequenceSegment</c> check fails on
    /// every link, so this is the copy-baseline / non-Artery-write case: the walk finds nothing to
    /// dispose and is pure per-link overhead (pointer chase + failed type test). This is the PR1
    /// "contamination candidate" -- every write-coalescing flush pays this walk even when nothing
    /// on the chain is pool-owned.
    /// </description></item>
    /// <item><description>
    /// <b>All-owned</b> (<see cref="Walk_AllOwned"/>): a chain of real
    /// <see cref="Akka.IO.OwnedSequenceSegment"/> links, each carrying a real
    /// <see cref="System.Buffers.IMemoryOwner{T}"/> minted the same way production does
    /// (<c>PooledPayloadWriter.Detach()</c>) -- the zero-copy case where the walk does real
    /// disposal work (return each rented buffer to its pool) on every link.
    /// </description></item>
    /// </list>
    /// <para>
    /// <b>Chain reuse across invocations.</b> Both chains are built ONCE per <c>N</c> in
    /// <see cref="GlobalSetup"/> and re-walked on every BenchmarkDotNet invocation rather than
    /// rebuilt per call (rebuilding would fold allocation/dispose cost from chain CONSTRUCTION into
    /// what is supposed to be a pure walk measurement). <c>DisposeOwner()</c> is idempotent -- after
    /// the first invocation's real disposal, every subsequent invocation on the all-owned chain
    /// re-walks already-<see langword="null"/>-owner segments. This is intentional and does not
    /// distort ns/op: <c>DisposeOwner()</c>'s own body (read field, null it, null-conditional
    /// <c>Dispose()</c>) executes the identical instructions whether or not an owner was actually
    /// present -- the only thing that changes is whether <c>IMemoryOwner&lt;byte&gt;.Dispose()</c>
    /// (an uncontended <c>ArrayPool</c> return, itself non-allocating) actually runs. What this
    /// benchmark measures -- and what answers the "is the walk cheap" question -- is dominated by
    /// the per-segment pointer-chase and <c>is IOwnedSequenceSegment</c> type test, not that one
    /// call's marginal cost.
    /// </para>
    /// <para>Run on b297bc572 (treatment) only -- both segment types (and this exact extension method) already exist there; this is a hot-path characterization, not an A/B commit comparison.</para>
    /// </summary>
    [Config(typeof(MicroBenchmarkConfig))]
    public class OwnedSequenceSegmentDisposalBenchmarks
    {
        private const int SegmentSize = 256;

        [Params(1, 4, 16, 64)]
        public int SegmentCount { get; set; }

        private ReadOnlySequence<byte> _borrowed;
        private ReadOnlySequence<byte> _owned;

        [GlobalSetup]
        public void GlobalSetup()
        {
            _borrowed = BuildBorrowedChain(SegmentCount, SegmentSize);
            _owned = BuildOwnedChain(SegmentCount, SegmentSize);
        }

        private static ReadOnlySequence<byte> BuildBorrowedChain(int segmentCount, int segmentSize)
        {
            var bytes = new byte[segmentSize];
            var first = new ChainedSegment(bytes, 0);
            var last = first;
            for (var i = 1; i < segmentCount; i++)
                last = last.Append(bytes);

            return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
        }

        private static ReadOnlySequence<byte> BuildOwnedChain(int segmentCount, int segmentSize)
        {
            var owner0 = RentOwner(segmentSize);
            var first = new OwnedSequenceSegment(owner0.Memory, owner0);
            var last = first;
            for (var i = 1; i < segmentCount; i++)
            {
                var owner = RentOwner(segmentSize);
                last = last.Append(owner.Memory, owner);
            }

            return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
        }

        /// <summary>Mints a real, production-shaped pooled owner (same <c>PooledPayloadWriter.Detach()</c> path <c>ArteryEncodeStage</c> uses).</summary>
        private static IMemoryOwner<byte> RentOwner(int size)
        {
            var writer = new PooledPayloadWriter(size);
            writer.GetSpan(size);
            writer.Advance(size);
            return writer.Detach();
        }

        /// <summary>The copy-baseline / non-Artery-write case: nothing on the chain implements <c>IOwnedSequenceSegment</c>, so the walk is pure per-link overhead.</summary>
        [Benchmark(Baseline = true)]
        public void Walk_AllBorrowed() => _borrowed.DisposeOwnedSegments();

        /// <summary>The zero-copy case: every link carries a real pooled owner the walk actually disposes.</summary>
        [Benchmark]
        public void Walk_AllOwned() => _owned.DisposeOwnedSegments();
    }
}
