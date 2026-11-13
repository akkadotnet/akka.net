# Artery outbound owner/segment allocation results

Consolidate the pooled frame owner and producer sequence segment in the Artery outbound handoff.

An internal PooledPayloadWriter.DetachOwnedSequence handoff replaces the separate owner and producer segment in ArteryEncodeStage. Public PooledPayloadWriter.Detach keeps its owner-only behavior. The codec writes the same frame, and TCP transfers/disposes ownership through the existing IOwnedSequenceSegment contract.

Wrappers are never pooled or reactivated. Separate ownership and returned flags make cleanup through the original sequence inert after transfer while the detached owner can return the original rent once. On the first multi-frame lane append, the compact seed is normalized to the existing appendable OwnedSequenceSegment chain; failures clean every owner moved onto the new chain. Later appends reuse that accumulator.

This reduces total allocated producer metadata while retaining the writer object and TCP's separate WriteBufferSegment. It also changes what remains alive after transfer: the combined segment remains the detached owner until downstream returns its rent, whereas the previous producer segment could become collectible while its smaller owner remained. Matched allocation profiles observe a 64 B combined owner versus the previous 40 B retained owner. These triggering-object sizes are not evidence of reduced live/peak memory. Fewer total allocated bytes can coexist with more metadata retained for each outstanding frame; no live-memory improvement is claimed.

### BenchmarkDotNet

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

## RemotePingPong observations

Two independent unprofiled ABBA studies compare frozen parent `4704dbe03f00631d181d22918e34d7c775145b97` against metadata candidate `0f27f582c92a2bcc35599f9e6d4cc88b40808825`. Each block starts one fresh process and runs three repetitions. Every observation is retained. Both revisions use the same real V2 payload (348 serialized bytes, eight readings), one client, one million configured messages, and window 50. The denominator is two million configured ping+echo messages per repetition; local primers and pending echo tails prevent calling that an independently observed exact delivery count.

Both revisions use `DOTNET_GCDynamicAdaptationMode=0` and `DOTNET_GCHeapCount=8`. All eight processes report Server GC, heap count 8, adaptation mode 0, and the retained-array allocation sanity probe of 8,388,800 bytes against a minimum of 8,388,608. Strict readbacks passed raw cumulative-counter arithmetic, UTC windows, source/binary pins, settings, and three repetitions per process. The reused parent binary has a separate equality receipt covering 632 relevant source/configuration files. Actual heap-history and allocation-stack verification are separate profiling evidence.

| Inbound/outbound lanes | Baseline median B/configured message (range) | Candidate median B/configured message (range) | Observed median delta | Baseline/candidate throughput median (messages/s) |
|---|---:|---:|---:|---:|
| 1/1 | 2889.864 (2885.525–2920.954) | 2857.322 (2855.294–2859.104) | -32.542 B (-1.126%) | 208380 / 205881 |
| 2/2 | 2975.004 (2971.611–2981.226) | 2946.050 (2941.595–2955.786) | -28.954 B (-0.973%) | 460356 / 390969 |

The shared host had unrelated activity recorded in the process/load receipts. Throughput and elapsed time are observations, not causal speedup or slowdown conclusions. The studies are kept separate. The 2/2 setting selects the lane/batch-normalization graph but does not establish exact batch cardinality or activity in both lanes for a single recipient.

### 1/1 lanes: all 12 repetitions

| Block | Role | Variant | Rep | Allocated bytes | B/configured message | Messages/s | Elapsed ms |
|---|---|---|---:|---:|---:|---:|---:|
| a1 | baseline | phase2 | 1 | 5777678496 | 2888.839 | 162365 | 12318.12 |
| a1 | baseline | phase2 | 2 | 5781778600 | 2890.889 | 207750 | 9627.35 |
| a1 | baseline | phase2 | 3 | 5771050224 | 2885.525 | 209009 | 9569.15 |
| b1 | candidate | metadata | 1 | 5716344200 | 2858.172 | 163506 | 12232.40 |
| b1 | candidate | metadata | 2 | 5712942952 | 2856.471 | 202184 | 9892.34 |
| b1 | candidate | metadata | 3 | 5712613936 | 2856.307 | 214916 | 9306.51 |
| b2 | candidate | metadata | 1 | 5718207648 | 2859.104 | 165344 | 12096.59 |
| b2 | candidate | metadata | 2 | 5710587432 | 2855.294 | 209578 | 9543.26 |
| b2 | candidate | metadata | 3 | 5716838448 | 2858.419 | 219950 | 9093.65 |
| a2 | baseline | phase2 | 1 | 5773120424 | 2886.560 | 153551 | 13025.66 |
| a2 | baseline | phase2 | 2 | 5828671672 | 2914.336 | 294638 | 6788.22 |
| a2 | baseline | phase2 | 3 | 5841907040 | 2920.954 | 324781 | 6158.80 |

