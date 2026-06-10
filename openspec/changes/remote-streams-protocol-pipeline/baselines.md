## Baseline Environment

Recorded on 2026-06-10 from `feature/remote-streams-protocol-pipeline` at `fdaac7172`.

The branch contained OpenSpec notes and wire-format tests only; no production Remote codec or transport implementation was changed from the upstream `dev` baseline.

## AkkaPduCodecBenchmark

Command:

```bash
dotnet run -c Release --project "src/benchmark/Akka.Benchmarks/Akka.Benchmarks.csproj" -- --filter "*AkkaPduCodecBenchmark*"
```

BenchmarkDotNet reported that it could not set high process priority because of permissions. The run continued at normal priority.

```text
BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Core i9-9900K CPU 3.60GHz (Coffee Lake), 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.103
  [Host]     : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3
```

| Method                 | Mean      | Error     | StdDev    | Gen0   | Gen1   | Allocated |
|----------------------- |----------:|----------:|----------:|-------:|-------:|----------:|
| WritePayloadPdu        | 768.59 ns | 15.340 ns | 29.919 ns | 0.1891 |      - |    1584 B |
| DecodePayloadPdu       | 981.64 ns | 19.170 ns | 31.498 ns | 0.2234 |      - |    1872 B |
| DecodePduOnly          |  90.26 ns |  1.696 ns |  3.267 ns | 0.0573 | 0.0001 |     480 B |
| DecodeMessageOnly      | 765.24 ns | 15.228 ns | 20.844 ns | 0.1453 |      - |    1216 B |
| DeserializePayloadOnly |  81.76 ns |  1.606 ns |  2.501 ns | 0.0200 |      - |     168 B |

## RemotePingPong

Command:

```bash
dotnet run -c Release --project "src/benchmark/RemotePingPong/RemotePingPong.csproj" -- 1
```

The benchmark could not elevate process priority because of permissions and continued at normal priority.

```text
OSVersion:                         Unix 6.8.0.117
ProcessorCount:                    8
ClockSpeed:                        0 MHZ
Actor Count:                       16
Messages sent/received per client: 200000  (2e5)
Is Server GC:                      True
Thread count:                      36
```

| Num clients | Total msg | Msgs/sec | Total ms | Start threads | End threads |
|------------:|----------:|---------:|---------:|--------------:|------------:|
|           1 |   200000 |    77550 |  2579.66 |            36 |          51 |
|           5 |  1000000 |   379076 |  2638.95 |            59 |          67 |
|          10 |  2000000 |   617284 |  3240.89 |            75 |          75 |
|          15 |  3000000 |   656599 |  4569.25 |            83 |          83 |
|          20 |  4000000 |   714414 |  5599.08 |            91 |          83 |
|          25 |  5000000 |   677415 |  7381.23 |            91 |          76 |
|          30 |  6000000 |   667335 |  8991.56 |            84 |          62 |
