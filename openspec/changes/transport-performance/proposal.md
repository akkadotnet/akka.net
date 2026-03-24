## Why

The Akka.NET 1.6 transport and serialization overhaul (Specs 1-4) replaces DotNetty with Akka.Streams TCP and introduces SerializerV2 with `IBufferWriter<byte>` / `ReadOnlySequence<byte>`. These changes must not only maintain but exceed the performance of the DotNetty-based transport. Performance validation using the existing RemotePingPong benchmark establishes a before/after baseline. Beyond meeting the baseline, targeted optimizations (flush batching, dispatch improvements, buffer pooling) can push throughput significantly higher.

## What Changes

- Establish DotNetty baseline using RemotePingPong benchmark on current `dev` branch
- Run identical benchmark on the new Akka.Streams transport (after Specs 1-4)
- New transport MUST exceed DotNetty throughput (messages/second)
- Identify and implement optimizations: flush batching, write coalescing, Pipe tuning, buffer pool sizing, dispatch improvements
- Continuous benchmarking as optimizations land

## Capabilities

### New Capabilities

- `transport-benchmarks`: Performance benchmarking infrastructure for comparing DotNetty vs Akka.Streams transport. Covers RemotePingPong benchmark setup, baseline capture, regression detection, and optimization validation.

### Modified Capabilities

## Impact

- **Benchmarks** (`src/benchmark/`): RemotePingPong benchmark with configurable transport selection
- **Akka.Remote**: Flush batching, write coalescing, Pipe threshold tuning in `StreamsTcpTransport`
- **Akka.IO**: Buffer pool sizing, Pipe `pauseWriterThreshold` / `resumeWriterThreshold` tuning
- **FrameBufferWriter**: `ArrayPool` sizing, growth strategy optimization
- **Documentation**: Performance comparison results published in release notes
