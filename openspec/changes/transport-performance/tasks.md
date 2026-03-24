## 1. Baseline Capture

- [ ] 1.1 Run RemotePingPong benchmark against current `dev` branch (DotNetty transport)
- [ ] 1.2 Record: messages/second, P50/P99 latency, allocation rate (bytes/op)
- [ ] 1.3 Document hardware specs, .NET version, OS, benchmark configuration
- [ ] 1.4 Commit baseline results to repo for future comparison

## 2. Initial Comparison

- [ ] 2.1 Run RemotePingPong benchmark against new transport (after Specs 1-4 integrated)
- [ ] 2.2 Compare messages/second, latency, and allocation rate vs DotNetty baseline
- [ ] 2.3 Profile with dotnet-trace or JetBrains profiler to identify hot spots
- [ ] 2.4 Document initial comparison results

## 3. Flush Batching

- [ ] 3.1 Implement flush batching in write-to-stream task: coalesce pending writes before `FlushAsync()`
- [ ] 3.2 Add HOCON configuration: `batching.flush-count` (max writes before flush), `batching.flush-interval` (max time before flush)
- [ ] 3.3 Benchmark: compare unbatched vs batched flush at various thresholds
- [ ] 3.4 Select optimal default values based on benchmark results

## 4. Pipe and Buffer Tuning

- [ ] 4.1 Benchmark Pipe `pauseWriterThreshold` / `resumeWriterThreshold` at various settings (64KB, 256KB, 1MB)
- [ ] 4.2 Benchmark `FrameBufferWriter` initial buffer size vs `SizeHint` accuracy
- [ ] 4.3 Benchmark `MemoryPool` vs `ArrayPool` for read path buffer allocation
- [ ] 4.4 Make thresholds configurable via HOCON
- [ ] 4.5 Select optimal defaults based on results

## 5. Dispatch and Hot Path Optimization

- [ ] 5.1 Profile serialization hot path: ensure zero allocations for common message types
- [ ] 5.2 Profile framing hot path: ensure `FrameBufferWriter` doesn't grow for typical messages
- [ ] 5.3 Profile read path: ensure frame parsing from `ReadOnlySequence<byte>` is allocation-free
- [ ] 5.4 Investigate write coalescing: can multiple small messages share a single frame buffer?
- [ ] 5.5 Investigate dispatch overhead: is the actor mailbox the bottleneck for high-throughput scenarios?

## 6. Final Validation

- [ ] 6.1 Run RemotePingPong benchmark with all optimizations applied
- [ ] 6.2 Confirm new transport exceeds DotNetty throughput
- [ ] 6.3 Confirm allocation rate is lower than DotNetty
- [ ] 6.4 Document final comparison results and optimization decisions
- [ ] 6.5 Add benchmark configuration to CI for regression detection
