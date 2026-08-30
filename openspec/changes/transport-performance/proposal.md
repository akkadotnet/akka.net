## Why

Artery TCP is the Akka.NET 1.6 high-throughput remoting path. It must beat the current DotNetty baseline while preserving remoting correctness under backpressure, system-message delivery, association restart, and quarantine scenarios.

The earlier performance plan targeted an Akka.Streams TCP replacement under classic remoting. That is no longer the target. Performance work now validates and tunes the Artery stack: envelope codec, `SerializerV2` payload path, TCP batching, bounded queues, and dispatch behavior.

## What Changes

- Preserve the existing DotNetty RemotePingPong baseline as the comparison point.
- Add Artery TCP RemotePingPong benchmark runs.
- Add envelope codec microbenchmarks comparing Artery binary envelope vs classic protobuf PDU path.
- Add serializer microbenchmarks comparing generated MessagePack V2 serializers vs V1 adapter fallback.
- Add queue/backpressure benchmarks and slow-receiver tests.
- Tune batching, socket/Pipe thresholds, and buffer pooling for Artery TCP.
- Document throughput, latency, and allocation results.

## Capabilities

### New Capabilities

- `artery-transport-benchmarks`: Performance benchmarking and tuning for Artery TCP, including RemotePingPong, envelope codec microbenchmarks, serializer comparisons, and backpressure validation.

## Impact

- **Benchmarks** (`src/benchmark/`): add or update RemotePingPong configuration for Artery TCP.
- **Akka.Remote Artery**: tune write batching, bounded queues, buffer pooling, envelope codec, and dispatch.
- **Akka.Serialization.V2**: validate generated MessagePack throughput and allocation rates.
- **Documentation**: publish DotNetty vs Artery TCP performance results and tuning decisions.
