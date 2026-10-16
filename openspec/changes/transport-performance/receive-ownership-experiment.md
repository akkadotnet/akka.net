# TCP receive ownership experiment

Status: measured research prototype. Reject shipping this one-outstanding borrowed-read integration and retain the production receive copy. Production TCP/Streams/Artery receive code is unchanged. This decision applies to the measured protocol; it does not rule out a future ownership design with different concurrency and explicit contracts.

## Existing lifetimes

`TcpConnection.ReadPipeChunkAsync` copies the `PipeReader` buffer into a managed array, advances the reader, then asynchronously tells the connection actor. `HandlePipeRead` tells the handler `Tcp.Received`. The Streams TCP stage forwards its `ReadOnlySequence<byte>` without a completion token. `ArteryFrameParser` retains chunk memory across incomplete frames, and parallel inbound lanes retain decoded payload slices in `LaneWorkItem` until a background consumer deserializes them.

Consequently, Tell returning, the TCP stage pushing, and the parser dequeuing a chunk are each insufficient evidence that the underlying bytes are no longer needed. A public TCP consumer can retain `Received.Data` indefinitely today. `SerializerV2.Deserialize` also has no documented no-retention contract: a custom serializer could return a message containing a slice of its input. Finishing synchronous deserialization alone therefore cannot authorize reuse for arbitrary serializers.

## Experimental protocol

The helper is compiled into the benchmark project and linked as source into Remote.Tests. It adds no production dependency, public API, or production protocol. Its consumers explicitly promise to finish reading input before acknowledging it.

```mermaid
sequenceDiagram
    participant P as Pipe writer
    participant R as Read loop
    participant A as Parser actor
    participant C as Consumer actor
    P->>R: Flush encoded Artery frames
    R->>R: ReadAsync (one borrowed read)
    R->>A: ReadChunk(buffer, completion)
    A->>A: Production ArteryFrameParser
    A->>A: Copy incomplete header/body tail
    A->>C: Complete frame slices + read completion
    C->>C: Optional queued mailbox turn or test gate
    C->>C: Production envelope decode; verify ordered ID and every payload byte
    C-->>R: Acknowledge exactly once
    R->>R: AdvanceTo(buffer.End)
    R->>R: Next ReadAsync
```

The copy control copies the whole read and advances immediately, so its reader can continue while the actor consumer is delayed. Its partial tails remain views of stable copied arrays. The borrow arm allows only one outstanding read and waits for the consumer before advancing. It copies only incomplete framing tails into stable storage before acknowledgement, avoiding a deadlock when the next read is needed to finish a frame. EOF does not mean consumption is complete: all posted frame batches must finish. Cancellation or injected writer failure does not revoke an active lease; the reader waits for its acknowledgement, advances once, then completes the reader and reports termination.

Each nonempty read gets one completion, including reads that contain only a partial frame. Test observations count acknowledgements and exactly one experiment finish. A poison memory pool overwrites returned rentals; controlled actor gates establish whether return occurred before or after consumption without arbitrary delays.

## Controls and checks

The existing `ArteryIngressSourceBenchmarks` class has six focused methods: copy and borrow for complete frames, fragmented headers/bodies, and queued actor consumers. No new benchmark class or parameter matrix is added. Both arms use prebuilt production-encoded Artery frames with 128-byte payloads, a real `Pipe`, production `ArteryFrameParser` and envelope decode, two asynchronous actors, ordered frame IDs and full payload-content checks. Each invocation delivers 4096 frames. Complete reads batch 32 frames. Fragmented reads first split the length field, then repeatedly split bodies. Results normalize by 4096 delivered frames per invocation.

Correctness controls exercise both arms with normal/queued consumers and complete/fragmented input, empty EOF, truncated EOF, cancellation and writer failure while borrowed memory is retained, exactly-once acknowledgements/finish/owner return, and copied data consumed after its pool rental has been poisoned. A separate real-socket public TCP test retains `Received.Data` through 128 KiB of subsequent reads. The production copy contract remains required.

## Measurement limits and integration decision

This is an end-to-end experiment in one revision, comparing two arms. It is not a before/after TCP socket implementation or a RemotePingPong measurement. The writer copies prebuilt bytes into the pipe in both arms; actual network I/O, generated deserialization, multi-lane channels, backoff and reconnect are absent. Actor creation/termination and a parser reconstruction per read are included and amortized. The completion objects, batch arrays and queued actor work are experimental coordination costs. The copy arm can pipeline reads; the borrow arm deliberately waits for the slow consumer. This asymmetry is part of the ownership tradeoff, not a promised optimization.

The measured job preserved the class's server-GC Monitoring/RunOnce configuration and used one launch, three warmups and three measured iterations, with no outlier removal. Both arms ran with `DOTNET_GCDynamicAdaptationMode=0` and `DOTNET_GCHeapCount=8`. Actual child Setup output confirmed server GC, eight heaps and adaptation disabled in every case. All three actual observations per case are retained in the full JSON; this small run supports a directional comparison, not a precise production throughput claim.

The measured borrowing arm reduces allocations but imposes per-read acknowledgement/backpressure and has roughly twice the observed mean time in all three scenarios. Retain the production copy rather than integrate this protocol. A different viable production design would additionally need an explicit internal opt-in carrying the read-completion token through TCP Streams, parser partial storage and every async lane; teardown must not complete the reader while consumers retain borrowed slices. It also needs serializer ownership capability/fallback copying for serializers allowed to retain input. Existing public TCP and serializer contracts cannot be silently tightened. A future experiment must preserve those contracts explicitly before its measurements can justify integration.

