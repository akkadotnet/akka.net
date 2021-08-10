#### 1.4.23 August 09 2021 ####
**Maintenance Release for Akka.NET 1.4**

Akka.NET v1.4.23 is designed to patch an issue that occurs on Linux machines using Akka.Cluster.Sharding with `akka.cluster.sharding.state-store-mode=ddata` and `akka.cluster.sharding.remember-entities=on`: "[System.DllNotFoundException: Unable to load shared library 'lmdb' or one of its dependencies](https://github.com/akkadotnet/akka.net/issues/5174)"

In [Akka.NET v1.4.21 we added built-in support for Akka.DistributedData.LightningDb](https://github.com/akkadotnet/akka.net/releases/tag/1.4.21) for use with the `remember-entities` setting, but we never received any reports about this issue until shortly after v1.4.22 was released. Fundamentally, the problem was that our downstream dependency, Lightning.NET, doesn't include any of the necessary Linux native binaries in their distributions currently. So in the meantime, we've published our own "vendored" distribution of Lightning.NET to NuGet until a new official one is released that includes these binaries.

There are some other small [fixes included in Akka.NET v1.4.23 and you can read about them here](https://github.com/akkadotnet/akka.net/milestone/54).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 8 | 136 | 2803 | Aaron Stannard |
| 2 | 61 | 3 | Gregorius Soedharmo |

#### 1.4.22 August 05 2021 ####
**Maintenance Release for Akka.NET 1.4**

Akka.NET v1.4.22 is a fairly large release that includes an assortment of performance and bug fixes.

**Performance Fixes**
Akka.NET v1.4.22 includes a _significant_ performance improvement for `Ask<T>`, which now requires 1 internal `await` operation instead of 3:

*Before*

|                        Method | Iterations |      Mean |     Error |    StdDev |     Gen 0 | Gen 1 | Gen 2 | Allocated |
|------------------------------ |----------- |----------:|----------:|----------:|----------:|------:|------:|----------:|
| RequestResponseActorSelection |      10000 | 83.313 ms | 0.7553 ms | 0.7065 ms | 4666.6667 |     - |     - |     19 MB |
|          CreateActorSelection |      10000 |  5.572 ms | 0.1066 ms | 0.1140 ms |  953.1250 |     - |     - |      4 MB |


*After*

|                        Method | Iterations |      Mean |     Error |    StdDev |     Gen 0 | Gen 1 | Gen 2 | Allocated |
|------------------------------ |----------- |----------:|----------:|----------:|----------:|------:|------:|----------:|
| RequestResponseActorSelection |      10000 | 71.216 ms | 0.9885 ms | 0.9246 ms | 4285.7143 |     - |     - |     17 MB |
|          CreateActorSelection |      10000 |  5.462 ms | 0.0495 ms | 0.0439 ms |  953.1250 |     - |     - |      4 MB |

**Bug Fixes and Improvements**

* [Akka: Use ranged nuget versioning for Newtonsoft.Json](https://github.com/akkadotnet/akka.net/pull/5099)
* [Akka: Pipe of Canceled Tasks](https://github.com/akkadotnet/akka.net/pull/5123)
* [Akka: CircuitBreaker's Open state should return a faulted Task instead of throwing](https://github.com/akkadotnet/akka.net/issues/5117)
* [Akka.Remote: Can DotNetty socket exception include information about the address?](https://github.com/akkadotnet/akka.net/issues/5130)
* [Akka.Remote: log full exception upon deserialization failure](https://github.com/akkadotnet/akka.net/pull/5121)
* [Akka.Cluster: SBR fix & update](https://github.com/akkadotnet/akka.net/pull/5147)
* [Akka.Streams: Restart Source|Flow|Sink: Configurable stream restart deadline](https://github.com/akkadotnet/akka.net/pull/5122)
* [Akka.DistributedData: ddata replicator stops but doesn't look like it can be restarted easily](https://github.com/akkadotnet/akka.net/pull/5145)
* [Akka.DistributedData: ddata ReadMajorityPlus and WriteMajorityPlus](https://github.com/akkadotnet/akka.net/pull/5146)
* [Akka.DistributedData: DData Max-Delta-Elements may not be fully honoured](https://github.com/akkadotnet/akka.net/issues/5157)

You can [see the full set of changes introduced in Akka.NET v1.4.22 here](https://github.com/akkadotnet/akka.net/milestone/52)

**Akka.Cluster.Sharding.RepairTool**
In addition to the work done on Akka.NET itself, we've also created a separate tool for cleaning up any left-over data in the event of an Akka.Cluster.Sharding cluster running with `akka.cluster.sharding.state-store-mode=persistence` was terminated abruptly before it had a chance to cleanup.

We've added documentation to the Akka.NET website that explains how to use this tool here: https://getakka.net/articles/clustering/cluster-sharding.html#cleaning-up-akkapersistence-shard-state

And the tool itself has documentation here: https://github.com/petabridge/Akka.Cluster.Sharding.RepairTool

| COMMITS | LOC+ | LOC- | AUTHOR |       
| --- | --- | --- | --- |                
| 16 | 1254 | 160 | Gregorius Soedharmo |
| 7 | 104 | 83 | Aaron Stannard |        
| 5 | 8 | 8 | dependabot[bot] |          
| 4 | 876 | 302 | Ismael Hamed |         
| 2 | 3942 | 716 | zbynek001 |           
| 2 | 17 | 3 | Andreas Dirnberger |      
| 1 | 187 | 2 | andyfurnival |           
| 1 | 110 | 5 | Igor Fedchenko |         