### 2/2 lanes: all 12 repetitions

| Block | Role | Variant | Rep | Allocated bytes | B/configured message | Messages/s | Elapsed ms |
|---|---|---|---:|---:|---:|---:|---:|
| a1 | baseline | phase2 | 1 | 5958918424 | 2979.459 | 325893 | 6137.48 |
| a1 | baseline | phase2 | 2 | 5950371016 | 2975.186 | 461574 | 4333.68 |
| a1 | baseline | phase2 | 3 | 5949643440 | 2974.822 | 483793 | 4134.48 |
| b1 | candidate | metadata | 1 | 5904196440 | 2952.098 | 302252 | 6617.80 |
| b1 | candidate | metadata | 2 | 5886506800 | 2943.253 | 440626 | 4539.37 |
| b1 | candidate | metadata | 3 | 5883189400 | 2941.595 | 449540 | 4449.83 |
| b2 | candidate | metadata | 1 | 5911572360 | 2955.786 | 283487 | 7055.36 |
| b2 | candidate | metadata | 2 | 5893671848 | 2946.836 | 390931 | 5116.61 |
| b2 | candidate | metadata | 3 | 5890527064 | 2945.264 | 391007 | 5115.25 |
| a2 | baseline | phase2 | 1 | 5962451400 | 2981.226 | 307930 | 6495.91 |
| a2 | baseline | phase2 | 2 | 5946478544 | 2973.239 | 465442 | 4297.94 |
| a2 | baseline | phase2 | 3 | 5943222976 | 2971.611 | 459137 | 4356.32 |

### Verification

The final integrated correctness runs passed with zero failures or skips:

| Executed local scope | Passed | Failed | Skipped |
|---|---:|---:|---:|
| All TCP specs plus PooledPayloadWriterSpec | 95 | 0 | 0 |
| Artery explicit/V2 codec, ordinary transport, control stream and lane batching specs | 49 | 0 | 0 |
| Full API approval suite | 35 | 0 | 0 |

These runs rebuilt the metadata implementation on the corrected Streams parent. The subsequent restack onto 4704dbe03 preserves all seven metadata source hashes and the metadata C# patch byte-for-byte; its sole integrated difference from the tested revision is the Streams enum's formatting, verified by whole-file nonwhitespace equality. No API approval files changed.

Twelve added behavioral cases cover zero-length and growing buffers, written-byte and rented-array identity, irreversible spent state, transferred-owner cleanup, returning each rent once, disposal of an old sequence after the same array is re-rented, capacity/rent failure, sliced mixed legacy/combined batching, partial-append cleanup, retained-batch cancellation/upstream failure, and real TCP with a borrowed preamble under a poisoning pool. Existing public Detach and actual ordinary/control transport poison cases remain in the executed scopes. Final source Slopwatch passed; the measured benchmark build had zero warnings/errors. These are the targeted core/Remote scopes and the full API suite, not a full core/Remote test run.

### Measured source

The twelve paired microbenchmark rows were measured at e9ea08a6f10039597afba5c683bbe00fd104bd3d. The same seven metadata C# files and their patch are preserved in the frozen transport candidate 0f27f582c92a2bcc35599f9e6d4cc88b40808825 (metadata code 606757a5a2914a34b6ebdef355b612a4221fb300) on corrected Streams parent 4704dbe03f00631d181d22918e34d7c775145b97. Benchmark source SHA256 remains c4f1235a1310b8948fbf4c864f7436bc67a3456bdd8f8ee7e10924e10239d278. Every case completed one launch, three raw warmups and three raw actual measurements; original generated tables are embedded above.

The micro controls isolate 32 B less allocation for a single frame and 8 B less per two-frame batch after the extra seed-normalization wrapper. Separate unprofiled Artery ABBA studies observe one-lane medians 2889.864274→2857.321788 B/configured message (−32.542486, −1.126%) and two-lane medians 2975.003614→2946.049728 (−28.953886, −0.973%). These are whole-process window observations, not theoretical per-frame savings or proof that 2/2 lanes create two-frame batches. Lane studies are not pooled. No causal timing or live-memory improvement is established.