## Verification so far

The focused Release run passed **16 tests, 0 failed, 0 skipped** on .NET 10 in 727 ms. Evidence: `/tmp/receive-ownership-evidence/tests/receive-ownership-corrected.trx` and `corrected-tests.log`. The initial run found a setup error (non-public experimental actor constructors unavailable to Akka's activator); that owned run was stopped, constructors corrected, and its original log retained as `initial-tests.log`. No ownership result is inferred from that failed setup.

Slopwatch and whitespace checks passed. The Release benchmark project built with 0 warnings and 0 errors. Product TCP, Streams TCP and Artery parser source remain unchanged.

Copied-byte validation has a specific scope: the shared helper's focused tests assert zero copied bytes for borrowed complete reads, and positive copied bytes for whole-read copy and fragmented-tail paths. The helper counts copied bytes in its returned totals; the frozen BDN methods return `Task` and do not export those per-invocation totals. Do not present an inferred exact copied-byte count as a benchmark result. Full payload bytes, ordered frame IDs, frame count and acknowledgement count are checked inside every measured invocation.

## Measured results and decision

The following is the generated BenchmarkDotNet output, shown directly. Allocations and times are per delivered frame.

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Core i9-9900K CPU 3.60GHz (Coffee Lake), 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.103
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-DIUUYA : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

OutlierMode=DontRemove  Concurrent=True  Server=True
InvocationCount=1  IterationCount=3  LaunchCount=1
RunStrategy=Monitoring  UnrollFactor=1  WarmupCount=3

```
| Method                            | Mean       | Error      | StdDev    | Req/sec      | Allocated |
|---------------------------------- |-----------:|-----------:|----------:|-------------:|----------:|
| ReceiveCopy_CompleteFrames        |   495.4 ns |   685.7 ns |  37.59 ns | 2,018,380.27 |     337 B |
| ReceiveBorrow_CompleteFrames      | 1,058.0 ns | 3,127.4 ns | 171.42 ns |   945,147.16 |      98 B |
| ReceiveCopy_FragmentedFrames      |   635.2 ns | 1,581.2 ns |  86.67 ns | 1,574,264.47 |     363 B |
| ReceiveBorrow_FragmentedFrames    | 1,397.1 ns | 6,291.4 ns | 344.85 ns |   715,747.36 |     134 B |
| ReceiveCopy_QueuedActorConsumer   |   556.5 ns | 2,324.0 ns | 127.39 ns | 1,796,840.09 |     349 B |
| ReceiveBorrow_QueuedActorConsumer | 1,155.5 ns | 4,278.9 ns | 234.54 ns |   865,388.92 |     111 B |

Borrowing reduced allocated memory by 239 B/frame for complete reads, 229 B/frame for fragmented reads, and 238 B/frame with queued actor consumption. Observed mean times increased by about 114%, 120%, and 108%, respectively. The three-sample confidence intervals are wide; these are directional experimental observations, not an estimate of a production TCP regression. The borrow arm's one-read limit and the copy arm's ability to pipeline actor work are part of the measured tradeoff. The experiment does not isolate a CPU cost for acknowledgement alone.

Decision: do not ship this one-outstanding-read protocol. Keep the safe production copy. Input-retaining serializers, non-acknowledging/terminated consumers, reader teardown, bounded multi-lane backpressure and restart ownership need a separate contract and design before another integration attempt.

## Source and evidence

The measured source was frozen at commit `7cda4b54786d35d7343844d5d22d463aaa2fd384`:

- `ReceiveOwnershipExperiment.cs`: SHA256 `fd0d4569521085c5e03cb47b2c82630461c6d60255f26ab372ffec49ccdee876`.
- `ArteryIngressSourceBenchmarks.cs`: SHA256 `4be51ad0df8d9b0755ae9f82bb65920fa890dd22863cd927f7a46c12cb02fbfd`.

Raw generated Markdown/CSV/full JSON, strict validation, exact commands, source/binary/runtimeconfig hashes, child GC output, and host process/load receipts are retained under `/tmp/akka-remaining-perf-20261016/receive-ownership`. The validated `summary.json` retains all 18 actual observations. No extra measurements or retries were used for this result. External shared-host activity remains a timing limitation. The lifetime test evidence and the initial setup-failure log remain under `/tmp/receive-ownership-evidence`.

## Final stack validation

The research branch was rebased onto metadata parent `bf90cf2d4cf3b02c7f5f63117f29ca4a5b46b190`. At validated rebased revision `2307ad56376513404879cc0f60a379da5d470a19`, the experiment helper, existing benchmark file, tests and production `ArteryFrameParser` are byte-for-byte identical to measured commit `7cda4b54786d35d7343844d5d22d463aaa2fd384`. The Remote.Tests project patch relative to the final parent is also unchanged. TCP/Streams/metadata production changes belong to the inherited parent; this research change adds no production modifications. The original pre-rebase head is preserved at `backup/receive-ownership-before-final-stack-20261016`.

A fresh restore/build and the existing `FullyQualifiedName~ReceiveOwnershipExperimentSpec` Release filter on .NET 10 passed **16 tests, 0 failed, 0 skipped** in 645 ms after the rebase. Slopwatch scanned all four changed C#/project files without a baseline and passed at warning severity; whitespace checks passed. Receipts, exact commands, build/test log, source hashes and TRX are under `/tmp/akka-remaining-perf-20261016/receive-ownership/final-validation`. The six BenchmarkDotNet rows above remain the original measurements at `7cda4b547`; no benchmark or RemotePingPong rerun is implied by this final correctness validation.
