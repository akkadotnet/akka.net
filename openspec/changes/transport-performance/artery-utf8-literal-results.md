# Artery literal UTF-8 allocation results

Measured on 2026-10-13 for PR #8805, stacked on #8804.

## Revisions and method

* Parent: `817fe7e792c2ab843586509fc78ddfbad55982d3` (delegate fix and its report).
* Candidate: `7b8a4adceb54db4b66908a980d9e629d4281a11a` (direct UTF-8 encoding, tests, benchmark extension).
* The extended benchmark was used on both revisions; parent production code was unchanged.
* Ubuntu 24.04.4, KVM guest, Intel Core i9-9900K, 8 visible cores; SDK 10.0.103, runtime 10.0.12, x64.
* BenchmarkDotNet 0.15.8 ShortRun, one launch, three warmups, three measured iterations, concurrent workstation GC.
* Other workloads caused CPU contention and severe memory/swap pressure during this work. Timing is provisional; allocation measurements are reported separately.
* The first parent benchmark attempt hit the 120-second build timeout and executed zero cases. Its log is retained; both successful comparisons used a 300-second build timeout with unchanged measurement settings.

## Allocation comparison

`Encode` calls the production single-pass codec with a reused byte-array payload and constant actor paths. Its manifest is empty. `EncodeLiteralMetadata` uses the explicit-parts codec with a reused frame buffer, the same paths, and the 17-byte manifest `real-benchmark-v1`. Setup allocations are outside measurement.

| Method | Payload bytes | Parent allocation | Candidate allocation | Parent mean | Candidate mean |
| --- | --- | --- | --- | --- | --- |
| Encode | 32 | 176 B | 48 B | 233.1 ns | 161.96 ns |
| EncodeLiteralMetadata | 32 | 176 B | 0 B | 124.2 ns | 72.63 ns |
| Encode | 256 | 176 B | 48 B | 223.3 ns | 169.79 ns |
| EncodeLiteralMetadata | 256 | 176 B | 0 B | 139.8 ns | 72.25 ns |
| Encode | 4096 | 176 B | 48 B | 252.1 ns | 205.19 ns |
| EncodeLiteralMetadata | 4096 | 176 B | 0 B | 170.0 ns | 120.07 ns |

The production encoder saves **128 B/op (176 → 48 B)** at all three payload sizes. The fixed-buffer case removes **176 B/op (176 → 0 B)** from encoding all three metadata literals. The native single-pass case still constructs its pooled writer; neither result means allocation-free transport. Payload serialization, sender/recipient path formatting, receive-side strings, detached owners, and stream wrappers remain outside these isolated measurements.

The cumulative production-encoder allocation baseline is **240 → 176 → 48 B/op** across the original code, #8804, and this change: 192 B/op lower in total. See the [delegate results](artery-encode-delegate-results.md) for the original baseline. This allocation comparison does not establish an integrated throughput speedup.

## RemotePingPong comparison

All invocations ran sequentially, with an additional process check before each start. Fixed order: parent, candidate, candidate, parent. Each invocation uses 10 clients, 100,000 messages per client, three repetitions, generated V2 real payload, window 50, and one inbound/outbound lane. Each repetition counts 2,000,000 completed application messages across both directions. This message count differs from the earlier report; compare revisions within this table.

| Order | Revision | Msgs/s (three reps) | Median msgs/s |
| --- | --- | --- | --- |
| 1 | parent | 186,568, 375,658, 117,675 | 186,568 |
| 2 | candidate | 257,666, 447,829, 419,728 | 419,728 |
| 3 | candidate | 306,467, 485,555, 481,580 | 481,580 |
| 4 | parent | 305,111, 471,365, 353,732 | 353,732 |

Every repetition completed without drop/resend warnings. The host contention and memory pressure preclude a reliable throughput improvement or no-regression conclusion. The actor systems are recreated between repetitions; cold tiering effects remain. No latency percentiles were measured. A quiet-machine comparison is needed for reliable throughput claims.

## Boxing finding from the existing profile

This investigation reused the delegate-fixed RemotePingPong allocation trace from the previous change, rather than launching another profiling run. It is evidence about the parent implementation; this PR does not modify stream representation.

The trace records 40-byte boxed `ReadOnlySequence<byte>` objects. Allocation stacks resolve through `GraphStageLogic.Push<T>` to Artery's encode stage, concat, shared/unique kill switches, termination watching, restart/substream plumbing, and TCP stages. The interpreter's `Connection.Slot` is an `object`, so forwarding a struct through a stage can box it again even in a fused graph. The converted trace reports zero lost events; allocation ticks remain sampled observations.

A reported 280 B is consistent with seven 40-byte boxes, but this profile does not prove that exact per-message count. Socket chunking/coalescing, direction, stage shape, and sampling affect the denominator. A separate benchmark should establish the number of boxes saved by carrying a reference through internal stages before changing the graph or interpreter. No boxing fix is included here.

## Reproduction and validation

Copy the extended benchmark into a worktree at the parent revision, then use identical commands on both revisions:

```bash
dotnet build src/benchmark/Akka.Benchmarks/Akka.Benchmarks.csproj -c Release
dotnet src/benchmark/Akka.Benchmarks/bin/Release/net10.0/Akka.Benchmarks.dll --filter '*ArterySinglePassEncodeBenchmarks*' --job short --buildTimeout 300 --artifacts /path/to/results
```

For each RemotePingPong invocation, build in Release and run the selected parent/candidate binary, waiting for completion before starting the next:

```bash
dotnet /path/to/RemotePingPong.dll run --artery --clients 10 --msgs 100000 --times 3 --payload real --serializer v2 --inbound-lanes 1 --outbound-lanes 1
```

Local checks: **297 Artery tests passed**, including Unicode and unpaired-surrogate wire-byte expectations, multibyte literals at 65,535/65,536 UTF-8 bytes, transport-context restoration, and existing ownership tests. The five new cases also passed on the parent, establishing preserved behavior. Two matching Remote API approvals, Release builds, whitespace verification, and Slopwatch passed. Hosted CI is tracked separately.

Raw benchmark reports, every RemotePingPong log, failed build evidence, test results, boxing stacks, binary hashes, and parent/candidate binaries are retained locally at `/home/petabridge/benchmark-results/akka.net/artery-utf8-literals-2026-10-13`.

## Stack delivery

After #8804 merged as `6659f8abb51dc5fea4a8aafcbebffa9525892032`, #8805 was rebased onto `dev`, replaying only its UTF-8 implementation, tests, benchmark extension, and report. A range comparison confirmed those two commits were unchanged by the rebase. The temporary stack base is no longer the PR target.

The rebased `dev` also includes the MessagePack 3.1.12 dependency update. The tables above remain historical measurements at their stated revisions; they are not new measurements of the rebased head. All 29 codec tests passed on the rebased branch. Hosted CI validates the broader integrated result separately.
