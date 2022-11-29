``` ini

BenchmarkDotNet=v0.13.1, OS=Windows 10.0.19044.2251 (21H2)
AMD Ryzen 7 1700, 1 CPU, 16 logical and 8 physical cores
.NET SDK=7.0.100
  [Host]     : .NET 7.0.0 (7.0.22.51805), X64 RyuJIT
  DefaultJob : .NET 7.0.0 (7.0.22.51805), X64 RyuJIT


```
|                                     Method |        Uid |       Mean |     Error |    StdDev |  Gen 0 | Allocated |
|------------------------------------------- |----------- |-----------:|----------:|----------:|-------:|----------:|
|                            **ActorPath_Parse** |          **1** | **268.316 ns** | **1.5227 ns** | **1.2715 ns** | **0.0992** |     **416 B** |
|                           ActorPath_Concat |          1 |  38.400 ns | 0.1241 ns | 0.1036 ns | 0.0268 |     112 B |
|                           ActorPath_Equals |          1 |   4.377 ns | 0.0361 ns | 0.0320 ns |      - |         - |
|                         ActorPath_ToString |          1 |  50.872 ns | 0.1519 ns | 0.1269 ns | 0.0268 |     112 B |
|            ActorPath_ToSerializationFormat |          1 | 168.876 ns | 1.6593 ns | 1.4709 ns | 0.0610 |     256 B |
| ActorPath_ToSerializationFormatWithAddress |          1 | 168.242 ns | 2.5289 ns | 2.1117 ns | 0.0610 |     256 B |
|                            **ActorPath_Parse** |     **100000** | **281.081 ns** | **1.4482 ns** | **1.2838 ns** | **0.0992** |     **416 B** |
|                           ActorPath_Concat |     100000 |  38.480 ns | 0.1676 ns | 0.1309 ns | 0.0268 |     112 B |
|                           ActorPath_Equals |     100000 |   4.377 ns | 0.0479 ns | 0.0425 ns |      - |         - |
|                         ActorPath_ToString |     100000 |  50.507 ns | 0.1441 ns | 0.1203 ns | 0.0268 |     112 B |
|            ActorPath_ToSerializationFormat |     100000 | 180.087 ns | 0.3269 ns | 0.2552 ns | 0.0629 |     264 B |
| ActorPath_ToSerializationFormatWithAddress |     100000 | 185.135 ns | 3.4828 ns | 3.0874 ns | 0.0629 |     264 B |
|                            **ActorPath_Parse** | **2147483647** | **300.098 ns** | **3.0413 ns** | **2.6960 ns** | **0.0992** |     **416 B** |
|                           ActorPath_Concat | 2147483647 |  39.325 ns | 0.8076 ns | 0.7159 ns | 0.0268 |     112 B |
|                           ActorPath_Equals | 2147483647 |   4.360 ns | 0.0337 ns | 0.0315 ns |      - |         - |
|                         ActorPath_ToString | 2147483647 |  51.842 ns | 1.0444 ns | 1.0725 ns | 0.0268 |     112 B |
|            ActorPath_ToSerializationFormat | 2147483647 | 193.560 ns | 2.6633 ns | 2.2240 ns | 0.0648 |     272 B |
| ActorPath_ToSerializationFormatWithAddress | 2147483647 | 194.647 ns | 3.9673 ns | 6.1767 ns | 0.0648 |     272 B |
