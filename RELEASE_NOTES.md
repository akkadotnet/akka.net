#### 1.3.16 November 14 2019 ####
**Maintenance Release for Akka.NET 1.3**

1.3.16 consists of non-breaking bugfixes and additions that have been contributed against the [Akka.NET v1.4.0 milestone](https://github.com/akkadotnet/akka.net/milestone/17) thus far.

This patch includes some small fixes, such as:

* [fix: NuGet symbols not published](https://github.com/akkadotnet/akka.net/pull/3966)
* [Akka.Cluster.Sharding: Consolidated passivation check on settings used in region and shard](https://github.com/akkadotnet/akka.net/pull/3961)
* [Akka.Cluster.Tools: Singleton - missing state change fix](https://github.com/akkadotnet/akka.net/pull/4003)
* [Akka.Cluster.Tools: Fixed singleton issue when leaving several nodes](https://github.com/akkadotnet/akka.net/pull/3962)

However, the biggest fix is for .NET Core 3.0 users. When .NET Core 3.0 was introduced, it broke some of the APIs in prior versions of [Hyperion](https://github.com/akkadotnet/Hyperion) which subsequently caused Akka.Cluster.Sharding and Akka.DistributedData users to have problems when attempting to run on .NET Core 3.0. These have been fixed as Akka.NET v1.3.16 is now running using the latest versions of Hyperion, which resolve this issue.

To [see the full set of changes in Akka.NET v1.3.16, click here](https://github.com/akkadotnet/akka.net/pull/4037).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 4 | 119 | 6 | Aaron Stannard |
| 3 | 531 | 126 | Ismael Hamed |
| 3 | 108 | 11 | Andre Loker |
| 2 | 2 | 2 | dependabot-preview[bot] |
| 1 | 6 | 1 | cptjazz |
| 1 | 1 | 1 | zbynek001 |