Matched allocation profiles below establish observed sizes and sampled producer representation changes; they do not assign the whole-process allocation differences to this one site.

### Allocation-profile attribution

Three new captures completed with zero lost events, three valid UTC/counter windows each and eight heaps observed in every window. The one-lane parent uses the exact frozen phase2 trace with explicit source/harness/binary/workload/GC/trace and actual-gate reuse receipt; the two-lane parent and candidate use separate matched traces. Source/binary/tool and 112-artifact readbacks passed. Profile timing is excluded from performance results.

Sampled full-trace ticks below are not object counts or per-message rates. Zero means no samples in the qualified category, not universal absence. Shared owner/segment TypeIDs are not expanded into target-site totals.

| Qualified category / observed object size | Parent1/1 (reused) | Metadata1/1 | Parent2/2 | Metadata2/2 |
|---|---:|---:|---:|---:|
| Encode producer rented owner, 40 B | 2419 | 0 | 2289 | 0 |
| Encode producer separate segment, 56 B | 3513 | 0 | 3338 | 0 |
| Private combined carrier, 64 B: direct producer | 0 | 95 | 0 | 173 |
| Private combined carrier, 64 B: other/inlined before identity linkage | 0 | 3735 | 0 | 3460 |
| Direct seed-normalization wrapper, 56 B | 0 | 0 | 0 | 377 |
| AppendFrameToBatch shared segment, 56 B:ordinary/normalization ambiguous | 0 | 0 | 1400 | 1458 |
| Remaining codec writer, 48 B | 2958 | 2827 | 2824 | 2830 |
| Remaining TCP write-buffer segment, 56 B | 3538 | 3401 | 3248 | 3247 |

The private combined carrier totals 3830 sampled ticks at 1/1 (95 directly qualified producer +3735 disjoint other/inlined ticks) and 3633 at 2/2 (173 +3460). Each capture validates one namespace-qualified, nonzero same-trace PID/CLR/TypeID identity, with 64 B triggering objects. Frozen Akka.dll IL verifies the exact Akka.IO.PooledSequenceSegment constructor at Create IL0003 and the factory call from PooledPayloadWriter.DetachOwnedSequence IL0018. Identity linkage establishes private-type totals; it gives the linked samples no lane, actor-instance, producer-callsite or per-message attribution. Shortened names alone were not accepted.

At 2/2, 377 samples directly qualify the seed-normalization wrapper through TransferToAppendableSegment; 1458 AppendFrameToBatch samples remain initial-normalization/ordinary-appended-link ambiguous. Parent append-ambiguous samples total 1400. These observations and the isolated two-frame control explain the representation tradeoff without measuring batch cardinality, invocation counts or normalization rate. Changing both lane settings prevents causal cross-lane attribution. Weighted AllocationAmount64 values are sampling-quantum estimates, not exact type allocation bytes.

Profiles confirm the remaining 48 B codec writer and 56 B TCP write-buffer segment. Current Detach/DetachOwnedSequence terminal handoffs spend the writer. Eliminating that remaining writer allocation requires an explicit internal reusable-handoff lifecycle or a different writer, while preserving public Detach/Dispose terminal behavior and the known PooledPayloadWriter serializer/backpatch fast paths. That alternative was not implemented or measured in this experiment; writer reuse is not ruled out as a future design. TCP metadata also remains separate.

The 64 B combined carrier remains the detached owner until TCP completion; the previous 40 B owner could remain after its separate 56 B producer segment became unreachable. Lower cumulative allocation does not establish lower peak live metadata, retained-buffer lifetime or a memory-usage reduction. Neither zero old producer samples nor these isolated controls prove unrelated public ownership paths cannot allocate. The unprofiled lane studies retain all 24 observations and their configured-message denominator/shared-host limits; these profiles do not assign their aggregate deltas wholly to this site or establish a causal throughput improvement.

Evidence: original isolated microbenchmark reports and source/patch receipts are retained under /tmp/artery-buffer-metadata-evidence. Independent unprofiled lane studies/raw repetitions and binary pins are in /tmp/akka-remaining-perf-20261016/metadata. Matched profile traces/ETLX, all-type/CPU reports, raw stacks/identity keys, IL, UTC/counter windows, heap histories, exact commands and final readback receipts are in /tmp/akka-remaining-perf-20261016/streams-metadata-profiles. These local results are distinct from hosted CI, merge and release acceptance. Durable archive copying/readback is separate.
