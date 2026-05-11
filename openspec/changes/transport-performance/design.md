## Context

The RemotePingPong benchmark is the standard throughput measurement for Akka.NET remoting. It measures round-trip messages/second between two ActorSystems. DotNetty's `FlushConsolidationHandler` provides write batching — consolidating multiple `flush()` calls during read loops for reduced syscalls. The new Akka.Streams transport must match or exceed this. The `FrameBufferWriter` + `Stream` + `Pipe` pipeline has fewer copy points than DotNetty, which should provide a baseline advantage, but flush batching and dispatch tuning are needed to maximize throughput.

## Goals / Non-Goals

**Goals:**
- Establish DotNetty baseline on current `dev` branch (messages/sec, latency percentiles, allocations)
- Benchmark the integrated outbound write-loop spike before the full transport rewrite
- New transport MUST exceed DotNetty throughput
- Implement flush batching (coalesce multiple writes before `stream.FlushAsync()`)
- Tune `Pipe` backpressure thresholds for throughput
- Tune outbound `ArrayPool` / `MemoryPool` buffer sizing
- Profile and optimize hot paths (allocation-free outbound serialization, dispatch overhead)
- Continuous benchmark tracking as optimizations land

**Non-Goals:**
- QUIC transport benchmarking (future)
- Treating serializer micro-benchmarks as sufficient proof without transport integration data
- Read-side pooled buffer experimentation across actor boundaries

## Decisions

### 1. RemotePingPong as primary benchmark

**Decision:** Use the existing RemotePingPong benchmark as the single most important metric. Measure messages/second, P50/P99 latency, and allocation rate (bytes/op).

**Rationale:** This is the benchmark the community knows and uses. It measures the full pipeline: serialization → framing → transport → network → transport → deframing → deserialization. End-to-end numbers are what matter.

### 1A. Outbound write-loop spike as precondition benchmark

**Decision:** Before the full transport rewrite lands, add a bounded benchmark that compares today's split outbound write path against an integrated transport-owned writer loop using send-shaped work items.

**Rationale:** This isolates the architectural question exposed by PR #8203: is collapsing serialization and framing into one outbound loop worth the transport contract break? That should be answered before the full transport replacement takes on more moving parts.

**Status:** Completed with a benchmark-only spike in `src/benchmark/Akka.Benchmarks/Remoting/IntegratedOutboundWriteLoopBenchmarks.cs`.

**Current directional result:** A short BenchmarkDotNet run on .NET 10.0.7 (`--job short --warmupCount 3 --iterationCount 5`) showed the integrated loop outperforming the current split path for every tested payload shape while reducing managed allocation to `0 B` in all integrated cases. Means from that run were:
- `StringShort`: `31.146 us`, `1904 B` -> `4.971 us`, `0 B`
- `StringMedium`: `17.695 us`, `3408 B` -> `6.103 us`, `0 B`
- `StringLong`: `25.512 us`, `26448 B` -> `4.972 us`, `0 B`
- `BytesSmall`: `27.454 us`, `1672 B` -> `3.392 us`, `0 B`
- `BytesLarge`: `36.459 us`, `83528 B` -> `6.577 us`, `0 B`

**Caveat:** These results are directional only. The run used a small sample count, BenchmarkDotNet flagged short iteration times, and some cases had wide confidence intervals and outlier removal. Treat this as evidence that the integrated outbound loop is worth pursuing, not as publication-quality benchmark data.

### 1B. First end-to-end comparison stays on the current wire format

**Decision:** The first full RemotePingPong comparison should use the redesigned transport and outbound path while preserving the current remoting wire format. Source-compatible C# API shims should not be added to the hot path before this comparison.

**Rationale:** This isolates whether the new regime is actually better. If the wire-compatible redesign does not beat the baseline in its cleanest form, extra compatibility work is unlikely to improve that outcome.

### 2. Flush batching in write task

**Decision:** The write-to-stream background task SHALL coalesce pending writes before calling `stream.FlushAsync()`. Instead of flushing after every `WriteAsync`, batch writes within a configurable window (e.g., flush after N writes or after a micro-delay if no more writes are pending).

**Rationale:** DotNetty's `FlushConsolidationHandler` does this — it defers flushes during read loop execution. Without batching, each `Write` → `FlushAsync` is a syscall. Batching reduces syscalls proportionally to batch size.

### 3. Pipe threshold tuning

**Decision:** Make `Pipe` `pauseWriterThreshold` and `resumeWriterThreshold` configurable via HOCON (`batching.pause-writer-threshold`, `batching.resume-writer-threshold`) and benchmark different values.

**Rationale:** These thresholds control how much data buffers in the Pipe before backpressure kicks in. Too low = excessive pausing. Too high = memory bloat. The right values depend on message size and throughput characteristics.

### 4. Profile-driven optimization

**Decision:** Use dotnet-trace / JetBrains profiler to identify allocation hot spots and CPU bottlenecks after the initial integration. Optimize based on data, not speculation.

**Rationale:** The architecture is designed for performance (zero-copy buffers, pooled arrays, sealed classes for devirtualization). Actual bottlenecks will emerge from profiling the integrated system.

## Risks / Trade-offs

**[Flush batching adds latency]** → Batching trades latency for throughput. For latency-sensitive use cases, batching can be disabled or configured with aggressive flush thresholds. Measure both throughput and latency percentiles.

**[Benchmark results are hardware-dependent]** → Run all comparisons on the same machine in the same session. Document hardware specs. Focus on relative improvement (%) rather than absolute numbers.

**[Regression after optimization]** → Each optimization is a separate commit with before/after numbers. Revert if an optimization regresses other metrics.

**[Spike and final transport may differ]** → The spike intentionally keeps scope narrow. Success means the architecture is promising, not that every remaining transport concern is solved. The spike should therefore be used as a gate, not as a substitute for final end-to-end benchmarks.
