## Context

The RemotePingPong benchmark is the standard throughput measurement for Akka.NET remoting. It measures round-trip messages/second between two ActorSystems. DotNetty's `FlushConsolidationHandler` provides write batching — consolidating multiple `flush()` calls during read loops for reduced syscalls. The new Akka.Streams transport must match or exceed this. The `FrameBufferWriter` + `Stream` + `Pipe` pipeline has fewer copy points than DotNetty, which should provide a baseline advantage, but flush batching and dispatch tuning are needed to maximize throughput.

## Goals / Non-Goals

**Goals:**
- Establish DotNetty baseline on current `dev` branch (messages/sec, latency percentiles, allocations)
- New transport MUST exceed DotNetty throughput
- Implement flush batching (coalesce multiple writes before `stream.FlushAsync()`)
- Tune `Pipe` backpressure thresholds for throughput
- Tune `ArrayPool` / `MemoryPool` buffer sizing
- Profile and optimize hot paths (allocation-free serialization, dispatch overhead)
- Continuous benchmark tracking as optimizations land

**Non-Goals:**
- QUIC transport benchmarking (future)
- Benchmarking serialization in isolation (already validated in POC)
- Micro-benchmarking individual components (focus on end-to-end throughput)

## Decisions

### 1. RemotePingPong as primary benchmark

**Decision:** Use the existing RemotePingPong benchmark as the single most important metric. Measure messages/second, P50/P99 latency, and allocation rate (bytes/op).

**Rationale:** This is the benchmark the community knows and uses. It measures the full pipeline: serialization → framing → transport → network → transport → deframing → deserialization. End-to-end numbers are what matter.

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
