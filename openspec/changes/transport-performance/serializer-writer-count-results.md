# Generated serializer writer count: measurements and limits

Measured 2026-10-15 for [PR #8811](https://github.com/akkadotnet/akka.net/pull/8811), stacked on [PR #8808](https://github.com/akkadotnet/akka.net/pull/8808).

## Revisions and behavior

Baseline `ac4ae42339e034011582f54b822da80e40c4c3b3` plus identical copied test/benchmark controls; unchanged generator and old goldens. Candidate production frozen at `544bb833c9507148992cd9fff828d556c56209bd`; subsequent documentation does not change measured code. Benchmark SHA-256 on both: `71076a11227768d938f458a476ed92034bacb5e9100726611a5eb41db3a58067`.

Generated Serialize uses WrittenCount deltas for sealed PooledPayloadWriter and ArrayBufferWriter<byte> destinations. MessagePackWriter construction still acquires its initial memory before reading the starting count. Final Flush and checked delta retain prefix exclusion, repeated-write accounting, cap checks and failure ordering. Arbitrary IBufferWriter destinations keep the original counting wrapper and checked long-to-int return.

Only serializers without reachable custom formatters or dynamic envelope payloads get these branches. A valid formatter can Flush and advance through an alias; the original wrapper excludes those advances. Retaining fallback for callback-bearing serializers preserves that behavior, including collection-only formatter reachability. Real RemotePingPong qualifies. No public API, dependency or wire change.

## Validation

Both revisions passed all 599 serializer tests: zero failures/skips, including 22 new behavioral controls. New cases exercise prefixes/repeated messages, growth, partial commits, formatter flush/throw/alias behavior, collection-only formatter reachability, disposed/full-cap destinations and Reset reuse after failure. Nine reviewed goldens each add the same 18 lines to Serialize; removing it reconstructs the previous snapshot byte-for-byte. No Write/Read/Manifest/SizeHint/wire changes. Slopwatch and whitespace checks pass.

An initial test-fixture build missed three defining partial registration declarations and discovered zero tests. Those declarations were corrected. The next run passed 591 tests with eight expected golden-test failures; all 22 behavior controls passed. Reviewed snapshots were approved before the final 599-pass rerun. Failed attempts remain retained as evidence.

## Controlled BenchmarkDotNet result

Reuse the existing GeneratedMessagePackSerializerBenchmarks class with its four unchanged ReadOnlyList methods plus two targeted pooled/opaque controls. Writers and collections are created in setup; existing Reset operations reuse capacity. Setup checks exact bytes, preserved prefixes and relative counts. Opaque adapter is allocated once in setup. Known Write cases save 32 B/op; opaque fallback stays 32 B; SizeHint stays zero. Short timing intervals do not establish a speedup/regression.

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
| Method                     | Mean      | Error    | StdDev   | Gen0   | Allocated |
|--------------------------- |----------:|---------:|---------:|-------:|----------:|
| ReadOnlyList_ListWrite     | 251.39 ns | 62.36 ns | 3.418 ns | 0.0038 |      32 B |
| ReadOnlyList_ListSizeHint  |  91.17 ns | 53.12 ns | 2.912 ns |      - |         - |
| ReadOnlyList_ArrayWrite    | 251.92 ns | 65.58 ns | 3.595 ns | 0.0038 |      32 B |
| ReadOnlyList_ArraySizeHint |  72.79 ns | 56.59 ns | 3.102 ns |      - |         - |
| ReadOnlyList_PooledWrite   | 258.08 ns | 69.21 ns | 3.794 ns | 0.0038 |      32 B |
| ReadOnlyList_OpaqueWrite   | 268.61 ns | 73.95 ns | 4.054 ns | 0.0038 |      32 B |

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
| Method                     | Mean      | Error     | StdDev    | Gen0   | Allocated |
|--------------------------- |----------:|----------:|----------:|-------:|----------:|
| ReadOnlyList_ListWrite     | 255.15 ns |  18.19 ns |  0.997 ns |      - |         - |
| ReadOnlyList_ListSizeHint  |  94.47 ns | 170.77 ns |  9.360 ns |      - |         - |
| ReadOnlyList_ArrayWrite    | 257.06 ns | 229.18 ns | 12.562 ns |      - |         - |
| ReadOnlyList_ArraySizeHint |  78.13 ns |  16.26 ns |  0.891 ns |      - |         - |
| ReadOnlyList_PooledWrite   | 262.99 ns | 134.56 ns |  7.376 ns |      - |         - |
| ReadOnlyList_OpaqueWrite   | 258.27 ns | 155.10 ns |  8.502 ns | 0.0038 |      32 B |

## Actual transport evidence and uncertainty

**Actual Artery RemotePingPong:** fresh binaries, sequential ABBA (parent, candidate, candidate, parent), three repetitions per invocation. One client, one inbound/outbound lane, one million repeats, window 50, real V2 serializer 987001, 348-byte payload. Identical temporary process-wide allocation counters and harness; sources restored byte-for-byte.

| All six repetitions/revision | Parent | Candidate |
| --- | ---: | ---: |
| Median B/configured ping+echo | 3244.100 | 3221.509 |
| Range | 1247.003–3270.842 | 3168.645–3228.242 |
| Median configured msg/s | 198464 | 213160 |

**These process totals do not establish a net transport allocation saving or a speedup.** Parent invocation A1 varies from 3217.738 to 1247.003 B/configured message; every observation is retained. Severe unrelated host contention was observed (load 23 on 8 visible cores). That compromises timing, but is not a proven explanation for the allocation readings. All runs exited zero, used the same configured workload, and logged no transport/drop/resend error; normal-priority fallback was reported.

GC.GetTotalAllocatedBytes(precise: true) brackets priming through completion across both loopback systems and all managed threads. Construction/teardown are excluded; background/lazy initialization inside remains. The denominator is configured traffic, not independently observed drained deliveries; local primers and up to 49 pending echo chains/client remain possible (about 0.0049% at this workload). Those tails cannot explain the large allocation drift.

All unprofiled observations:

| Run | Rep | B/configured ping+echo | Configured msg/s | Elapsed ms |
|---|---:|---:|---:|---:|
| rpp-a1-baseline | 1 | 3217.738 | 130319 | 15347.47 |
| rpp-a1-baseline | 2 | 2376.620 | 152696 | 13098.43 |
| rpp-a1-baseline | 3 | 1247.003 | 153469 | 13032.06 |
| rpp-b1-candidate | 1 | 3168.645 | 108992 | 18350.09 |
| rpp-b1-candidate | 2 | 3178.170 | 127438 | 15694.76 |
| rpp-b1-candidate | 3 | 3222.767 | 255494 | 7828.00 |
| rpp-b2-candidate | 1 | 3220.251 | 180980 | 11051.01 |
| rpp-b2-candidate | 2 | 3223.571 | 245339 | 8152.81 |
| rpp-b2-candidate | 3 | 3228.242 | 276435 | 7235.06 |
| rpp-a2-baseline | 1 | 3270.462 | 243458 | 8215.88 |
| rpp-a2-baseline | 2 | 3270.842 | 334281 | 5983.80 |
| rpp-a2-baseline | 3 | 3270.696 | 338295 | 5912.84 |

**Separate allocation profiles:** each revision runs three repetitions of the same one-client/one-million-repeat workload (6 million configured ping-plus-echo count per trace). They run sequentially, separately from unprofiled timing. Both report zero lost events and zero missing counting-writer stacks.

| Exact sampled type: AkkaGeneratedCountingBufferWriter, observed size 32 B | Parent ticks | Candidate ticks |
| --- | ---: | ---: |
| Real serializer / Artery encode stacks | 1648 | 0 |

Parent samples include early RealBenchmarkSerializer.Serialize frames and late inlined ArteryEnvelopeCodec.Encode callers through 26.835 s. The candidate samples no counting-writer objects. This corroborates the generated-code and controlled 32 B/write result. GC AllocationTick counts and quantum weights are sampled evidence, not exact allocation totals or transport bytes/message; whole-process traces include startup/teardown.

**Accounting follow-up:** GCGlobalHeapHistory confirms changing server-GC heap counts in both profiled runs: parent 1→3→5→4→5; candidate 1→3→5→4→3→4→8→6. Runtime-counter accounting under those transitions is a concrete hypothesis to investigate, not a confirmed cause of the unstable repetition totals. These traces lack exact counter-window markers/raw start-and-end counter readings, so no causal attribution is claimed. Validate that measurement before using small process-total differences to judge later PRs.

**Hosted validation:** Azure132996 started against exact synthetic merge 2d684a6d3 (base ac4ae4233/head 544bb833c). Hosted CI is pending; local 599-test results are complete. No merge is requested or implied.

## Next targets

1. Async callback closure: ConcurrentAsyncCallback.OnAsyncInput allocates its display class before testing whether producer tracing context exists. Actual retained IL places newobj at IL_0000 before HasValue; late Artery read/ack profiles observe 64 B objects. Move tracing-only closure creation into a separate helper, preserving context, ordering, feedback and errors. Savings per application message require a separate measurement.
2. Artery literal caching: decode currently materializes UTF-8 manifest and actor-path strings before resolving refs. A bounded decoder-owned byte-to-string cache could avoid repeated conversion without changing the wire protocol. Own miss keys and check collisions; never retain pooled input memory. Resolved-ref caching additionally needs actorUID, temporary/unresolved refs, remote-deployment retries, provider/address scope and lane ownership controls. Current profiles do not establish the original 460 B attribution; the benchmark manifest is only 17 characters.
3. Restart/substream payload boxing: recurring OnNext(object) handoffs still box sequences. A typed message also grows, so measure net message+box size and actual frequency; do not equate removing a 40 B box with 40 B net savings. Full actor-ref/manifest compression remains separate UID/versioned protocol work.

The TCP nonnullable-sequence guard has box IL, but retained samples occur early; a steady per-message benefit is unproven. Larger owner/segment pooling changes must preserve asynchronous buffer ownership.

## Reproduction and retained evidence

Raw generated reports, all 12 repetitions, TRX, commands, source/binary hashes, temporary harness patch, traces, analysis and failed attempts are archived with verified hashes at `/home/petabridge/benchmark-results/akka.net/serializer-writer-count-2026-10-15`. The PR embeds both generated tables directly.

```bash
dotnet test src/core/Akka.Serialization.V2.Tests/Akka.Serialization.V2.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false -nodeReuse:false --logger trx

dotnet build src/benchmark/Akka.Benchmarks/Akka.Benchmarks.csproj -c Release -m:1 -p:UseSharedCompilation=false -nodeReuse:false

dotnet src/benchmark/Akka.Benchmarks/bin/Release/net10.0/Akka.Benchmarks.dll --filter '*GeneratedMessagePackSerializerBenchmarks.ReadOnlyList_*' --job Short --buildTimeout 300 --artifacts /path/to/results

dotnet /path/to/frozen/RemotePingPong.dll run --artery --clients 1 --msgs 1000000 --times 3 --payload real --serializer v2 --inbound-lanes 1 --outbound-lanes 1
```

Retained scripts contain the exact executed commands and identical temporary allocation-counter window. Never overlap RemotePingPong processes; profiled timings are excluded from performance claims.
