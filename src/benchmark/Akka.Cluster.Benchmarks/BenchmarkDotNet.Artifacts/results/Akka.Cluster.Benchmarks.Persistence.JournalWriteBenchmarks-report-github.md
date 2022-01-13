``` ini

BenchmarkDotNet=v0.13.1, OS=Windows 10.0.19041.1415 (2004/May2020Update/20H1)
AMD Ryzen 7 1700, 1 CPU, 16 logical and 8 physical cores
.NET SDK=6.0.100
  [Host]     : .NET 6.0.0 (6.0.21.52210), X64 RyuJIT
  Job-DVMTLJ : .NET 6.0.0 (6.0.21.52210), X64 RyuJIT

InvocationCount=1  UnrollFactor=1  

```
|             Method | PersistentActors | WriteMsgCount |       Mean |     Error |    StdDev |     Median |      Gen 0 |     Gen 1 | Allocated |
|------------------- |----------------- |-------------- |-----------:|----------:|----------:|-----------:|-----------:|----------:|----------:|
| **WriteToPersistence** |                **1** |           **100** |   **2.110 ms** | **0.0776 ms** | **0.2123 ms** |   **2.032 ms** |          **-** |         **-** |    **631 KB** |
| **WriteToPersistence** |               **10** |           **100** |  **23.042 ms** | **0.9491 ms** | **2.7686 ms** |  **21.977 ms** |  **1000.0000** | **1000.0000** |  **6,410 KB** |
| **WriteToPersistence** |              **100** |           **100** | **196.058 ms** | **1.9166 ms** | **1.6004 ms** | **196.414 ms** | **14000.0000** | **3000.0000** | **63,377 KB** |
