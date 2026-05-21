## Why

The Akka.NET 1.6 transport and serialization overhaul (Specs 1-4) replaces DotNetty with Akka.Streams TCP and introduces SerializerV2 with `IBufferWriter<byte>` / `ReadOnlySequence<byte>`. These changes must not only maintain but exceed the performance of the DotNetty-based transport. Performance validation using the existing RemotePingPong benchmark establishes a before/after baseline. Beyond meeting the baseline, targeted optimizations (flush batching, dispatch improvements, buffer pooling) can push throughput significantly higher.

## What Changes

- Establish DotNetty baseline using RemotePingPong benchmark on current `dev` branch
- Add a bounded outbound-write-loop spike benchmark before the full transport rewrite lands
- Use the spike results as a gate for whether the transport write contract should change before the full transport rewrite proceeds
- Run the first end-to-end benchmark on the wire-compatible Akka.Streams redesign before adding compatibility shims or alternate wire formats
- New transport MUST exceed DotNetty throughput (messages/second)
- Identify and implement optimizations: flush batching, write coalescing, Pipe tuning, outbound buffer pool sizing, dispatch improvements
- Continuous benchmarking as optimizations land

## Capabilities

### New Capabilities

- `transport-benchmarks`: Performance benchmarking infrastructure for comparing DotNetty vs Akka.Streams transport. Covers RemotePingPong benchmark setup, baseline capture, regression detection, and optimization validation.

### Modified Capabilities

## Impact

- **Benchmarks** (`src/benchmark/`): RemotePingPong benchmark with configurable transport selection, plus a bounded spike benchmark for the integrated outbound write path
- **Akka.Remote**: Flush batching, write coalescing, Pipe threshold tuning in `StreamsTcpTransport`
- **Akka.IO**: Buffer pool sizing, Pipe `pauseWriterThreshold` / `resumeWriterThreshold` tuning
- **FrameBufferWriter**: `ArrayPool` sizing, growth strategy optimization
- **Documentation**: Performance comparison results published in release notes
