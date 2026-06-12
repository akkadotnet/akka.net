## 1. Baseline Preservation

- [ ] 1.1 Verify existing DotNetty RemotePingPong baseline is documented
- [ ] 1.2 Re-run DotNetty baseline on current reference hardware if hardware/runtime changed
- [ ] 1.3 Record messages/sec, P50/P95/P99 latency, allocation rate, hardware, OS, .NET version, GC mode

## 2. Artery RemotePingPong

- [ ] 2.1 Add benchmark configuration for Artery TCP
- [ ] 2.2 Run RemotePingPong with 1, 5, 10, 15, 20, 25, and 30 clients
- [ ] 2.3 Compare throughput against DotNetty baseline
- [ ] 2.4 Compare allocation rate against DotNetty baseline
- [ ] 2.5 Compare latency percentiles against DotNetty baseline

## 3. Envelope Codec Benchmarks

- [ ] 3.1 Add microbenchmark for classic protobuf PDU encode/decode
- [ ] 3.2 Add microbenchmark for Artery envelope encode/decode
- [ ] 3.3 Benchmark empty, small, medium, and near-max-frame payloads
- [ ] 3.4 Benchmark multi-segment decode input
- [ ] 3.5 Record throughput and allocations

## 4. Serializer Benchmarks

- [ ] 4.1 Benchmark V1 adapter fallback serialization/deserialization
- [ ] 4.2 Benchmark generated MessagePack V2 serialization/deserialization
- [ ] 4.3 Benchmark representative actor messages
- [ ] 4.4 Benchmark persistence-style event payloads
- [ ] 4.5 Record size hint accuracy and buffer growth frequency

## 5. Batching And Flush Policy

- [ ] 5.1 Implement configurable Artery TCP write batching if not already present
- [ ] 5.2 Benchmark flush by message count
- [ ] 5.3 Benchmark flush by byte threshold
- [ ] 5.4 Benchmark flush by latency budget
- [ ] 5.5 Select default throughput profile
- [ ] 5.6 Validate low-latency profile

## 6. Queue And Backpressure Validation

- [ ] 6.1 Benchmark slow receiver with fast sender
- [ ] 6.2 Verify ordinary queue stays bounded
- [ ] 6.3 Verify control/system traffic is not starved by ordinary traffic
- [ ] 6.4 Verify ordinary queue overflow behavior is deterministic
- [ ] 6.5 Verify control queue overflow triggers severe failure/quarantine behavior

## 7. Profiling And Optimization

- [ ] 7.1 Profile Artery RemotePingPong with dotnet-trace or JetBrains profiler
- [ ] 7.2 Identify allocation hot spots
- [ ] 7.3 Identify CPU hot spots
- [ ] 7.4 Optimize envelope parsing/writing based on profiling
- [ ] 7.5 Optimize dispatch path based on profiling
- [ ] 7.6 Re-run benchmarks after each optimization

## 8. Final Validation

- [ ] 8.1 Confirm Artery TCP exceeds 680K msgs/sec baseline on reference hardware
- [ ] 8.2 Confirm allocation rate is lower than DotNetty/classic path
- [ ] 8.3 Confirm queue/backpressure tests pass
- [ ] 8.4 Confirm control/system traffic correctness under load
- [ ] 8.5 Document final results and recommended defaults
