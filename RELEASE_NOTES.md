#### 1.3.16 October 30 2019 ####
**Maintenance Release for Akka.NET 1.3**

1.3.16 is a minor change, brought about by the introduction of [Hyperion v0.9.10](https://github.com/akkadotnet/Hyperion/releases/tag/0.9.10). This fixes a breaking change introduced by .NET Core 3.0, which could cause Akka.Cluster.Sharding users some problems when running apps on .NET Core 3.0.

Hyperion v0.9.10 also allows for full x-plat communciation between .NET Framework and .NET Core on all supported versions of each.

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 2 | 10 | 40 | Aaron Stannard |