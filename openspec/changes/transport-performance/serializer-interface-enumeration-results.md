# Generated IReadOnlyList enumeration: before and after

Measured 2026-10-14 for [PR #8808](https://github.com/akkadotnet/akka.net/pull/8808), stacked on [#8807](https://github.com/akkadotnet/akka.net/pull/8807).

## Scope and revisions

Baseline `5e9db44be4297d9026202f428ed633e21571c65d` uses the parent generator plus identical new benchmark/test controls. Candidate production is frozen at `0d7cc90a64edcd4dee1e0d52b727e08c10dfcf99`; subsequent documentation does not change measured code. Benchmark source SHA-256: `2515dd0ce4e0a1ac044cbe1e8cd3441b480e4fbb2cafe3704d997ce9b94717db` on both.

The generator specializes native IReadOnlyList traversal for exact runtime List<T> and arrays in Write and SizeOf. Derived/custom implementations keep interface foreach. Exact List enumeration preserves version checks. The traversal expression is captured once after the existing Count read. Only collections whose immediate element is not another native collection specialize, avoiding exponential expansion for directly nested lists. Null handling, headers, field order, reader paths, wire bytes and public APIs retain existing behavior.

## Verification

Both baseline and candidate passed all 577 serializer tests, zero failures/skips. Nine new cases cover empty/populated List/array wire identity and exact sizes, custom and derived enumeration without indexer use, mutation fail-fast in Write/SizeHint, and two/three-level native nesting. The generated golden adds 36 semantic lines for two traversal branches; the larger textual diff also renumbers local names. Wire snapshots are unchanged. Independent source/golden review, Slopwatch and whitespace checks passed.

The initial framework-specific build failed before discovery (NETSDK1005); a later expected golden mismatch was reviewed and approved before the complete passing rerun. Those attempts are retained, not counted as successful tests.

Raw logs, commands, TRX, exact generated BenchmarkDotNet reports, instrumented binaries/patches, hashes, traces and analysis are retained at `/home/petabridge/benchmark-results/akka.net/serializer-interface-enumeration-2026-10-14`. The PR embeds the generated tables directly.

## Controlled BenchmarkDotNet measurements

Reuse the existing GeneratedMessagePackSerializerBenchmarks class with four targeted methods, prebuilt eight-reading collections and a reused buffer. Setup validates equal bytes and exact hints. List Write saves 40 B/op; array Write saves 32 B/op. Both warmed SizeHint cases remain at zero allocation. The 32 B counting-writer wrapper remains. ShortRun timings are noisy and do not establish a speedup or a SizeHint regression.

Baseline:

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Core i9-9900K CPU 3.60GHz (Coffee Lake), 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.103
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                     | Mean      | Error     | StdDev    | Gen0   | Allocated |
|--------------------------- |----------:|----------:|----------:|-------:|----------:|
| ReadOnlyList_ListWrite     | 424.55 ns | 321.83 ns | 17.641 ns | 0.0086 |      72 B |
| ReadOnlyList_ListSizeHint  |  90.32 ns |  65.51 ns |  3.591 ns |      - |         - |
| ReadOnlyList_ArrayWrite    | 382.23 ns | 218.10 ns | 11.955 ns | 0.0076 |      64 B |
| ReadOnlyList_ArraySizeHint |  87.45 ns |  17.48 ns |  0.958 ns |      - |         - |

Candidate:

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Core i9-9900K CPU 3.60GHz (Coffee Lake), 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.103
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                     | Mean      | Error       | StdDev    | Median    | Gen0   | Allocated |
|--------------------------- |----------:|------------:|----------:|----------:|-------:|----------:|
| ReadOnlyList_ListWrite     | 300.69 ns |   360.02 ns | 19.734 ns | 294.39 ns | 0.0038 |      32 B |
| ReadOnlyList_ListSizeHint  |  96.59 ns |   109.59 ns |  6.007 ns |  94.54 ns |      - |         - |
| ReadOnlyList_ArrayWrite    | 246.55 ns |    61.42 ns |  3.367 ns | 246.13 ns | 0.0038 |      32 B |
| ReadOnlyList_ArraySizeHint | 138.06 ns | 1,627.69 ns | 89.219 ns |  88.41 ns |      - |         - |

## Actual transport and stack evidence

**Actual Artery RemotePingPong:** sequential ABBA (parent, candidate, candidate, parent), three repetitions per invocation. Each uses one client, one inbound/outbound lane, one million repeats, window 50, real source-generated V2 payload (348 bytes). Fresh builds and identical temporary precise process-wide GC counters; temporary source restored byte-for-byte.

| All six repetitions/revision | Parent | Candidate |
| --- | ---: | ---: |
| Median B/configured ping+echo | 3303.639 | 3268.673 |
| Range | 3276.731–3309.536 | 2579.136–3274.010 |
| Median configured msg/s | 222240 | 330360.5 |
| Throughput range | 137599–306843 | 86761–369481 |

The median difference is **34.965 B/configured ping+echo (1.06%)**, a noisy process-level estimate consistent with the isolated Write allocation saving. Candidate invocation B2/repetition 2 is anomalously low; no repetitions were discarded. Baseline also drifts. The large timing ranges and host contention prevent a throughput-gain claim. Logs show no transport/drop/resend errors; process priority elevation was denied on all four runs. The low-allocation outlier's cause remains unresolved.

GC.GetTotalAllocatedBytes(precise: true) covers both loopback systems and all managed threads from before priming until completion; construction/teardown are outside, incidental background/lazy initialization inside. The denominator is configured traffic, not independently observed drained deliveries: local primers and up to 49 pending echo chains/client remain possible (about 0.0049% at this workload). Each repetition recreates both ActorSystems. This is not exact transport-only or boxing-only accounting.

All unprofiled repetitions:

| Run | Rep | B/configured ping+echo | Configured msg/s | Elapsed ms |
|---|---:|---:|---:|---:|
| rpp-a1-baseline | 1 | 3292.704 | 137599 | 14535.33 |
| rpp-a1-baseline | 2 | 3307.676 | 300436 | 6657.38 |
| rpp-a1-baseline | 3 | 3306.969 | 306843 | 6518.04 |
| rpp-b1-candidate | 1 | 3221.830 | 86761 | 23052.90 |
| rpp-b1-candidate | 2 | 3269.407 | 330524 | 6051.38 |
| rpp-b1-candidate | 3 | 3270.891 | 330197 | 6057.69 |
| rpp-b2-candidate | 1 | 3267.940 | 217581 | 9192.37 |
| rpp-b2-candidate | 2 | 2579.136 | 369481 | 5413.70 |
| rpp-b2-candidate | 3 | 3274.010 | 336418 | 5945.34 |
| rpp-a2-baseline | 1 | 3309.536 | 209710 | 9537.70 |
| rpp-a2-baseline | 2 | 3300.308 | 234770 | 8519.76 |
| rpp-a2-baseline | 3 | 3276.731 | 149892 | 13343.59 |

**Separate allocation traces:** parent and candidate each ran one client × one million repeats sequentially under gc-verbose plus SampleProfiler. Both report zero lost events; instrumented timings are excluded from performance conclusions. Whole-process traces include startup/teardown; resolved serializer/Artery stacks identify the relevant sites.

| Exact sampled type: Enumerator[RemotePingPong.RealPayload.V2.Reading], observed size 40 B | Parent ticks | Candidate ticks |
| --- | ---: | ---: |
| Generated WriteRealBenchmarkMessage | 831 | 0 |
| Generated SizeOfRealBenchmarkMessage | 29 | 0 |
| Total | 860 | 0 |

Allocation ticks/quantum weights are sampled, not object counts or exact bytes. Their disappearance corroborates the controlled benchmark and generated-code change; it does not establish 80 B saved on every encode. The 32 B counting-writer type remains sampled on both revisions (576/563 ticks).

**Hosted validation:** retargeting the stack base to the allowed feature/* namespace triggered Azure build 132962 against the exact synthetic merge (base5e9db44be/head0d7cc90a6), preserving the code diff. Hosted validation is running; local 577-test results above are complete. Parent #8807 passed its same-source validation audit before being marked ready; new same-source branch-triggered checks are separate. Neither PR is merged.

## Reproduction

Use the identical benchmark controls on each revision and run workloads sequentially. Retained scripts include exact commands, allocation-counter patch and source/binary hashes.

```bash
dotnet test src/core/Akka.Serialization.V2.Tests/Akka.Serialization.V2.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false -nodeReuse:false --logger trx

dotnet build src/benchmark/Akka.Benchmarks/Akka.Benchmarks.csproj -c Release -m:1 -p:UseSharedCompilation=false -nodeReuse:false

dotnet src/benchmark/Akka.Benchmarks/bin/Release/net10.0/Akka.Benchmarks.dll --filter '*GeneratedMessagePackSerializerBenchmarks.ReadOnlyList_*' --job Short --buildTimeout 300 --artifacts /path/to/results

dotnet /path/to/frozen/RemotePingPong.dll run --artery --clients 1 --msgs 1000000 --times 3 --payload real --serializer v2 --inbound-lanes 1 --outbound-lanes 1
```

Separate profiles use gc-verbose plus Microsoft-DotNETCore-SampleProfiler and one repetition. Never substitute profiled timings for the unprofiled comparison. Remaining writer wrappers and restart/substream boxes are future targets requiring separate changes and measured net savings.
