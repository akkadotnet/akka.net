# Sequence boxing allocation results

Measured on 2026-10-14 at `4a18e7f1d94312e772ae9e8e6a9179866094f220`, including #8805's UTF-8 changes on the rebased MessagePack 3.1.12 base.

## Question and controls

The existing RemotePingPong allocation trace contains 40-byte boxed `ReadOnlySequence<byte>` objects at encode, kill-switch, termination, concat, restart/substream, and TCP stages. Source inspection explains the mechanism: `GraphStageLogic.Push<T>` assigns the element to `Connection.Slot`, whose type is `object`. Forwarding a value can box it again even in a fused graph.

`SequenceBoxingBenchmarks` measures whether one allocated reference carrier amortizes that cost. Both graphs start with the same reused 128-byte payload array, create their representation inside measured execution, traverse 0, 1, 4, or 8 custom forwarding stages, and aggregate message count and byte totals. The carrier is newly allocated for every element and contains the sequence. Each invocation awaits delivery of 100,000 messages and validates 12,800,000 bytes. Custom stages ensure each requested forwarding hop exists.

The zero-stage case includes the sequence's first box or the carrier's allocation. Per-message results also include amortized graph materialization and completion overhead. This is a fully fused synthetic graph, without TCP, serialization, restart substreams, async boundaries, batching, or pooled-buffer ownership.

## Generated BenchmarkDotNet output

The following output is copied directly from BenchmarkDotNet's GitHub Markdown exporter. Each operation is one delivered message.

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Core i9-9900K CPU 3.60GHz (Coffee Lake), 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.103
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1
WarmupCount=3

```
| Method          | ForwardingStages | Mean     | Error     | StdDev   | Ratio | RatioSD | Req/sec      | Gen0   | Gen1   | Allocated | Alloc Ratio |
|---------------- |----------------- |---------:|----------:|---------:|------:|--------:|-------------:|-------:|-------:|----------:|------------:|
| **SequenceValues**  | **0**                | **109.4 ns** |   **4.73 ns** |  **0.26 ns** |  **1.00** |    **0.00** | **9,140,087.72** | **0.0048** |      **-** |      **41 B** |        **1.00** |
| ReferenceFrames | 0                | 101.9 ns |  15.07 ns |  0.83 ns |  0.93 |    0.01 | 9,810,780.28 | 0.0048 |      - |      41 B |        1.00 |
|                 |                  |          |           |          |       |         |              |        |        |           |             |
| **SequenceValues**  | **1**                | **160.7 ns** |  **13.33 ns** |  **0.73 ns** |  **1.00** |    **0.01** | **6,222,943.23** | **0.0094** |      **-** |      **81 B** |        **1.00** |
| ReferenceFrames | 1                | 130.0 ns |   9.26 ns |  0.51 ns |  0.81 |    0.00 | 7,694,139.77 | 0.0048 |      - |      41 B |        0.51 |
|                 |                  |          |           |          |       |         |              |        |        |           |             |
| **SequenceValues**  | **4**                | **278.1 ns** |  **75.82 ns** |  **4.16 ns** |  **1.00** |    **0.02** | **3,596,038.36** | **0.0238** | **0.0003** |     **201 B** |        **1.00** |
| ReferenceFrames | 4                | 224.3 ns |  55.04 ns |  3.02 ns |  0.81 |    0.01 | 4,458,217.43 | 0.0047 |      - |      41 B |        0.20 |
|                 |                  |          |           |          |       |         |              |        |        |           |             |
| **SequenceValues**  | **8**                | **443.3 ns** | **347.80 ns** | **19.06 ns** |  **1.00** |    **0.05** | **2,255,732.04** | **0.0425** |      **-** |     **361 B** |        **1.00** |
| ReferenceFrames | 8                | 294.4 ns |   2.28 ns |  0.13 ns |  0.66 |    0.02 | 3,396,367.65 | 0.0047 |      - |      41 B |        0.11 |

## Interpretation and limits

At zero forwarding stages both methods report 41 B/message. Sequence allocations increase by about 40 B for each added forwarding stage: 81 B at one, 201 B at four, and 361 B at eight. The reference graph remains at 41 B, including its fresh carrier. At eight stages the measured allocation difference is 320 B/message. The sub-byte infrastructure overhead is rounded in the exported table.

This confirms repeated boxing in the controlled interpreter graph and establishes that including carrier construction does not erase the saving. It does not establish Artery's exact allocation saving, the customer's specific 280 B/application-message figure, pooled-owner correctness, or network throughput. Actual Artery boundaries also include substreams, socket chunks, and batching, so their message denominator differs. Timing includes carrier dereferencing versus direct sequence access; ShortRun, concurrent host workloads, and inability to set process priority limit timing conclusions. The eight-stage sequence timing has a particularly wide confidence interval.

The installed BenchmarkDotNet 0.15.8 implementation uses `GC.GetTotalAllocatedBytes(precise: true)` on this runtime. Its measurement brackets the completed workload, and MemoryDiagnoser divides by invocation count times `OperationsPerInvoke`. Interpreter-thread allocations are therefore counted; a thread-local allocation counter was not used.

A subsequent production change can carry an internal reference from encode through Artery-owned outbound stages, then unwrap at the sequence-based TCP boundary. Before claiming savings it needs a real-topology comparison, preserved `OwnedSequenceSegment` ownership through batching/TCP, poison-pool and teardown checks, and sequential parent/candidate RemotePingPong runs. This PR establishes the measurement control and changes no production stream representation.

## Reproduction and validation

```bash
dotnet build src/benchmark/Akka.Benchmarks/Akka.Benchmarks.csproj -c Release -m:1 -p:UseSharedCompilation=false -nodeReuse:false
MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0 dotnet src/benchmark/Akka.Benchmarks/bin/Release/net10.0/Akka.Benchmarks.dll --filter '*SequenceBoxingBenchmarks*' --job Short --buildTimeout 300 --artifacts /path/to/results
```

Release build passed with zero warnings/errors. All eight benchmark cases executed successfully; every invocation validates delivery totals. Slopwatch and whitespace checks passed. BenchmarkDotNet reports an inherited duplicate GitHub Markdown exporter warning from the shared configuration; all eight cases have populated results. No RemotePingPong process was launched for this benchmark-only change. Hosted CI is tracked separately.

Raw logs, generated CSV/HTML/Markdown reports, source hash, runtime metadata, and decompiled allocation-accounting excerpts are retained at `/home/petabridge/benchmark-results/akka.net/sequence-boxing-2026-10-14` with a SHA-256 manifest.
