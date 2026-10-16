# Artery outbound frame metadata experiment

The prototype combines the outbound frame's producer sequence segment and detachable memory owner. An internal PooledPayloadWriter.DetachOwnedSequence handoff replaces the separate owner plus segment in ArteryEncodeStage. Public PooledPayloadWriter.Detach and its owner-only behavior are unchanged. The codec still writes the same frame, and TCP still transfers/disposes ownership through IOwnedSequenceSegment.

Wrappers are never pooled or reactivated. Separate ownership and returned flags make cleanup through the original sequence inert after transfer while the detached owner can return the original rent once. On the first multi-frame lane append, the compact seed is normalized to the existing appendable OwnedSequenceSegment chain; failures clean every owner moved onto the new chain. Later appends reuse that accumulator.

This reduces total allocated producer metadata but leaves the writer object and TCP's separate WriteBufferSegment in place. After transfer, the combined segment itself remains the owner until downstream returns the rent; the original path could discard its producer segment sooner. Live retained metadata has not been measured.

## Isolated BenchmarkDotNet evidence

The one-frame pair allocates **144 → 112 B/operation**, a **32 B/frame** reduction, at all three payload sizes. The real two-frame lane-batch pair allocates **344 → 336 B/operation**, only **8 B per two-frame batch (4 B/frame)**. Its additional seed-normalization wrapper consumes most of the two-frame carrier saving. The N2 result is measured directly; it is not extrapolated from N1.

These are paired pattern controls within the **same prototype binary**, not separately built parent/candidate production revisions. Both N2 methods call the prototype's actual AppendFrameToBatch, including its new seed-type guard; the legacy control takes the guard's existing-segment branch. Every operation constructs its writer/carriers inside the measured path, writes the same explicit-parts envelope/payload and disposes ownership. Inputs may be reused; candidate wrappers are not preallocated. N2 rows encode and consume two frames. Serializer lookup, source-generated serialization and TCP buffering/socket work are excluded.

The configured Micro benchmark has no additional job. The CLI uses Short with one launch/three warmups/three actual iterations and .NET 10. Actual child GC is **Concurrent Workstation**, as recorded in all 12 process headers. DOTNET_GCDynamicAdaptationMode=0 and DOTNET_GCHeapCount=8 were requested; the latter is not evidence of eight effective heaps under Workstation GC.

One-frame mean timings were lower in all three rows. The two-frame 4096-byte row moved from 442.9 to 468.6 ns, while its smaller-payload means decreased. Three-sample confidence intervals are broad, so no uniform speedup or throughput conclusion follows. Start-of-run load averages were 7.29/13.18/13.29 on eight visible cores, with 3.1 GiB swap in use. The shared host was not otherwise isolated; no other team performance workload overlapped. Raw timing samples and intervals are retained.

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Core i9-9900K CPU 3.60GHz (Coffee Lake), 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.103
  [Host]    : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  .NET 10.0 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=.NET 10.0  Runtime=.NET 10.0  Toolchain=net10.0  
IterationCount=3  LaunchCount=1  WarmupCount=3  

