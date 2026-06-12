## Context

The current DotNetty baseline on `dev` is approximately 680K messages/sec in RemotePingPong on the recorded reference machine. Artery TCP must exceed that before it becomes the preferred remoting path.

The performance target changed from a classic-remoting transport swap to a new Artery stack. This means the critical hot paths are:

- Artery binary envelope encode/decode,
- `SerializerV2` payload serialization,
- generated MessagePack serializers,
- TCP framing and batching,
- bounded outbound queues,
- control/system stream behavior under ordinary traffic load,
- actor dispatch after envelope decode.

## Goals / Non-Goals

**Goals:**

- Preserve DotNetty RemotePingPong baseline for comparison.
- Prove Artery TCP exceeds DotNetty throughput.
- Prove Artery TCP allocates less than classic remoting in the common path.
- Prove bounded queues prevent unbounded memory growth.
- Tune batching without hiding unacceptable tail latency.
- Validate generated MessagePack serializers outperform V1 adapter fallback.

**Non-Goals:**

- QUIC benchmarking.
- TLS benchmarking before plaintext Artery TCP is stable.
- Optimizing classic remoting beyond compatibility fixes.
- Treating microbenchmarks as sufficient without RemotePingPong.

## Decisions

### 1. RemotePingPong Remains The Primary Gate

RemotePingPong measures the integrated path and remains the main acceptance test.

Gate: Artery TCP must exceed the current DotNetty peak baseline on the same hardware and runtime configuration.

### 2. Add Envelope Codec Microbenchmarks

Artery envelope encode/decode must be benchmarked against the classic protobuf `AkkaPduCodec` path.

Suggested gate: materially lower allocations and at least 3x throughput improvement in codec-only benchmark before deeper tuning.

### 3. Add SerializerV2 Payload Benchmarks

Generated MessagePack V2 serializers should be benchmarked against V1 adapter fallback.

Suggested gate: generated serializers allocate less and are faster than V1 fallback for representative user messages.

### 4. Backpressure Is A Correctness Gate

Bounded queues must be validated under slow receivers and bursty senders. Throughput wins are not acceptable if queue memory can grow unbounded.

### 5. Batching Is Configurable

Artery TCP should batch writes by bytes, message count, and/or a short latency budget. Defaults must be benchmark-driven, and low-latency profiles should be possible.

## Risks / Trade-offs

**Throughput vs latency**: batching improves throughput but can hurt P99 latency. Record both.

**Benchmark drift**: run comparisons on the same hardware, OS, .NET runtime, and process settings.

**Queue policy changes user experience**: bounded queues may expose missing flow control. Document behavior clearly.

**False wins from incomplete correctness**: do not benchmark final throughput before control stream and reliable system-message behavior are in place.
