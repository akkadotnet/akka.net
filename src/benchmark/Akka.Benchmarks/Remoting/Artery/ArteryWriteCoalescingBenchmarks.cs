//-----------------------------------------------------------------------
// <copyright file="ArteryWriteCoalescingBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Net;
using System.Reflection;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.IO;
using Akka.Serialization;
using Akka.Streams;
using Akka.Streams.Implementation.IO;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Remoting.Artery
{
    /// <summary>
    /// Isolates Akka.Streams' outbound TCP write-coalescing path --
    /// <c>Akka.Streams.Implementation.IO.TcpConnectionStage.TcpStreamLogic.AppendToWriteBuffer</c>
    /// + <c>.DrainWriteBuffer</c> -- the REAL production private methods on the REAL production
    /// type, invoked directly. Both methods are <c>private</c> on an <c>internal</c> nested class
    /// tied to <see cref="Akka.Streams.Stage.GraphStageLogic"/>'s port-connection machinery, so
    /// they cannot be called from outside the class body even with InternalsVisibleTo; this
    /// benchmark binds an OPEN INSTANCE DELEGATE to each via reflection ONCE in
    /// <see cref="GlobalSetup"/> (<c>MethodInfo.CreateDelegate</c>), so the per-op hot path pays
    /// only a direct delegate call -- no per-call reflection, no boxing of the
    /// <see cref="ReadOnlySequence{T}"/> struct argument. <see cref="TcpStreamLogic"/> is
    /// constructed via its <c>public</c> constructor with no live connection actor / interpreter
    /// attached (<see cref="ActorRefs.Nobody"/> stands in for the connection <see cref="IActorRef"/>)
    /// -- <c>AppendToWriteBuffer</c>/<c>DrainWriteBuffer</c> touch only the buffer's own
    /// head/tail/byte-count fields, never the connection or the graph interpreter, so this is a
    /// faithful, ActorSystem-free isolation of exactly the two methods under test.
    /// <para>
    /// <b>Identical source runs on both A/B commits</b> (293c5835d baseline, b297bc572 treatment)
    /// because both already ship <c>OwnedSequenceSegment.Create</c> -- what differs is what
    /// <c>AppendToWriteBuffer</c> DOES with a segment-backed input: baseline's implementation
    /// ignores segment identity entirely and does <c>memory.ToArray()</c> (an O(frame) copy) for
    /// every accumulated element regardless of how it's backed; treatment's implementation branches
    /// on <c>data.Start.GetObject() is ReadOnlySequenceSegment&lt;byte&gt;</c> and, when true (as it
    /// always is here), zero-copy <c>DetachOwner()</c>s straight into its own write-buffer segment.
    /// </para>
    /// <para>
    /// <b>Two benchmarks, so the frame-minting cost is visible and subtractable.</b>
    /// <see cref="MintFrames_Only"/> mints <see cref="FrameCount"/> fresh owner-carrying frames
    /// (<c>PooledPayloadWriter.Detach()</c> + <c>OwnedSequenceSegment.Create</c> -- IDENTICAL API
    /// on both commits, unrelated to the coalescing diff) and disposes them immediately, WITHOUT
    /// touching <c>TcpStreamLogic</c> at all -- this is the [Benchmark(Baseline = true)] reference
    /// so BenchmarkDotNet's own ratio column already shows the incremental cost.
    /// <see cref="MintAppendDrain"/> does the same minting, then feeds all frames through the real
    /// <c>AppendToWriteBuffer</c> and one real <c>DrainWriteBuffer</c>. Minting is unavoidably
    /// inside the timed region for BOTH benchmarks (every invocation needs FRESH owners --
    /// <c>DetachOwner()</c> is a one-shot consuming operation, so a stale, already-detached frame
    /// re-used across invocations would silently stop exercising the owner-transfer path being
    /// measured) -- but since minting is identical in both benchmarks and both commits, subtracting
    /// <see cref="MintFrames_Only"/> from <see cref="MintAppendDrain"/> isolates append+drain's own
    /// incremental cost.
    /// </para>
    /// </summary>
    [Config(typeof(MicroBenchmarkConfig))]
    public class ArteryWriteCoalescingBenchmarks
    {
        /// <summary>Frames coalesced per benchmark invocation (= OperationsPerInvoke) -- matches the task's "representative batch" size.</summary>
        private const int FrameCount = 16;

        [Params(32, 256, 4096)]
        public int PayloadSize { get; set; }

        private TcpConnectionStage.TcpStreamLogic _logic = null!;
        private Action<ReadOnlySequence<byte>> _append = null!;
        private Func<ReadOnlySequence<byte>> _drain = null!;

        [GlobalSetup]
        public void GlobalSetup()
        {
            var inlet = new Inlet<ReadOnlySequence<byte>>("bench.in");
            var outlet = new Outlet<ReadOnlySequence<byte>>("bench.out");
            // Port.Id defaults to -1 and is normally assigned by module-building during real
            // materialization -- GraphStageLogic's constructor sizes its Handlers array from
            // shape.Inlets/Outlets.Count() (here: 1 + 1), and SetHandler indexes into it by
            // inlet.Id / outlet.Id. Since this benchmark never materializes a real graph, the IDs
            // are set by hand so the constructor's own SetHandler calls land in-bounds.
            inlet.Id = 0;
            outlet.Id = 0;
            var shape = new FlowShape<ReadOnlySequence<byte>, ReadOnlySequence<byte>>(inlet, outlet);
            var role = new TcpConnectionStage.Inbound(ActorRefs.Nobody, halfClose: false);

            // No interpreter/materializer attached -- AppendToWriteBuffer/DrainWriteBuffer never
            // touch Grab/Push/Pull/the connection, only the buffer's own fields (verified by
            // reading both methods' bodies before writing this benchmark).
            _logic = new TcpConnectionStage.TcpStreamLogic(shape, role, new IPEndPoint(IPAddress.Loopback, 9001));

            var logicType = typeof(TcpConnectionStage.TcpStreamLogic);
            var appendMethod = logicType.GetMethod("AppendToWriteBuffer", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new MissingMethodException(logicType.FullName, "AppendToWriteBuffer");
            var drainMethod = logicType.GetMethod("DrainWriteBuffer", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new MissingMethodException(logicType.FullName, "DrainWriteBuffer");

            _append = (Action<ReadOnlySequence<byte>>)appendMethod.CreateDelegate(typeof(Action<ReadOnlySequence<byte>>), _logic);
            _drain = (Func<ReadOnlySequence<byte>>)drainMethod.CreateDelegate(typeof(Func<ReadOnlySequence<byte>>), _logic);
        }

        /// <summary>Mints <see cref="FrameCount"/> fresh owner-carrying frames -- the same real <c>PooledPayloadWriter</c>-backed owner production's <c>ArteryEncodeStage</c> hands to coalescing.</summary>
        private static ReadOnlySequence<byte>[] MintFrames(int count, int payloadSize)
        {
            var frames = new ReadOnlySequence<byte>[count];
            for (var i = 0; i < count; i++)
            {
                var writer = new PooledPayloadWriter(payloadSize);
                writer.GetSpan(payloadSize);
                writer.Advance(payloadSize);
                var owner = writer.Detach();
                frames[i] = OwnedSequenceSegment.Create(owner, payloadSize);
            }

            return frames;
        }

        /// <summary>Reference: mint-and-immediately-dispose, WITHOUT going through <see cref="TcpConnectionStage.TcpStreamLogic"/> at all.</summary>
        [Benchmark(Baseline = true, OperationsPerInvoke = FrameCount)]
        public long MintFrames_Only()
        {
            var frames = MintFrames(FrameCount, PayloadSize);
            long total = 0;
            for (var i = 0; i < frames.Length; i++)
            {
                total += frames[i].Length;
                frames[i].DisposeOwnedSegments();
            }

            return total;
        }

        /// <summary>Mint, then run the REAL <c>AppendToWriteBuffer</c> x<see cref="FrameCount"/> + one real <c>DrainWriteBuffer</c>.</summary>
        [Benchmark(OperationsPerInvoke = FrameCount)]
        public long MintAppendDrain()
        {
            var frames = MintFrames(FrameCount, PayloadSize);
            for (var i = 0; i < frames.Length; i++)
                _append(frames[i]);

            var drained = _drain();
            var length = drained.Length;
            // Stands in for write-coalescing's own downstream (TcpConnection, at the point bytes
            // are proven copied into the OS write pipe) -- without this, treatment's real owners
            // leak out of the ArrayPool across thousands of BenchmarkDotNet invocations.
            drained.DisposeOwnedSegments();

            // Baseline's AppendToWriteBuffer copies bytes (ToArray) and never touches the
            // ORIGINAL frame's owner -- that disposal is ArteryEncodeStage's own job, on a delay
            // (see its 2-generation-lag remarks); coalescing simply isn't involved. Treatment's
            // AppendToWriteBuffer DETACHES the original owner into its OWN WriteBufferSegment
            // (transferred, not disposed), so by here the original is already null and this loop
            // is a harmless no-op. Without this, baseline's minted buffers would never return to
            // the ArrayPool across repeated invocations -- understating steady-state pool reuse
            // and inflating "coalescing" cost with cache-miss churn that is actually
            // ArteryEncodeStage's unmeasured disposal debt, not AppendToWriteBuffer's own cost.
            for (var i = 0; i < frames.Length; i++)
                frames[i].DisposeOwnedSegments();

            return length;
        }
    }
}
