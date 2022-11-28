``` ini

BenchmarkDotNet=v0.13.1, OS=Windows 10.0.19044.2251 (21H2)
AMD Ryzen 7 1700, 1 CPU, 16 logical and 8 physical cores
.NET SDK=7.0.100
  [Host]     : .NET Core 3.1.23 (CoreCLR 4.700.22.11601, CoreFX 4.700.22.12208), X64 RyuJIT
  DefaultJob : .NET Core 3.1.23 (CoreCLR 4.700.22.11601, CoreFX 4.700.22.12208), X64 RyuJIT


```
|                  Method |           Formatted |      Mean |     Error |    StdDev | Allocated |
|------------------------ |-------------------- |----------:|----------:|----------:|----------:|
| **Int64CharCountBenchmark** |                   **1** |  **4.471 ns** | **0.0402 ns** | **0.0357 ns** |         **-** |
| **Int64CharCountBenchmark** |                **1000** |  **4.818 ns** | **0.0517 ns** | **0.0484 ns** |         **-** |
| **Int64CharCountBenchmark** | **9223372036854775807** | **25.199 ns** | **0.3360 ns** | **0.2978 ns** |         **-** |
