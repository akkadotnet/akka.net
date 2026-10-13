# Artery encoding delegate allocation results

Measured on 2026-10-13 for the static `WithTransport` callback change.

## Revisions and environment

* Baseline production code: `23a8566a592c736240d81687c16080ca5612a3d7`.
* Candidate code and benchmark: `b5449a5051685f95f8e3fb6008272b0dacbf2fc0`.
* The new `ArterySinglePassEncodeBenchmarks` was also present during baseline measurement; production encoding was unchanged.
* Ubuntu 24.04.4, KVM guest, Intel Core i9-9900K, 8 visible cores; SDK 10.0.103, runtime 10.0.12, x64.
* BenchmarkDotNet 0.15.8 ShortRun: one launch, three warmups, three measured iterations, concurrent workstation GC.
* RemotePingPong: server GC and tiered PGO; loopback TCP; one inbound and one outbound lane, window 50, otherwise default settings.
* Other workloads used CPU during these runs. Timing results are directional observations, not a demonstrated throughput improvement or regression.

## Production encoder microbenchmark

This benchmark calls the actual single-pass codec overload with a reused byte-array payload and constant path strings. It includes serializer lookup and transport context, but excludes actor-path formatting, decode, detached owners, stream stages, and TCP.

| Payload bytes | Baseline allocation | Candidate allocation | Baseline mean | Candidate mean |
| --- | --- | --- | --- | --- |
| 32 | 240 B | 176 B | 224.5 ns | 216.3 ns |
| 256 | 240 B | 176 B | 243.0 ns | 216.5 ns |
| 4096 | 240 B | 176 B | 300.5 ns | 246.5 ns |

Observed reduction: **64 B per encode (26.7%)**, independent of payload size in these cases. This is not a claim of allocation-free transport. ShortRun timing uncertainty, especially in the baseline, precludes a reliable speedup claim.

## Integrated RemotePingPong

Unprofiled ping-pong runs, three repetitions per case. One-client cases use 1,000,000 messages per client; ten-client cases use 250,000. The harness counts both directions: respectively 2,000,000 and 5,000,000 completed application messages per repetition. The generated V2 real payload is 348 serialized bytes; toy is the existing string payload.

| Payload | Clients | Baseline msgs/s (three reps) | Candidate msgs/s (three reps) | Baseline median | Candidate median |
| --- | --- | --- | --- | --- | --- |
| toy | 1 | 279,174, 403,226, 382,556 | 291,164, 391,084, 382,996 | 382,556 | 382,996 |
| toy | 10 | 614,629, 810,373, 805,413 | 611,322, 807,624, 817,796 | 805,413 | 807,624 |
| real | 1 | 222,075, 184,878, 208,182 | 235,018, 196,368, 167,225 | 208,182 | 196,368 |
| real | 10 | 172,313, 438,944, 447,388 | 174,740, 370,289, 448,431 | 438,944 | 370,289 |

Every repetition completed with no drop/resend warnings. Systems are recreated between repetitions; these include cold startup/tiering effects and do not measure a long-lived warm association. No latency percentiles were collected. The baseline and candidate were run sequentially rather than interleaved, so these numbers establish a reproducible workload and a completion check, not a statistically controlled timing comparison.

### Interleaved follow-up

The initial generated-payload medians were lower on the candidate (5.7% at one client, 15.6% at ten clients). A fixed baseline/candidate/candidate/baseline sequence repeated the ten-client real-payload case with identical settings. Every invocation ran to completion before the next started, including all profiling runs.

| Order | Revision | Msgs/s (three reps) | Median msgs/s |
| --- | --- | --- | --- |
| 1 | baseline | 217,127, 413,087, 446,988 | 413,087 |
| 2 | candidate | 384,025, 329,577, 212,171 | 329,577 |
| 3 | candidate | 308,986, 378,015, 334,852 | 334,852 |
| 4 | baseline | 220,362, 227,708, 431,221 | 227,708 |

All four invocations completed without drop/resend warnings. The unchanged baseline's median moved from 413,087 to 227,708 msgs/s (44.9% lower), showing that this host cannot provide a stable timing control during these measurements. Other compiler and test processes were observed competing for CPU. Preserve both the initial lower candidate results and this control drift: throughput improvement and absence of regression remain unproven pending a quiet-machine comparison. Allocation and correctness findings are independent of that timing limitation.

## Allocation tracing

Separate `gc-verbose` traces used the real V2 payload, one client, 500,000 messages, one repetition (1,000,000 counted messages across both directions). Profiled throughput is excluded from the table above. Allocation tick counts are sampled, not exact object totals.

| Sampled type | Baseline allocation ticks | Candidate allocation ticks |
| --- | --- | --- |
| Encode closure (64-byte object) | 591 | 0 |
| Func returning PooledPayloadWriter (64-byte object) | 633 | 0 |

Reflection on the baseline codec identifies its nested closure as `ArteryEnvelopeCodec+<>c__DisplayClass2_0`; the candidate codec has a static callback holder instead. The integrated baseline samples both a closure and a delegate. The microbenchmark's measured net reduction is 64 B: do not substitute the sum of sampled object sizes for measured allocation savings on another call path or runtime. The cached static delegate can allocate at initialization; the change removes per-message creation.

## Reproduction and correctness

Copy the new benchmark into a worktree at the baseline revision before measuring. Build and run the same benchmark on each revision:

```bash
dotnet build src/benchmark/Akka.Benchmarks/Akka.Benchmarks.csproj -c Release
dotnet src/benchmark/Akka.Benchmarks/bin/Release/net10.0/Akka.Benchmarks.dll --filter '*ArterySinglePassEncodeBenchmarks*' --job short --artifacts /path/to/results
```

Build RemotePingPong in Release, then run each payload/client combination with the message counts above:

```bash
dotnet src/benchmark/RemotePingPong/bin/Release/net10.0/RemotePingPong.dll run --artery --clients 1 --msgs 1000000 --times 3 --inbound-lanes 1 --outbound-lanes 1 --payload toy
```

For generated V2, use `--payload real --serializer v2`. For ten clients, use `--clients 10 --msgs 250000`. For the separate trace, use the same real-payload command with `--msgs 500000 --times 1` launched under:

```bash
dotnet-trace collect --profile gc-verbose --output /path/to/result.nettrace --show-child-io -- dotnet /path/to/RemotePingPong.dll run ...
```

Local validation on the candidate: 292 Artery tests passed, including six context/restoration/cleanup cases; two matching Remote API approval tests passed; whitespace verification and Slopwatch passed. Successful test encoding is compared byte-for-byte with the explicit-parts codec. Serializer failure after writing returns the rented buffer and restores absent, matching, and different outer transport contexts.

Raw logs, traces, BenchmarkDotNet reports, binary hashes, and baseline RemotePingPong binaries are retained locally at `/home/petabridge/benchmark-results/akka.net/artery-encode-static-delegate-2026-10-13`. Hosted CI and merge state are tracked separately on the pull request.
