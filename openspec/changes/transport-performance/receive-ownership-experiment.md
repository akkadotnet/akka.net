# TCP receive ownership experiment

Status: isolated prototype; production TCP/Streams/Artery receive code is unchanged. Correctness tests and six focused BenchmarkDotNet cases must establish whether this direction warrants an integration design. This is not a shipping zero-copy transport fix.

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

The existing `ArteryIngressSourceBenchmarks` class has six focused methods: copy and borrow for complete frames, fragmented headers/bodies, and queued actor consumers. No new benchmark class or parameter matrix is added. Both arms use prebuilt production-encoded Artery frames, a real `Pipe`, production `ArteryFrameParser` and envelope decode, two asynchronous actors, ordered frame IDs and full payload-content checks. Complete reads batch 32 frames. Fragmented reads first split the length field, then repeatedly split bodies. Results normalize by 4096 delivered frames per invocation.

Correctness controls exercise both arms with normal/queued consumers and complete/fragmented input, empty EOF, truncated EOF, cancellation and writer failure while borrowed memory is retained, exactly-once acknowledgements/finish/owner return, and copied data consumed after its pool rental has been poisoned. A separate real-socket public TCP test retains `Received.Data` through 128 KiB of subsequent reads. The production copy contract remains required.

## Measurement limits and integration decision

This is an end-to-end experiment in one revision, comparing two arms. It is not a before/after TCP socket implementation or a RemotePingPong measurement. The writer copies prebuilt bytes into the pipe in both arms; actual network I/O, generated deserialization, multi-lane channels, backoff and reconnect are absent. Actor creation/termination and a parser reconstruction per read are included and amortized. The completion objects, batch arrays and queued actor work are experimental coordination costs. The copy arm can pipeline reads; the borrow arm deliberately waits for the slow consumer. This asymmetry is part of the ownership tradeoff, not a promised optimization.

Preserve the class's existing GC flavor, log the actual BenchmarkDotNet child GC configuration, and apply identical fixed-policy environment controls to both arms when coordinated. Retain raw output; noisy Short timings cannot establish a throughput benefit.

Before integrating, decide from measured allocation and time whether the saved copy justifies coordination and backpressure. A viable production design additionally needs an explicit internal opt-in carrying the read-completion token through TCP Streams, parser partial storage and every async lane; teardown must not complete the reader while consumers retain borrowed slices. It also needs serializer ownership capability/fallback copying for serializers allowed to retain input. Existing public TCP and serializer contracts cannot be silently tightened. If these changes cost more than the savings or cannot preserve contracts, retain the copy and report the measured rejection.

## Verification so far

The focused Release run passed **16 tests, 0 failed, 0 skipped** on .NET10 in727ms. Evidence: `/tmp/receive-ownership-evidence/tests/receive-ownership-corrected.trx` and `corrected-tests.log`. The initial run found a setup error (non-public experimental actor constructors unavailable to Akka's activator); that owned run was stopped, constructors corrected, and its original log retained as `initial-tests.log`. No ownership result is inferred from that failed setup.

Slopwatch and whitespace checks passed. Product TCP, Streams TCP and Artery parser source remain unchanged. The benchmark project itself has not yet been built, and the six BenchmarkDotNet cases have not run. Performance evidence and an integration decision are pending the coordinated measurement slot; no performance outcome is assumed here.