```
| Method                                             | PayloadSize | Mean     | Error       | StdDev   | Gen0   | Allocated |
|--------------------------------------------------- |------------ |---------:|------------:|---------:|-------:|----------:|
| **Encode_SeparateOwnerAndSegment_Then_Dispose**        | **32**          | **150.2 ns** |   **129.97 ns** |  **7.12 ns** | **0.0172** |     **144 B** |
| Encode_CombinedOwnerAndSegment_Then_Dispose        | 32          | 119.2 ns |    73.90 ns |  4.05 ns | 0.0134 |     112 B |
| Encode_TwoSeparateOwnerFrames_Then_BatchAndDispose | 32          | 408.9 ns | 1,505.72 ns | 82.53 ns | 0.0410 |     344 B |
| Encode_TwoCombinedOwnerFrames_Then_BatchAndDispose | 32          | 374.0 ns |   138.63 ns |  7.60 ns | 0.0401 |     336 B |
| **Encode_SeparateOwnerAndSegment_Then_Dispose**        | **256**         | **138.6 ns** |    **26.22 ns** |  **1.44 ns** | **0.0172** |     **144 B** |
| Encode_CombinedOwnerAndSegment_Then_Dispose        | 256         | 121.7 ns |    94.67 ns |  5.19 ns | 0.0134 |     112 B |
| Encode_TwoSeparateOwnerFrames_Then_BatchAndDispose | 256         | 346.1 ns |   213.29 ns | 11.69 ns | 0.0410 |     344 B |
| Encode_TwoCombinedOwnerFrames_Then_BatchAndDispose | 256         | 334.8 ns |    22.22 ns |  1.22 ns | 0.0401 |     336 B |
| **Encode_SeparateOwnerAndSegment_Then_Dispose**        | **4096**        | **189.5 ns** |   **206.27 ns** | **11.31 ns** | **0.0172** |     **144 B** |
| Encode_CombinedOwnerAndSegment_Then_Dispose        | 4096        | 158.6 ns |   172.99 ns |  9.48 ns | 0.0134 |     112 B |
| Encode_TwoSeparateOwnerFrames_Then_BatchAndDispose | 4096        | 442.9 ns |    83.11 ns |  4.56 ns | 0.0410 |     344 B |
| Encode_TwoCombinedOwnerFrames_Then_BatchAndDispose | 4096        | 468.6 ns |   852.72 ns | 46.74 ns | 0.0401 |     336 B |

## Executed local verification

| Executed local scope | Passed | Failed | Skipped |
|---|---:|---:|---:|
| PooledPayloadWriterSpec + existing TcpConnectionOwnershipDisposalSpec | 31 | 0 | 0 |
| ArteryLaneWriteBatchingSpec + explicit codec + ordinary/control transport specs | 36 | 0 | 0 |
| Separately executed ArteryEnvelopeCodecV2Spec | 13 | 0 | 0 |
| ApproveCore, ApproveRemote, substring-matched ApproveRemoteHosting | 3 | 0 | 0 |

The 31-case core run preceded the final batching-adapter slice/signature adjustment. The final 36-case Remote run rebuilt final core and exercised that adapter, including shortened seed/frame slices. The 13 V2 cases ran against the retained final Remote assembly with `--no-build`. The three API approvals are the executed subset, not a full API-suite result. Two compiler-only failed attempts are retained: missing alias/local-name conflict, then ambiguous TcpListener; neither attempt executed tests. Final source Slopwatch passed, and the benchmark project built with zero warnings/errors.

Twelve added behavioral cases cover zero-length and growing buffers, written-byte and rented-array identity, irreversible spent state, transferred-owner cleanup, returning each rent once, disposal of an old sequence after the same array is re-rented, capacity/rent failure, sliced mixed legacy/combined batching, partial-append cleanup, retained-batch cancellation/upstream failure, and real TCP with a borrowed preamble under a poisoning pool. Existing public Detach and actual ordinary/control transport poison cases remain in the executed scopes. No hosted CI, full core/Remote/API suite or NativeAOT result is claimed for this prototype.

## Provenance

Measured source is `e9ea08a6f10039597afba5c683bbe00fd104bd3d`, originally based on `0879aceea3044dd2bf03958bd4ef0d5d0e05f0b1`. Benchmark source SHA256 is `c4f1235a1310b8948fbf4c864f7436bc67a3456bdd8f8ee7e10924e10239d278`. Seven C# source hashes, the existing benchmark/production binaries and generated BDN harness/binaries are recorded separately. The generated build and all 12 cases completed successfully; every case has one launch, three raw warmups and three raw actual measurements. Raw/full JSON, generated Markdown, exact command, runtimeconfigs and readback-verified result hashes are retained in `/tmp/artery-buffer-metadata-evidence/paired-short`.

## Pending integration and transport evidence

Provisionally retain the experiment pending actual Artery evidence. Do not rebase it until the corrected Streams final parent is available. Then build fresh parent and metadata candidate binaries, run sequential matched Artery ABBA, and capture allocation profiles that validate the actual owner/segment site and remaining writer/TCP wrappers. Existing micro controls establish the isolated allocation reduction; they do not establish the end-to-end allocation delta, live-memory improvement or transport throughput gain. The object layout expectations are not a substitute for actual ObjectSize attribution in those profiles. No metadata RPP/profiles or PR publication have occurred yet.
