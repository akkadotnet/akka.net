#### 1.4.11 August 20th 2020 ####
**Placeholder for nightlies**

#### 1.4.10 August 20th 2020 ####
**Maintenance Release for Akka.NET 1.4**

Akka.NET v1.4.10 includes some minor bug fixes and some major feature additions to Akka.Persistence.Query:

* [Fixed: Akka.Persistence.Sql SqlJournal caching all Persistence Ids in memory does not scale](https://github.com/akkadotnet/akka.net/issues/4524)
* [Fixed Akka.Persistence.Query PersistenceIds queries now work across all nodes, rather than local node](https://github.com/akkadotnet/akka.net/pull/4531)
* [Akka.Actor: Akka.Pattern: Pass in clearer error message into OpenCircuitException](https://github.com/akkadotnet/akka.net/issues/4314)
* [Akka.Persistence: Allow persistence plugins to customize JournalPerfSpec's default values](https://github.com/akkadotnet/akka.net/pull/4544)
* [Akka.Remote: Racy RemotingTerminator actor crash in Akka.Remote initialization](https://github.com/akkadotnet/akka.net/issues/4530)

To see the [full set of fixes in Akka.NET v1.4.10, please see the milestone on Github](https://github.com/akkadotnet/akka.net/milestone/41).



#### 1.4.9 July 21 2020 ####
**Maintenance Release for Akka.NET 1.4**

Akka.NET v1.4.9 features some important bug fixes for Akka.NET v1.4:

* [Akka: Re-enable dead letter logging after specified duration](https://github.com/akkadotnet/akka.net/pull/4513)
* [Akka: Optimize allocations in `ActorPath.Join()`](https://github.com/akkadotnet/akka.net/pull/4510)
* [Akka.IO: Optimize TCP-related actor creation in Akka.IO](https://github.com/akkadotnet/akka.net/pull/4509)
* [Akka.DistributedData: Resolve "An Item with the same key but different value already exists" error during pruning](https://github.com/akkadotnet/akka.net/pull/4512)
* [Akka.Cluster: Cluster event listener that logs all events](https://github.com/akkadotnet/akka.net/pull/4502)
* [Akka.Cluster.Tools.Singleton.ClusterSingletonManager bug: An element with the same key but a different value already exists](https://github.com/akkadotnet/akka.net/issues/4474)

To see the [full set of fixes in Akka.NET v1.4.9, please see the milestone on Github](https://github.com/akkadotnet/akka.net/milestone/40).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 6 | 1008 | 90 | Gregorius Soedharmo |
| 5 | 475 | 27 | Ismael Hamed |
| 3 | 6 | 6 | dependabot-preview[bot] |
| 2 | 28 | 5 | Petri Kero |
| 1 | 3 | 0 | Aaron Stannard |
| 1 | 2 | 2 | tometchy |
| 1 | 1 | 1 | to11mtm |
| 1 | 1 | 1 | Kevin Preller |

#### 1.4.8 June 17 2020 ####
**Maintenance Release for Akka.NET 1.4**

Akka.NET v1.4.8 features some important bug fixes for Akka.NET v1.4:

* [Akka: fix issue with setting IActorRefProvider via BootstrapSetup](https://github.com/akkadotnet/akka.net/pull/4473)
* [Akka.Cluster: Akka v1.4 Idle CPU usage increased comparing v1.3](https://github.com/akkadotnet/akka.net/issues/4434)
* [Akka.Cluster.Sharding: Backport of the feature called ClusterDistribution in Lagom](https://github.com/akkadotnet/akka.net/pull/4455)
* [Akka.TestKit: added ActorSystemSetup overload for TestKits](https://github.com/akkadotnet/akka.net/pull/4464)

To see the [full set of fixes in Akka.NET v1.4.8, please see the milestone on Github](https://github.com/akkadotnet/akka.net/milestone/39).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 7 | 310 | 24 | Aaron Stannard |
| 6 | 8 | 8 | dependabot-preview[bot] |
| 2 | 669 | 10 | Ismael Hamed |
| 2 | 38 | 27 | Gregorius Soedharmo |
| 1 | 1 | 1 | Bartosz Sypytkowski |

#### 1.4.7 May 26 2020 ####
**Maintenance Release for Akka.NET 1.4**

Akka.NET v1.4.7 is a fairly sizable path that introduces some significant new features and changes:

* [Akka: added `akka.logger-async-start` HOCON setting to allow loggers to start without blocking `ActorSystem` creation](https://github.com/akkadotnet/akka.net/pull/4424) - useful for developers running Akka.NET on Xamarin or in other environments with low CPU counts;
* [Akka: ported `ActorSystemSetup` to allow programmatic config of serializers and other `ActorSystem` properties](https://github.com/akkadotnet/akka.net/issues/4426)
* [Akka.Streams: update and modernize Attributes](https://github.com/akkadotnet/akka.net/pull/4437)
* [Akka.DistributedData.LightningDb: Wire format stabilized](https://github.com/akkadotnet/akka.net/issues/4261) - Akka.Distributed.Data.LightningDb is now ready for production use.

To see the full set of changes for Akka.NET 1.4.7, please [see the 1.4.7 milestone](https://github.com/akkadotnet/akka.net/milestone/38).

#### 1.4.6 May 12 2020 ####
**Maintenance Release for Akka.NET 1.4**

Akka.NET v1.4.6 consists of mainly minor bug fixes and updates:

* [Akka.Cluster.DistributedData: Akka.DistributedData.ORDictionary Unable to cast object of type](https://github.com/akkadotnet/akka.net/issues/4400)
* [Akka: add CoordinatedShutdown.run-by-actor-system-terminated setting](https://github.com/akkadotnet/akka.net/issues/4203) - runs `CoordinatedShutdown` whenever `ActorSystem.Terminate()` is called.
* [Akka.Actor: Inconsistent application of SupervisorStrategy on Pool routers](https://github.com/akkadotnet/akka.net/issues/3626)

To see the full set of changes for Akka.NET 1.4.6, please [see the 1.4.6 milestone](https://github.com/akkadotnet/akka.net/milestone/37).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 3 | 746 | 96 | Gregorius Soedharmo |
| 1 | 65 | 117 | Igor Fedchenko |
| 1 | 10 | 5 | Bogdan-Rotund |

#### 1.4.5 April 29 2020 ####
**Maintenance Release for Akka.NET 1.4**

Akka.NET v1.4.5 consists mostly of bug fixes and quality-of-life improvements.

* [Akka.Cluster.Sharding: release shard lease when shard stopped](https://github.com/akkadotnet/akka.net/pull/4369)
* [Akka.Cluster.DistributedData: ORMultiValueDictionary - DistributedData Serialization InvalidCastException Unable to cast object of type 'DeltaGroup[System.String]' to type 'Akka.DistributedData.ORSet1[System.String]'](https://github.com/akkadotnet/akka.net/issues/4367)
* [Akka.Actor: Support double wildcards in actor deployment config](https://github.com/akkadotnet/akka.net/issues/4136)

To see the full set of changes for Akka.NET 1.4.5, please [see the 1.4.5 milestone](https://github.com/akkadotnet/akka.net/milestone/36).

| COMMITS | LOC+ | LOC- | AUTHOR |      
| --- | --- | --- | --- |               
| 3 | 6 | 6 | Michael Handschuh |       
| 3 | 486 | 91 | Aaron Stannard |       
| 3 | 3 | 3 | dependabot-preview[bot] | 
| 2 | 44 | 3 | zbynek001 |              
| 1 | 7 | 0 | JoRo |                    
| 1 | 6 | 21 | Ismael Hamed |           
| 1 | 3 | 3 | Igor Fedchenko |          
| 1 | 163 | 60 | Gregorius Soedharmo |  
| 1 | 14 | 2 | Bogdan Rotund |          

#### 1.4.4 March 31 2020 ####
**Maintenance Release for Akka.NET 1.4**

Akka.NET v1.4.4 includes one major fix for HOCON fallback configurations, a new module (Akka.Coordination), and some major improvements to Akka.Cluster.Tools / Akka.Cluster.Sharding:

* [Akka.Coordination: Lease API & integration](https://github.com/akkadotnet/akka.net/pull/4344)
* [Akka: Timers for self scheduled messages added, FSM timers fixes](https://github.com/akkadotnet/akka.net/pull/3778)
* [Akka **Important** Bugfix: Config.WithFallback is acting inconsistently](https://github.com/akkadotnet/akka.net/pull/4358)
* [Akka.Cluster.Sharding: Updates](https://github.com/akkadotnet/akka.net/pull/4354)

To see the full set of changes for Akka.NET 1.4.4, please [see the 1.4.4 milestone](https://github.com/akkadotnet/akka.net/milestone/35).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 3 | 4845 | 225 | zbynek001 |
| 2 | 3 | 3 | dependabot-preview[bot] |
| 2 | 159 | 0 | Aaron Stannard |
| 2 | 1099 | 23 | Gregorius Soedharmo |
| 1 | 34 | 5 | Petri Kero |
| 1 | 1 | 1 | Felix Reisinger |

#### 1.4.3 March 18 2020 ####
**Maintenance Release for Akka.NET 1.4**

Akka.NET v1.4.3 fixes one major issue that affected Akka.Persistence.Sql users as part of the v1.4 rollout:

* [Bugfix: No connection string for Sql Event Journal was specified](https://github.com/akkadotnet/akka.net/issues/4343)

To see the full set of changes for Akka.NET 1.4.3, please [see the 1.4.3 milestone](https://github.com/akkadotnet/akka.net/milestone/34).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 1 | 78 | 2 | Gregorius Soedharmo |
| 1 | 2 | 2 | dependabot-preview[bot] |
| 1 | 172 | 11 | Ismael Hamed |

#### 1.4.2 March 12 2020 ####
**Maintenance Release for Akka.NET 1.4**

Akka.NET v1.4.2 fixes two issues that affected users as part of the Akka.NET v1.4.1 rollout:

* [Bugfix: Missing v1.4.1 NuGet packages](https://github.com/akkadotnet/akka.net/issues/4332)
* [Bugfix: App.config loading broken in Akka.NET v1.4.1](https://github.com/akkadotnet/akka.net/issues/4330)

To see the full set of changes for Akka.NET 1.4.2, please [see the 1.4.2 milestone](https://github.com/akkadotnet/akka.net/milestone/32).

#### 1.4.1 March 11 2020 ####
**Major release for Akka.NET 1.4**

Akka.NET v1.4 is a non-breaking upgrade for all Akka.NET v1.3 and earlier users. It introduces many new features, all of which can be found in our ["What's new in Akka.NET v1.4" page](https://getakka.net/community/whats-new/akkadotnet-v1.4.html).

Major changes include:

* Akka.Cluster.Sharding and Akka.DistributedData are now out of beta status and are treated as mature modules.
* Akka.Remote's performance has significantly increased as a function of our new batching mode (see the numbers) - which is tunable via HOCON configuration to best support your needs. [Learn how to performance optimize Akka.Remote here](https://getakka.net/articles/remoting/performance.html).
* Added new Akka.Cluster.Metrics module to measure and broadcast the performance of each node, also includes some routers that can use this information to direct traffic to the "least busy" nodes.
* Added [Stream References to Akka.Streams](https://getakka.net/articles/streams/streamrefs.html),  a feature which allows Akka.Streams graphs to span across the network.
* Moved from .NET Standard 1.6 to .NET Standard 2.0 and dropped support for all versions of .NET Framework that aren't supported by .NET Standard 2.0 - this means issues caused by our polyfills (i.e. SerializableAttribute) should be fully resolved now.
* Moved onto modern C# best practices, such as changing our APIs to support `System.ValueTuple` over regular tuples.

And hundreds of other fixes and improvements. See the full set of changes for Akka.NET v1.4 here:

* [Akka.NET v1.4 Milestone](https://github.com/akkadotnet/akka.net/milestone/17)
* [Akka.NET v1.4.1 Milestone](https://github.com/akkadotnet/akka.net/milestone/33)

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 199 | 41324 | 26885 | Aaron Stannard |
| 46 | 63 | 63 | dependabot-preview[bot] |
| 34 | 16800 | 6804 | Igor Fedchenko |
| 13 | 11671 | 1059 | Bartosz Sypytkowski |
| 10 | 802 | 317 | Ismael Hamed |
| 7 | 1876 | 362 | zbynek001 |
| 6 | 897 | 579 | Sean Gilliam |
| 5 | 95 | 986 | cptjazz |
| 5 | 4837 | 51 | Valdis Zobēla |
| 5 | 407 | 95 | Jonathan Nagy |
| 5 | 116 | 14 | Andre Loker |
| 3 | 992 | 77 | Gregorius Soedharmo |
| 3 | 2 | 2 | Arjen Smits |
| 2 | 7 | 7 | jdsartori |
| 2 | 4 | 6 | Onur Gumus |
| 2 | 15 | 1 | Kaiwei Li |
| 1 | 65 | 47 | Ondrej Pialek |
| 1 | 62 | 15 | Mathias Feitzinger |
| 1 | 3 | 3 | Abi |
| 1 | 3 | 1 | jg11jg |
| 1 | 18 | 16 | Peter Huang |
| 1 | 1 | 2 | Maciej Wódke |
| 1 | 1 | 1 | Wessel Kranenborg |
| 1 | 1 | 1 | Greatsamps |
| 1 | 1 | 1 | Christoffer Jedbäck |
| 1 | 1 | 1 | Andre |

#### 1.4.1-rc3 March 10 2020 ####
**Stable release candidate for Akka.NET 1.4**

In Akka.NET v1.4.1-rc2 we rolled back all of the stand-alone HOCON additions and made Akka.NET v1.4 non-breaking. 

* [Akka.Persistence.Sql.Common: Fix, unsealed `JournalSettings` and `SnapshotStoreSettings` classes](https://github.com/akkadotnet/akka.net/pull/4319)

#### 1.4.1-rc3 March 10 2020 ####
**Stable release candidate for Akka.NET 1.4**

In Akka.NET v1.4.1-rc2 we rolled back all of the stand-alone HOCON additions and made Akka.NET v1.4 non-breaking. 

For information on the full set of changes between RC1 and RC2, [please see "What's New in Akka.NET v1.4" on the official Akka.NET website](https://getakka.net/community/whats-new/akkadotnet-v1.4.html) 

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 13 | 9431 | 5347 | Aaron Stannard |
| 2 | 3 | 3 | dependabot-preview[bot] |
| 1 | 97 | 73 | Gregorius Soedharmo |
| 1 | 6 | 1 | Igor Fedchenko |

#### 1.4.1-rc1 February 28 2020 ####
**Stable release candidate for Akka.NET 1.4**

Akka.NET v1.4.0-rc1 represents [the completion of the entire Akka.NET v1.4.0 milestone](https://github.com/akkadotnet/akka.net/milestone/17).

Provided that there are no major bugs, architectural issues, performance regressions, or stability issues reported as we begin the process of moving all of our plugins onto Akka.NET v1.4.0-RC1, Akka.NET v1.4.0 final will ship within two weeks.

For information on the full set of changes, [please see "What's New in Akka.NET v1.4" on the official Akka.NET website](https://getakka.net/community/whats-new/akkadotnet-v1.4.html).

| COMMITS | LOC+ | LOC- | AUTHOR |         
| --- | --- | --- | --- |                  
| 206 | 33015 | 24489 | Aaron Stannard |   
| 47 | 63 | 63 | dependabot-preview[bot] | 
| 33 | 16794 | 6803 | Igor Fedchenko |     
| 15 | 1370 | 557 | Ismael Hamed |         
| 13 | 11671 | 1059 | Bartosz Sypytkowski |
| 10 | 3451 | 706 | zbynek001 |            
| 8 | 224 | 25 | Andre Loker |             
| 6 | 897 | 579 | Sean Gilliam |           
| 6 | 101 | 987 | cptjazz |                
| 5 | 4837 | 51 | Valdis Zobēla |          
| 5 | 407 | 95 | Jonathan Nagy |           
| 3 | 8 | 8 | jdsartori |                  
| 3 | 2 | 2 | Arjen Smits |                
| 3 | 16 | 2 | Kaiwei Li |                 
| 2 | 6 | 6 | Abi |                        
| 2 | 4 | 6 | Onur Gumus |                 
| 2 | 36 | 32 | Peter Huang |              
| 2 | 2 | 4 | Maciej Wódke |               
| 2 | 2 | 2 | Wessel Kranenborg |          
| 2 | 130 | 94 | Ondrej Pialek |           
| 1 | 633 | 1 | Gregorius Soedharmo |      
| 1 | 62 | 15 | Mathias Feitzinger |       
| 1 | 3 | 1 | jg11jg |                     
| 1 | 1 | 1 | Greatsamps |                 
| 1 | 1 | 1 | Christoffer Jedbäck |        
| 1 | 1 | 1 | Andre |                      

#### 1.4.0-beta4 January 28 2020 ####
**Fourth pre-release candidate for Akka.NET 1.4**

Akka.NET v1.4.0-beta4 represents a significant advancement against the v1.4.0 milestone, with numerous changes and fixes. 

**Akka.NET now targets .NET Standard 2.0 going forward** - this first big change in this release is that we've dropped support for .NET Framework 4.5. We will only target .NET Standard 2.0 going forward with the v1.4.0 milestone from this point onward. .NET Standard 2.0 can be consumed by .NET Framework 4.6.1+ and .NET Core 2.0 and higher.

**Introduction to Akka.Cluster.Metrics** - in this release we introduce a brand new Akka.NET NuGet package, Akka.Cluster.Metrics, which is designed to allow users to share data about the relative busyness of each node in their cluster. Akka.Cluster.Metrics can be consumed inside routers, i.e. "route this message to the node with the most available memory," and Akka.Cluster.Metrics also supports the publication of custom metrics types.

If you want to [learn more about how to use Akka.Cluster.Metrics, read the official documentation here](https://getakka.net/articles/clustering/cluster-metrics.html).

**Significant Akka.Remote Performance Improvements** - as part of this release we've introduced some new changes that are enabled by default in the Akka.Remote DotNetty transport: "flush batching" or otherwise known as I/O batching. The idea behind this is to group multiple logical writes into a smaller number of system writes. 

You will want to tune this setting to match the behavior of your specific application, and you can read our [brand new "Akka.Remote Performance Optimization" page](https://getakka.net/articles/remoting/performance.html).

To [follow our progress on the Akka.NET v1.4 milestone, click here](https://github.com/akkadotnet/akka.net/milestone/17).

We expect to release more beta versions in the future, and if you wish to [get access to nightly Akka.NET builds then click here](https://getakka.net/community/getting-access-to-nightly-builds.html).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 27 | 15375 | 5575 | Igor Fedchenko |
| 26 | 2131 | 2468 | Aaron Stannard |
| 25 | 34 | 34 | dependabot-preview[bot] |
| 8 | 765 | 203 | Ismael Hamed |
| 4 | 75 | 70 | Jonathan Nagy |
| 3 | 108 | 11 | Andre Loker |
| 2 | 380 | 43 | Valdis Zobēla |
| 1 | 62 | 15 | Mathias Feitzinger |
| 1 | 6 | 1 | cptjazz |
| 1 | 14 | 0 | Kaiwei Li |
| 1 | 1 | 1 | zbynek001 |
| 1 | 1 | 1 | Christoffer Jedbäck |

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

#### 1.4.0-beta3 October 30 2019 ####
**Third pre-release candidate for Akka.NET 1.4**
This release contains some more significant changes for Akka.NET v1.4.0.

* All APIs in Akka.Streams, Akka, and elsewhere which used to return `Tuple<T1, T2>` now support the equivalent `ValueTuple<T1,T2>` syntax - so new language features such as tuple deconstruction can be used by our users.
* Upgraded to Hyperion 0.9.10, which properly supports .NET Core 3.0 and cross-platform communication between .NET Framework and .NET Core.
* All `ILoggingAdapter` instances running inside Akka.NET actors will now include the full Akka.Remote `Address` during logging, which is very helpful for users who aggregate their logs inside consolidated systems.
* Various Akka.Cluster.Sharding fixes.

To [follow our progress on the Akka.NET v1.4 milestone, click here](https://github.com/akkadotnet/akka.net/milestone/17).

We expect to release more beta versions in the future, and if you wish to [get access to nightly Akka.NET builds then click here](https://getakka.net/community/getting-access-to-nightly-builds.html).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |

| 115 | 10739 | 8969 | Aaron Stannard |
| 13 | 11671 | 1059 | Bartosz Sypytkowski |
| 10 | 16 | 16 | dependabot-preview[bot] |
| 6 | 897 | 579 | Sean Gilliam |
| 6 | 1744 | 358 | zbynek001 |
| 5 | 568 | 240 | Ismael Hamed |
| 5 | 116 | 14 | Andre Loker |
| 3 | 4457 | 8 | Valdis Zobēla |
| 3 | 2 | 2 | Arjen Smits |
| 3 | 14 | 9 | cptjazz |
| 2 | 7 | 7 | jdsartori |
| 2 | 4 | 6 | Onur Gumus |
| 2 | 1341 | 1182 | Igor Fedchenko |
| 1 | 65 | 47 | Ondrej Pialek |
| 1 | 3 | 3 | Abi |
| 1 | 3 | 1 | jg11jg |
| 1 | 18 | 16 | Peter Huang |
| 1 | 1 | 2 | Maciej Wódke |
| 1 | 1 | 1 | Wessel Kranenborg |
| 1 | 1 | 1 | Kaiwei Li |
| 1 | 1 | 1 | Greatsamps |
| 1 | 1 | 1 | Andre |

#### 1.4.0-beta2 September 23 2019 ####
**Second pre-release candidate for Akka.NET 1.4**
Akka.NET v1.4.0 is still moving along and this release contains some new and important changes.

* We've added a new package, the Akka.Persistence.TestKit - this is designed to allow users to test their `PersistentActor` implementations under real-world conditions such as database connection failures, serialization errors, and so forth. It works alongside the standard Akka.NET TestKit distributions and offers a simple, in-place API to do so.
* Akka.Streams now supports [Stream Context propagation](https://github.com/akkadotnet/akka.net/pull/3741), designed to make it really easy to work with tools such as Kafka, Amazon SQS, and more - where you might want to have one piece of context (such as the partition offset in Kafka) and propagate it from the very front of an Akka.Stream all the way to the end, and then finally process it once the rest of the stream has completed processing. In the case of Kakfa, this might be updating the local consumer's partition offset only once we've been able to fully guarantee the processing of the message.
* Fixed [a major issue with Akka.Remote, which caused unnecessary `Quarantine` events](https://github.com/akkadotnet/akka.net/issues/3905).

To [follow our progress on the Akka.NET v1.4 milestone, click here](https://github.com/akkadotnet/akka.net/milestone/17).

We expect to release more beta versions in the future, and if you wish to [get access to nightly Akka.NET builds then click here](https://getakka.net/community/getting-access-to-nightly-builds.html).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 97 | 6527 | 3729 | Aaron Stannard |
| 13 | 11671 | 1059 | Bartosz Sypytkowski |
| 4 | 1708 | 347 | zbynek001 |
| 2 | 7 | 7 | jdsartori |
| 2 | 4 | 6 | Onur Gumus |
| 2 | 37 | 114 | Ismael Hamed |
| 1 | 65 | 47 | Ondrej Pialek |
| 1 | 3020 | 2 | Valdis Zobēla |
| 1 | 3 | 3 | Abi |
| 1 | 3 | 1 | jg11jg |
| 1 | 18 | 16 | Peter Huang |
| 1 | 1 | 2 | Maciej Wódke |
| 1 | 1 | 1 | Wessel Kranenborg |
| 1 | 1 | 1 | Kaiwei Li |
| 1 | 1 | 1 | Greatsamps |
| 1 | 1 | 1 | Arjen Smits |
| 1 | 1 | 1 | Andre |

#### 1.3.14 July 29 2019 ####
**Maintenance Release for Akka.NET 1.3**
You know what? We're going to stop promising that _this_ is the last 1.3.x release, because even though we've said that twice... We now have _another_ 1.3.x release. 

1.3.14 consists of non-breaking bugfixes and additions that have been contributed against the [Akka.NET v1.4.0 milestone](https://github.com/akkadotnet/akka.net/milestone/17) thus far. These include:

* Akka.Cluster.Sharding: default "persistent" mode has been stabilized and errors that users have ran into during `ShardCoordinator` recovery, such as [Exception in PersistentShardCoordinator ReceiveRecover](https://github.com/akkadotnet/akka.net/issues/3414);
* [Akka.Remote: no longer disassociates when serialization errors are thrown](https://github.com/akkadotnet/akka.net/pull/3782) in the remoting pipeline - the connection will now stay open;
* [Akka.Cluster.Tools: mission-critical `ClusterClient` and `ClusterClientReceptionist` fixes](https://github.com/akkadotnet/akka.net/pull/3866);
* [SourceLink debugging support for Akka.NET](https://github.com/akkadotnet/akka.net/pull/3848); and
* [Akka.Persistence: Allow AtLeastOnceDelivery parameters to be set from deriving classes](https://github.com/akkadotnet/akka.net/pull/3810); 
* [Akka.Persistence.Sql: BatchingSqlJournal now preserves Sender in PersistCallback](https://github.com/akkadotnet/akka.net/pull/3779); and
* [Akka: bugfix - coordinated shutdown timesout when exit-clr = on](https://github.com/akkadotnet/akka.net/issues/3815).

To [see the full set of changes for Akka.NET v1.3.14, click here](https://github.com/akkadotnet/akka.net/pull/3869).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 22 | 2893 | 828 | Aaron Stannard |
| 3 | 1706 | 347 | zbynek001 |
| 2 | 37 | 114 | Ismael Hamed |
| 1 | 65 | 47 | Ondrej Pialek |
| 1 | 3 | 3 | Abi |
| 1 | 18 | 16 | Peter Huang |
| 1 | 1 | 2 | Maciej Wódke |
| 1 | 1 | 1 | Wessel Kranenborg |
| 1 | 1 | 1 | Kaiwei Li |
| 1 | 1 | 1 | jdsartori |

#### 1.4.0-beta1 July 17 2019 ####
**First pre-release candidate for Akka.NET 1.4**
Akka.NET v1.4 has a ways to go before it's fully ready for market, but this is the first publicly available release on NuGet and it contains some massive changes.

* Akka.Cluster.Sharding's default "persistent" mode has been stabilized and errors that users have ran into during `ShardCoordinator` recovery, such as [Exception in PersistentShardCoordinator ReceiveRecover](https://github.com/akkadotnet/akka.net/issues/3414)
* StreamRefs have been added to Akka.Streams, which allows streams to run across process boundaries and over Akka.Remote / Akka.Cluster.
* Akka.NET now targets .NET Standard 2.0, which means many annoying polyfills and other hacks we needed to add in order to support .NET Core 1.1 are now gone and replaced with standard APIs.
* Lots of small bug fixes and API changes have been added to Akka core and other libraries.

To [follow our progress on the Akka.NET v1.4 milestone, click here](https://github.com/akkadotnet/akka.net/milestone/17).

We expect to release more beta versions in the future, and if you wish to [get access to nightly Akka.NET builds then click here](https://getakka.net/community/getting-access-to-nightly-builds.html).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 39 | 3479 | 1731 | Aaron Stannard |
| 6 | 5255 | 531 | Bartosz Sypytkowski |
| 4 | 1708 | 347 | zbynek001 |
| 2 | 37 | 114 | Ismael Hamed |
| 1 | 3 | 3 | Abi |
| 1 | 2 | 3 | Onur Gumus |
| 1 | 18 | 16 | Peter Huang |
| 1 | 1 | 2 | Maciej Wódke |
| 1 | 1 | 1 | jdsartori |
| 1 | 1 | 1 | Wessel Kranenborg |

#### 1.3.13 April 30 2019 ####
**Maintenance Release for Akka.NET 1.3**
Well, we thought 1.3.12 would be the final release for Akka.NET v1.3.* - but then we found some nasty bugs prior to merging any of the v1.4 features into our development branch. But finally, for real this time - this is really the last v1.3.13 release.

This patch contains some critical bug fixes and improvements to Akka.NET:
* [Akka.Persistence: OnPersistRejected now logs an error with the complete stacktrace](https://github.com/akkadotnet/akka.net/pull/3763)
* [Akka.Persistence.Sql: Ensure that BatchingSqlJournal will propagate sql connection opening failure](https://github.com/akkadotnet/akka.net/pull/3754)
* [Akka: Add UnboundedStablePriorityMailbox](https://github.com/akkadotnet/akka.net/issues/2652)
* [Akka.Remote and Akka.Cluster: Port exhaustion problem with Akka.Cluster](https://github.com/akkadotnet/akka.net/issues/2575)

To [see the full set of changes for Akka.NET v1.3.13, click here](https://github.com/akkadotnet/akka.net/milestone/31).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 2 | 5 | 6 | Ismael Hamed |
| 2 | 45 | 7 | Shukhrat Nekbaev |
| 2 | 23 | 30 | Aaron Stannard |
| 1 | 87 | 7 | Bartosz Sypytkowski |
| 1 | 492 | 9 | AndreSteenbergen |
| 1 | 2 | 2 | ThomasWetzel |
| 1 | 12 | 12 | Sean Gilliam |

#### 1.3.12 March 13 2019 ####
**Maintenance Release for Akka.NET 1.3**
This will be the final bugfix release for Akka.NET v1.3.* - going forward we will be working on releasing Akka.NET v1.4.

This patch contains many important bug fixes and improvements:
* [Akka.Cluster.Sharding: Automatic passivation for sharding](https://github.com/akkadotnet/akka.net/pull/3718)
* [Akka.Persistence: Optimize AtLeastOnceDelivery by not scheduling ticks when not needed](https://github.com/akkadotnet/akka.net/pull/3704)
* [Akka.Persistence.Sql.Common: Bugfix CurrentEventsByTag does not return more than a 100 events](https://github.com/akkadotnet/akka.net/issues/3697)
* [Akka.Persistence.Sql.Common: DeleteMessages when journal is empty causes duplicate key SQL exception](https://github.com/akkadotnet/akka.net/issues/3721)
* [Akka.Cluster: SplitBrainResolver: don't down oldest if alone in cluster](https://github.com/akkadotnet/akka.net/pull/3690)
* [Akka: Include manifest or class in missing serializer failure if possible](https://github.com/akkadotnet/akka.net/pull/3701)
* [Akka: Memory leak when disposing actor system with non default ActorRefProvider](https://github.com/akkadotnet/akka.net/issues/2640)

To [see the full set of changes for Akka.NET v1.3.12, click here](https://github.com/akkadotnet/akka.net/milestone/30).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 8 | 1487 | 357 | Ismael Hamed |
| 7 | 126 | 120 | jdsartori |
| 3 | 198 | 4 | JSartori |
| 3 | 155 | 22 | Aaron Stannard |
| 2 | 11 | 2 | Peter Lin |
| 1 | 8 | 4 | Raimund Hirz |
| 1 | 7 | 0 | Warren Falk |
| 1 | 45 | 6 | Peter Huang |
| 1 | 14 | 13 | Bartosz Sypytkowski |
| 1 | 11 | 1 | Greg Shackles |
| 1 | 10 | 10 | Jill D. Headen |
| 1 | 1 | 1 | Isaac Z |

#### 1.3.11 December 17 2018 ####
**Maintenance Release for Akka.NET 1.3**

Akka.NET v1.3.11 is a bugfix patch primarily aimed at solving the following issue: [DotNetty Remote Transport Issues with .NET Core 2.1](https://github.com/akkadotnet/akka.net/issues/3506).

.NET Core 2.1 exposed some issues with the DotNetty connection methods in DotNetty v0.4.8 that have since been fixed in subsequent releases. In Akka.NET v1.3.11 we've resolved this issue by upgrading to DotNetty v0.6.0.

In addition to the above, we've introduced some additional fixes and changes in Akka.NET v1.3.11:

* [Akka.FSharp: Akka.Fsharp spawning an actor results in Exception](https://github.com/akkadotnet/akka.net/issues/3402)
* [Akka.Remote: tcp-reuse-addr = off-for-windows prevents actorsystem from starting](https://github.com/akkadotnet/akka.net/issues/3293)
* [Akka.Remote: tcp socket address reuse - default configuration](https://github.com/akkadotnet/akka.net/issues/2477)
* [Akka.Cluster.Tools: 
Actor still receiving messages from mediator after termination](https://github.com/akkadotnet/akka.net/issues/3658)
* [Akka.Persistence: Provide minSequenceNr for snapshot deletion](https://github.com/akkadotnet/akka.net/pull/3641)

To [see the full set of changes for Akka.NET 1.3.11, click here](https://github.com/akkadotnet/akka.net/milestone/29)

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 5 | 123 | 71 | Aaron Stannard |
| 3 | 96 | 10 | Ismael Hamed |
| 2 | 4 | 3 | Oleksandr Kobylianskyi |
| 1 | 5 | 1 | Ruben Mamo |
| 1 | 23 | 6 | Chris Hoare |

#### 1.3.10 November 1 2018 ####
**Maintenance Release for Akka.NET 1.3**

Akka.NET v1.3.10 consists mostly of bug fixes and patches to various parts of Akka.NET:

* [Akka.Remote: add support for using installed certificates with thumbprints](https://github.com/akkadotnet/akka.net/issues/3632)
* [Akka.IO: fix TCP sockets leak](https://github.com/akkadotnet/akka.net/issues/3630)
* [Akka.DI.Core: Check if Dependency Resolver is configured to avoid a `NullReferenceException`](https://github.com/akkadotnet/akka.net/pull/3619)
* [Akka.Streams: Interop between Akka.Streams and IObservable](https://github.com/akkadotnet/akka.net/pull/3112)
* [HOCON: Parse size in bytes format. Parse microseconds and nanoseconds.](https://github.com/akkadotnet/akka.net/pull/3600)
* [Akka.Cluster: Don't automatically down quarantined nodes](https://github.com/akkadotnet/akka.net/pull/3605)

To [see the full set of changes for Akka.NET 1.3.10, click here](https://github.com/akkadotnet/akka.net/milestone/28).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 8 | 887 | 220 | Bartosz Sypytkowski |
| 5 | 67 | 174 | Aaron Stannard |
| 4 | 15 | 7 | Caio Proiete |
| 3 | 7 | 4 | Maciek Misztal |
| 2 | 60 | 8 | Marcus Weaver |
| 2 | 57 | 12 | moerwald |
| 2 | 278 | 16 | Peter Shrosbree |
| 2 | 2 | 2 | Fábio Beirão |
| 1 | 71 | 71 | Sean Gilliam |
| 1 | 6 | 0 | basbossinkdivverence |
| 1 | 24 | 5 | Ismael Hamed |
| 1 | 193 | 8 | to11mtm |
| 1 | 17 | 33 | zbynek001 |
| 1 | 12 | 3 | Oleksandr Bogomaz |
| 1 | 1 | 1 | MelnikovIG |
| 1 | 1 | 1 | Alex Villarreal |
| 1 | 1 | 0 | Yongjie Ma |

#### 1.3.9 August 22 2018 ####
**Maintenance Release for Akka.NET 1.3**

Akka.NET v1.3.9 features some major changes to Akka.Cluster.Sharding, additional Akka.Streams stages, and some general bug fixes across the board.

**Akka.Cluster.Sharding Improvements**
The [Akka.Cluster.Sharding documentation](http://getakka.net/articles/clustering/cluster-sharding.html#quickstart) already describes some of the major changes in Akka.NET v1.3.9, but we figured it would be worth calling special attention to those changes here.

**Props Factory for Entity Actors**

> In some cases, the actor may need to know the `entityId` associated with it. This can be achieved using the `entityPropsFactory` parameter to `ClusterSharding.Start` or `ClusterSharding.StartAsync`. The entity ID will be passed to the factory as a parameter, which can then be used in the creation of the actor.

In addition to the existing APIs we've always had for defining sharded entities via `Props`, Akka.NET v1.3.9 introduces [a new method overload for `Start`](http://getakka.net/api/Akka.Cluster.Sharding.ClusterSharding.html#Akka_Cluster_Sharding_ClusterSharding_Start_System_String_System_Func_System_String_Akka_Actor_Props__Akka_Cluster_Sharding_ClusterShardingSettings_Akka_Cluster_Sharding_ExtractEntityId_Akka_Cluster_Sharding_ExtractShardId_) and [`StartAsync`](http://getakka.net/api/Akka.Cluster.Sharding.ClusterSharding.html#Akka_Cluster_Sharding_ClusterSharding_StartAsync_System_String_System_Func_System_String_Akka_Actor_Props__Akka_Cluster_Sharding_ClusterShardingSettings_Akka_Cluster_Sharding_ExtractEntityId_Akka_Cluster_Sharding_ExtractShardId_) which allows users to pass in the `entityId` of each entity actor as a constructor argument to those entities when they start.

For example:

```
var anotherCounterShard = ClusterSharding.Get(Sys).Start(
	                        typeName: "AnotherCounter",
	                        entityProps: Props.Create<AnotherCounter>(),
	                        typeName: AnotherCounter.ShardingTypeName,
	                        entityPropsFactory: entityId => AnotherCounter.Props(entityId),
	                        settings: ClusterShardingSettings.Create(Sys),
	                        extractEntityId: Counter.ExtractEntityId,
	                        extractShardId: Counter.ExtractShardId);
```

This will give you the opportunity to pass in the `entityId` for each actor as a constructor argument into the `Props` of your entity actor and possibly other use cases too. 

**Improvements to Starting and Querying Existing Shard Entity Types**
Two additional major usability improvements to Cluster.Sharding come from some API additions and changes.

The first is that it's now possible to look up all of the currently registered shard types via the [`ClusterSharding.ShardTypeNames` property](http://getakka.net/api/Akka.Cluster.Sharding.ClusterSharding.html#Akka_Cluster_Sharding_ClusterSharding_ShardTypeNames). So long as a `ShardRegion` of that type has been started in the cluster, that entity type name will be added to the collection exposed by this property.

The other major usability improvement is a change to the `ClusterSharding.Start` property itself. Historically, you used to have to know whether or not the node you wanted to use sharding on was going to be hosting shards (call `ClusterSharding.Start`) or simply communicated with shards hosted on a different cluster role type (call `ClusterSharding.StartProxy`). Going forward, it's safe to call `ClusterSharding.Start` on any node and you will either receive an `IActorRef` to active `ShardRegion` or a `ShardRegion` running in "proxy only" mode; this is determined by looking at the `ClusterShardingSettings` and determining if the current node is in a role that is allowed to host shards of this type.

* [Akka.Cluster.Sharding: Sharding API Updates](https://github.com/akkadotnet/akka.net/pull/3524)
* [Akka.Cluster.Sharding: sharding rebalance fix](https://github.com/akkadotnet/akka.net/pull/3518)
* [Akka.Cluster.Sharding: log formatting fix](https://github.com/akkadotnet/akka.net/pull/3554)
* [Akka.Cluster.Sharding: `RestartShard` escapes into userspace](https://github.com/akkadotnet/akka.net/pull/3509)

**Akka.Streams Additions and Changes**
In Akka.NET v1.3.9 we've added some new built-in stream stages and API methods designed to help improve developer productivity and ease of use.

* [Akka.Streams: add CombineMaterialized method to Source](https://github.com/akkadotnet/akka.net/pull/3489)
* [Akka.Streams: 
KillSwitches: flow stage from CancellationToken](https://github.com/akkadotnet/akka.net/pull/3568)
* [Akka.Streams: Port KeepAliveConcat and UnfoldFlow](https://github.com/akkadotnet/akka.net/pull/3560)
* [Akka.Streams: Port PagedSource & IntervalBasedRateLimiter](https://github.com/akkadotnet/akka.net/pull/3570)

**Other Updates, Additions, and Bugfixes**
* [Akka.Cluster: cluster coordinated leave fix for empty cluster](https://github.com/akkadotnet/akka.net/pull/3516)
* [Akka.Cluster.Tools: bumped ClusterClient message drop log messages from DEBUG to WARNING](https://github.com/akkadotnet/akka.net/pull/3513)
* [Akka.Cluster.Tools: Singleton - confirm TakeOverFromMe when singleton already in oldest state](https://github.com/akkadotnet/akka.net/pull/3553)
* [Akka.Remote: RemoteWatcher race-condition fix](https://github.com/akkadotnet/akka.net/pull/3519)
* [Akka: fix concurrency bug in CircuitBreaker](https://github.com/akkadotnet/akka.net/pull/3505)
* [Akka: Fixed ReceiveTimeout not triggered in some case when combined with NotInfluenceReceiveTimeout messages](https://github.com/akkadotnet/akka.net/pull/3555)
* [Akka.Persistence: Optimized recovery](https://github.com/akkadotnet/akka.net/pull/3549)
* [Akka.Persistence: Allow persisting events when recovery has completed](https://github.com/akkadotnet/akka.net/pull/3366)

To [see the full set of changes for Akka.NET v1.3.9, click here](https://github.com/akkadotnet/akka.net/milestone/27).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 28 | 2448 | 5691 | Aaron Stannard |
| 11 | 1373 | 230 | zbynek001 |
| 8 | 4590 | 577 | Bartosz Sypytkowski |
| 4 | 438 | 99 | Ismael Hamed |
| 4 | 230 | 240 | Sean Gilliam |
| 2 | 1438 | 0 | Oleksandr Bogomaz |
| 1 | 86 | 79 | Nick Polideropoulos |
| 1 | 78 | 0 | v1rusw0rm |
| 1 | 4 | 4 | Joshua Garnett |
| 1 | 32 | 17 | Jarl Sveinung Flø Rasmussen |
| 1 | 27 | 1 | Sam13 |
| 1 | 250 | 220 | Maxim Cherednik |
| 1 | 184 | 124 | Josh Taylor |
| 1 | 14 | 0 | Peter Shrosbree |
| 1 | 1278 | 42 | Marc Piechura |
| 1 | 1 | 1 | Vasily Kirichenko |
| 1 | 1 | 1 | Samuel Kelemen |
| 1 | 1 | 1 | Nyola Mike |
| 1 | 1 | 1 | Fábio Beirão |

#### 1.3.8 June 04 2018 ####
**Maintenance Release for Akka.NET 1.3**

Akka.NET v1.3.8 is a minor patch consisting mostly of bug fixes as well as an upgrade to DotNetty v0.4.8.

**DotNetty v0.4.8 Upgrade**
You can [read the release notes for DotNetty v0.4.8 here](https://github.com/Azure/DotNetty/blob/5eee925b7597c6b07689f25f328966e330ff58f9/RELEASE_NOTES.md) - but here are the major improvements as they pertain to Akka.NET:

1. DotNetty length-frame decoding is now fully-supported on .NET Core on Linux and
2. Socket shutdown code has been improved, which fixes a potential "port exhaustion" issue reported by Akka.Remote users.

If you've been affected by either of these issues, we strongly encourage that you upgrade your applications to Akka.NET v1.3.8 as soon as possible.

**Updates and Additions**
1. [Akka.Streams: add PreMaterialize support for Sources](https://github.com/akkadotnet/akka.net/pull/3476)
2. [Akka.Streams: add PreMaterialize support for Sinks](https://github.com/akkadotnet/akka.net/pull/3477)
3. [Akka.Streams: 
Port Pulse, DelayFlow and Valve streams-contrib stages](https://github.com/akkadotnet/akka.net/pull/3421)
4. [Akka.FSharp: Unit test Akka.FSharp.System.create with extensions](https://github.com/akkadotnet/akka.net/pull/3407)

Relevant documentation for Akka.Streams pre-materialization, for those who are interested: http://getakka.net/articles/streams/basics.html#source-pre-materialization

**Bugfixes**
1. [Akka.Remote: ActorSelection fails for ActorPath from remotely deployed actors](https://github.com/akkadotnet/akka.net/issues/1544)
2. [Akka.Remote: WilcardCard ActorSelections that fail to match any actors don't deliver messages into DeadLetters](https://github.com/akkadotnet/akka.net/issues/3420)
3. [Akka.Cluster: SplitBrainResolver logs "network partition detected" after change in cluster membership, even when no unreachable nodes](https://github.com/akkadotnet/akka.net/issues/3450)
4. [Akka: SynchronizationLockException in user-defined mailboxes](https://github.com/akkadotnet/akka.net/issues/3459)
5. [Akka: UnhandledMessageForwarder crashes and restarted every time the app is starting](https://github.com/akkadotnet/akka.net/issues/3267)

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 17 | 498 | 171 | Aaron Stannard |
| 4 | 1054 | 23 | Bartosz Sypytkowski |
| 2 | 2 | 2 | Fábio Beirão |
| 2 | 16 | 2 | Aaron Palmer |
| 1 | 1063 | 4 | Oleksandr Bogomaz |
| 1 | 1 | 1 | Ismael Hamed |
| 1 | 1 | 1 | Gauthier Segay |

#### 1.3.7 May 15 2018 ####
**Maintenance Release for Akka.NET 1.3**

Akka.NET v1.3.7 is a minor patch consisting mostly of bug fixes.

**DotNetty stabilization**
We've had a number of issues related to DotNetty issues over recent weeks, and we've resolved those in this patch by doing the following:

* [Locking down the version of DotNetty to v0.4.6 until further notice](https://github.com/akkadotnet/akka.net/pull/3410)
* [Resolving memory leaks introduced with DotNetty in v1.3.6](https://github.com/akkadotnet/akka.net/pull/3436)

We will be upgrading to DotNetty v0.4.8 in a near future release, but in the meantime this patch fixes critical issues introduced in v1.3.6.

**Bugfixes**
1. [Akka.Persistence.Sql: Slow reading of big snapshots](https://github.com/akkadotnet/akka.net/issues/3422) - this will require a recompilation of all Akka.Persistence.Sql-type Akka.Persistence plugins.
2. [Akka.Fsharp: spawning an actor results in Exception in 1.3.6 release](https://github.com/akkadotnet/akka.net/issues/3402)

See [the full list of fixes for Akka.NET v1.3.7 here](https://github.com/akkadotnet/akka.net/milestone/25).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 5 | 130 | 180 | Aaron Stannard |
| 3 | 7 | 1 | chrisjhoare |
| 2 | 3 | 1 | ivog |
| 1 | 70 | 17 | TietoOliverKurowski |
| 1 | 41 | 4 | Bart de Boer |
| 1 | 11 | 3 | Oleksandr Bogomaz |
| 1 | 1 | 1 | Vasily Kirichenko |

#### 1.3.6 April 17 2018 ####
**Maintenance Release for Akka.NET 1.3**

Akka.NET v1.3.6 is a minor patch consisting mostly of bug fixes.

**Akka.FSharp on .NET Standard**
The biggest change in this release is [the availability of Akka.FSharp on .NET Standard and .NET Core](https://github.com/akkadotnet/akka.net/issues/2826)!

Akka.FSharp runs on .NET Standard 2.0 as of 1.3.6 (it doesn't support .NET Standard 1.6 like the rest of Akka.NET due to FSharp-specific, downstream dependencies.)

**Updates and Additions**
1. [Akka.Streams: Port 4 "streams contrib" stages - AccumulateWhileUnchanged, LastElement, PartitionWith, Sample](https://github.com/akkadotnet/akka.net/pull/3375)
2. [Akka.Remote: Add `public-port` setting to allow for port aliasing inside environments like Docker, PCF](https://github.com/akkadotnet/akka.net/issues/3357)

**Bugfixes**
1. [Akka.Cluster.Sharding: Removing string.GetHashCode usage from distributed classes](https://github.com/akkadotnet/akka.net/pull/3363)
2. [Akka.Cluster.Sharding: HashCodeMessageExtractor can create inconsistent ShardId's](https://github.com/akkadotnet/akka.net/issues/3361)
3. [Akka.Remote: 
Error while decoding incoming Akka PDU Exception when communicating between Remote Actors with a large number of messages on Linux](https://github.com/akkadotnet/akka.net/issues/3370)
4. [Akka.Cluster.Sharding: DData: Cannot create a shard proxy on a cluster node that is not in the same role as the proxied shard entity](https://github.com/akkadotnet/akka.net/issues/3352)
5. [Akka.Streams: Fix GroupedWithin allocation of new buffer after emit.](https://github.com/akkadotnet/akka.net/pull/3382)
6. [Akka.Persistence: Add missing ReturnRecoveryPermit](https://github.com/akkadotnet/akka.net/pull/3372)

You can see [the full set of changes for Akka.NET v1.3.6 here](hhttps://github.com/akkadotnet/akka.net/milestone/24).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 7 | 261 | 38 | Aaron Stannard |
| 6 | 28 | 28 | cimryan |
| 5 | 53 | 20 | Tomasz Jaskula |
| 2 | 7 | 4 | Ondrej Pialek |
| 2 | 20 | 10 | Ismael Hamed |
| 1 | 739 | 0 | Oleksandr Bogomaz |
| 1 | 64 | 6 | Robert |
| 1 | 23 | 29 | nathvi |
| 1 | 2 | 1 | Sebastien Bacquet |
| 1 | 1 | 2 | OndÅ?ej PiÃ¡lek |
| 1 | 1 | 1 | Steffen Skov |
| 1 | 1 | 1 | Sean Gilliam |
| 1 | 1 | 1 | Matthew Herman |
| 1 | 1 | 1 | Jan Pluskal |

#### 1.3.5 February 21 2018 ####
**Maintenance Release for Akka.NET 1.3**

Akka.NET v1.3.5 is a minor patch containing only bugfixes.

**Updates and Bugfixes**
1. [Akka.Cluster.Tools: DistributedPubSub Fix premature pruning of topics](https://github.com/akkadotnet/akka.net/pull/3322)

You can see [the full set of changes for Akka.NET v1.3.4 here](https://github.com/akkadotnet/akka.net/milestone/23).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 4 | 4405 | 4284 | Aaron Stannard |
| 1 | 4 | 4 | Joshua Garnett |

#### 1.3.4 February 1 2018 ####
**Maintenance Release for Akka.NET 1.3**

Akka.NET v1.3.4 is a minor patch mostly focused on bugfixes.

**Updates and Bugfixes**
1. [Akka: Ask interface should be clean](https://github.com/akkadotnet/akka.net/pull/3220)
1. [Akka.Cluster.Sharding: DData replicator is always assigned](https://github.com/akkadotnet/akka.net/issues/3297)
2. [Akka.Cluster: Akka.Cluster Group Routers don't respect role setting when running with allow-local-routees](https://github.com/akkadotnet/akka.net/issues/3294)
3. [Akka.Streams: Implement PartitionHub](https://github.com/akkadotnet/akka.net/pull/3287)
4. [Akka.Remote AkkaPduCodec performance fixes](https://github.com/akkadotnet/akka.net/pull/3299)
5. [Akka.Serialization.Hyperion updated](https://github.com/akkadotnet/akka.net/pull/3306) to [Hyperion v0.9.8](https://github.com/akkadotnet/Hyperion/releases/tag/v0.9.8)

You can see [the full set of changes for Akka.NET v1.3.4 here](https://github.com/akkadotnet/akka.net/milestone/22).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 6 | 304 | 209 | Aaron Stannard |
| 1 | 250 | 220 | Maxim Cherednik |
| 1 | 1278 | 42 | Marc Piechura |
| 1 | 1 | 1 | zbynek001 |
| 1 | 1 | 1 | Vasily Kirichenko |

#### 1.3.3 January 19 2018 ####
**Maintenance Release for Akka.NET 1.3**

The largest changes featured in Akka.NET v1.3.3 are the introduction of [Splint brain resolvers](http://getakka.net/articles/clustering/split-brain-resolver.html) and `WeaklyUp` members in Akka.Cluster.

**Akka.Cluster Split Brain Resolvers**
Split brain resolvers are specialized [`IDowningProvider`](http://getakka.net/api/Akka.Cluster.IDowningProvider.html) implementations that give Akka.Cluster users the ability to automatically down `Unreachable` cluster nodes in accordance with well-defined partition resolution strategies, namely:

* Static quorums;
* Keep majority;
* Keep oldest; and 
* Keep-referee.

You can learn more about why you may want to use these and which strategy is right for you by reading our [Splint brain resolver documentation](http://getakka.net/articles/clustering/split-brain-resolver.html).

**Akka.Cluster `WeaklyUp` Members**
One common problem that occurs in Akka.Cluster is that once a current member of the cluster becomes `Unreachable`, the leader of the cluster isn't able to allow any new members of the cluster to join until that `Unreachable` member becomes `Reachable` again or is removed from the cluster via a [`Cluster.Down` command](http://getakka.net/api/Akka.Cluster.Cluster.html#Akka_Cluster_Cluster_Down_Akka_Actor_Address_).

Beginning in Akka.NET 1.3.3, you can allow nodes to still join and participate in the cluster even while other member nodes are unreachable by opting into the `WeaklyUp` status for members. You can do this by setting the following in your HOCON configuration beginning in Akka.NET v1.3.3:

```
akka.cluster.allow-weakly-up-members = on
```

This will allow nodes who have joined the cluster when at least one other member was unreachable to become functioning cluster members with a status of `WeaklyUp`. If the unreachable members of the cluster are downed or become reachable again, all `WeaklyUp` nodes will be upgraded to the usual `Up` status for available cluster members.

**Akka.Cluster.Sharding and Akka.Cluster.DistributedData Integration**
A new experimental feature we've added in Akka.NET v1.3.3 is the ability to fully decouple [Akka.Cluster.Sharding](http://getakka.net/articles/clustering/cluster-sharding.html) from Akka.Persistence and instead run it on top of [Akka.Cluster.DistributedData, our library for creating eventually consistent replicated data structures on top of Akka.Cluster](http://getakka.net/articles/clustering/distributed-data.html).

Beginning in Akka.NET 1.3.3, you can set the following HOCON configuration option to have the `ShardingCoordinator` replicate its shard placement state using DData instead of persisting it to storage via Akka.Persistence:

```
akka.cluster.sharding.state-store-mode = ddata
```

This setting only affects how Akka.Cluster.Sharding's internal state is managed. If you're using Akka.Persistence with your own entity actors inside Akka.Cluster.Sharding, this change will have no impact on them.

**Updates and bugfixes**:
* [Added `Cluster.JoinAsync` and `Clutser.JoinSeedNodesAsync` methods](https://github.com/akkadotnet/akka.net/pull/3196)
* [Updated Akka.Serialization.Hyperion to Hyperion v0.9.7](https://github.com/akkadotnet/akka.net/pull/3279) - see [Hyperion v0.9.7 release notes here](https://github.com/akkadotnet/Hyperion/releases/tag/v0.9.7).
* [Fixed: A Source.SplitAfter Akka example extra output](https://github.com/akkadotnet/akka.net/issues/3222)
* [Fixed: Udp.Received give incorrect ByteString when client send several packets at once](https://github.com/akkadotnet/akka.net/issues/3210)
* [Fixed: TcpOutgoingConnection does not dispose properly - memory leak](https://github.com/akkadotnet/akka.net/issues/3211)
* [Fixed: Akka.IO & WSAEWOULDBLOCK socket error](https://github.com/akkadotnet/akka.net/issues/3188)
* [Fixed: Sharding-RegionProxyTerminated fix](https://github.com/akkadotnet/akka.net/pull/3192)
* [Fixed: Excessive rebalance in LeastShardAllocationStrategy](https://github.com/akkadotnet/akka.net/pull/3191)
* [Fixed: Persistence - fix double return of recovery permit](https://github.com/akkadotnet/akka.net/pull/3201)
* [Change: Changed Akka.IO configured buffer-size to 512B](https://github.com/akkadotnet/akka.net/pull/3176)
* [Change: Added human-friendly error for failed MNTK discovery](https://github.com/akkadotnet/akka.net/pull/3198)

You can [see the full changeset for Akka.NET 1.3.3 here](https://github.com/akkadotnet/akka.net/milestone/21).

| COMMITS | LOC+ | LOC- | AUTHOR |        
| --- | --- | --- | --- |                 
| 17 | 2094 | 1389 | Marc Piechura |      
| 13 | 5426 | 2827 | Bartosz Sypytkowski |
| 12 | 444 | 815 | Aaron Stannard |       
| 11 | 346 | 217 | ravengerUA |           
| 3 | 90 | 28 | zbynek001 |               
| 3 | 78 | 84 | Maxim Cherednik |         
| 2 | 445 | 1 | Vasily Kirichenko |       
| 2 | 22 | 11 | Ismael Hamed |            
| 2 | 11 | 9 | Nicola Sanitate |          
| 1 | 9 | 10 | mrrd |                     
| 1 | 7 | 2 | Richard Dobson |            
| 1 | 33 | 7 | Ivars Auzins |             
| 1 | 30 | 11 | Will |                    
| 1 | 3 | 3 | HaniOB |                    
| 1 | 11 | 199 | Jon Galloway |           
| 1 | 1 | 1 | Sam Neirinck |              
| 1 | 1 | 1 | Irvin Dominin |             

#### 1.3.2 October 20 2017 ####
**Maintenance Release for Akka.NET 1.3**

**Updates and bugfixes**:
- Bugfix: Akka incorrectly schedules continuations after .Ask, causing deadlocks and/or delays
- Bugfix: ByteString.ToString is sometimes broken for Unicode encoding
- Bugfix: ClusterShardingMessageSerializer Exception after upgrade from 1.2.0 to 1.3.1
- Bugfix: Fix an inconstant ToString on ConsistentRoutee when the node is remote vs. local
- Various documentation fixes
- Akka.Streams: Implement MergePrioritized
- Akka.Streams: Implement Restart Flow/Source/Sink
- Akka.TestKit.Xunit: updated `xunit` dependency to 2.3.0 stable.
- Akka.Cluster.TestKit: removed dependency on Akka.Tests.Shared.Internals

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 9 | 137 | 59 | Aaron Stannard |
| 8 | 2713 | 997 | Alex Valuyskiy |
| 3 | 486 | 95 | Bartosz Sypytkowski |
| 3 | 12 | 12 | Sebastien Bacquet |
| 2 | 33 | 7 | ravengerUA |
| 2 | 184 | 102 | Arjen Smits |
| 1 | 71 | 7 | Adam Friedman |
| 1 | 7 | 4 | Sam Neirinck |
| 1 | 604 | 481 | zbynek001 |
| 1 | 6 | 6 | Kenneth Ito |
| 1 | 42 | 3 | Lukas Rieger |
| 1 | 40 | 2 | Joshua Benjamin |
| 1 | 4 | 5 | derrickcrowne |
| 1 | 3 | 2 | Mikhail Moussikhine |
| 1 | 20 | 0 | Arturo Sevilla |
| 1 | 2 | 0 | Pawel Banka |
| 1 | 17 | 11 | planerist |
| 1 | 1 | 4 | lesscode |



You can [view the full v1.3.2 change set here](https://github.com/akkadotnet/akka.net/milestone/20).

#### 1.3.1 September 5 2017 ####
**Maintenance Release for Akka.NET 1.3**

**Updates and bugfixes**:

- Bugfix: Hyperion NuGet package restore creating duplicate assemblies for the same version inside Akka
- Various documentation fixes and updates
- Bugfix: issue where data sent via UDP when `ByteString` payload had buffers with length more than 1, `UdpSender` only wrote the first part of the buffers and dropped the rest.
- Bugfix: Akka.IO.Tcp failed to write some outgoing messages.
- Improved support for OSX & Rider
- Bugfix: Akka.Persistence support for `SerializerWithStringManifest` required by Akka.Cluster.Sharding and Akka.Cluster.Tools
	- Akka.Persistence.Sqlite and Akka.Persistence.SqlServer were unable to support `SerializerWithStringManifest`, so using Akka.Cluster.Sharding with Sql plugins would not work.
- Bugfix: Akka.Streams generic type parameters of the flow returned from current implementation of Bidiflow's JoinMat method were incorrect.
- Bugfix: `PersistenceMessageSerializer` was failing with the wrong exception when a non-supported type was provided.

**Akka.Persistence backwards compability warning**:

- Akka.Persistence.Sql introduces an additional field to the schema used by Sql-based plugins to allow for the use of `SerializerWithStringManifest` called `serializer_id`.  It requires any previous Sql schema to be updated to have this field.  Details are included in the Akka.Persistence.Sqlite plugin README.md file.  Users of the Akka.Persistence.Sqlite plugin must alter their existing databases to add the field `serializer_id int (4)`:

```
ALTER TABLE {your_event_journal_table_name} ADD COLUMN `serializer_id` INTEGER ( 4 )
ALTER TABLE {your_snapshot_table_name} ADD COLUMN `serializer_id` INTEGER ( 4 )
```

[See the full set of Akka.NET 1.3.1 fixes here](https://github.com/akkadotnet/akka.net/milestone/19).

#### 1.3.0 August 11 2017 ####
**Feature Release for Akka.NET**
Akka.NET 1.3.0 is a major feature release that introduces the significant changes to Akka.NET and its runtime.

**.NET Core and .NET Standard 1.6 Support**
This release introduces support for .NET Standard 1.6 for our core libraries and .NET Core 1.1 for the MultiNode Test Runner standalone executable. All packages for Akka.NET are dual-released under both .NET 4.5 and .NET Standard 1.6.

As a side note: Akka.NET on .NET 4.5 is not wire compatible with Akka.NET on .NET Core; this is due to fundamental changes made to the base types in the CLR on .NET Core. It's a common problem facing many different serializers and networking libraries in .NET at the moment. You can use a X-plat serializer we've developed here: https://github.com/akkadotnet/akka.net/pull/2947 - please comment on that thread if you're considering building hybrid .NET and .NET Core clusters.

**Akka.Persistence Released to Market**
Akka.Persistence has graduated from beta status to stable modules and its interfaces are now considered to be stable. We'll begin updating all of the Akka.Persistence plugins to stable and to add .NET Standard / .NET 4.5 support to each of them as well following this patch.

**DocFx-based Documentation Site**
Documentation is now generated using DocFx and compiled from within the Akka.NET project rather than a separate documentation repository. 

**API Changes**
This release does **not** maintain wire format compatibility with the previous release (v1.2.3) inside Akka.Remote; primarily this is due to having to upgrade from Google Protobuf2 to Protobuf3 in order to add .NET Standard support, but we've also taken the liberty of making other serialization improvements while we're at it. So be advised that during an upgrade from 1.2.* to 1.3.* there will be periods of network disruption between nodes using different versions of the service.

**Akka.Remote Performance Improvements**
Akka.Remote's throughput has been significantly increased.

[See the full set of Akka.NET 1.3.0 fixes here](https://github.com/akkadotnet/akka.net/milestone/14).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 64 | 7109 | 2670 | Marc Piechura |
| 61 | 2420 | 6703 | Nick Chamberlain |
| 46 | 2316 | 10066 | Aaron Stannard |
| 42 | 56428 | 85473 | Alex Valuyskiy |
| 32 | 7924 | 9483 | ravengerUA |
| 31 | 17284 | 13592 | Bartosz Sypytkowski |
| 25 | 2527 | 1124 | Gregorius Soedharmo |
| 21 | 7810 | 1688 | zbynek001 |
| 11 | 1932 | 2167 | Sean Gilliam |
| 9 | 946 | 219 | Arjen Smits |
| 4 | 679 | 105 | alexvaluyskiy |
| 4 | 344 | 6 | Lealand Vettleson |
| 4 | 1644 | 2210 | Arkatufus |
| 3 | 32 | 6 | Lukas Rieger |
| 3 | 153 | 17 | Quartus Dev |
| 2 | 8 | 11 | Pawel Banka |
| 2 | 4866 | 12678 | olegz |
| 2 | 1148 | 176 | Ismael Hamed |
| 1 | 62 | 5 | Mikhail Kantarovskiy |
| 1 | 4 | 2 | tstojecki |
| 1 | 22 | 2 | Maxim Cherednik |
| 1 | 1 | 1 | Sean Killeen |

#### 1.2.3 July 07 2017 ####
**Maintenance Release for Akka.NET 1.2**

Resolves a bug introduced in Akka.NET 1.2.2 that caused Akka.Remote to not terminate properly under some conditions during `ActorSystem.Terminate`.

[See the full set of Akka.NET 1.2.3 fixes here](https://github.com/akkadotnet/akka.net/milestone/18).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 3 | 46 | 63 | Aaron Stannard |

#### 1.2.2 June 28 2017 ####
**Maintenance Release for Akka.NET 1.2**

Finally, fully resolves issues related to Akka.Cluster nodes not being able to cleanly leave or join a cluster after a period of network instability. Also includes some minor fixes for clustered routers and Akka.Persistence.

[See the full set of Akka.NET 1.2.2 fixes here](https://github.com/akkadotnet/akka.net/milestone/16).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 13 | 589 | 52 | Aaron Stannard |

#### 1.2.1 June 22 2017 ####
**Maintenance Release for Akka.NET 1.2**

Resolves issues related to Akka.Cluster nodes not being able to cleanly leave or join a cluster after a period of network instability.

[See the full set of Akka.NET 1.2.1 fixes here](https://github.com/akkadotnet/akka.net/milestone/14).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 15 | 1362 | 753 | alexvaluyskiy |
| 7 | 635 | 1487 | ravengerUA |
| 7 | 1966 | 764 | Nick Chamberlain |
| 4 | 420 | 345 | Aaron Stannard |
| 3 | 18715 | 999 | Alex Valuyskiy |
| 2 | 1943 | 3492 | Sean Gilliam |
| 2 | 104 | 24 | Jaskula Tomasz |
| 1 | 6 | 10 | Szer |
| 1 | 20 | 25 | Lealand Vettleson |

#### 1.2.0 April 11 2017 ####
**Feature Release for Akka.NET**
Akka.NET 1.2 is a major feature release that introduces the following major changes:

**Akka.Remote now uses [DotNetty](https://github.com/azure/dotnetty) for its transport layer**
The biggest change for 1.2 is the removal of Helios 2.0 as the default transport and the introduction of DotNetty. The new DotNetty transport is fully backwards compatible with the Helios 1.4 and 2.* transports, so you should be able to upgrade from any Akka.NET 1.* application to 1.2 without any downtime. All of the `helios.tcp` HOCON is also supported by the DotNetty transport, so none of that needs to updated for the DotNetty transport to work out of the box.

In addition, the DotNetty transport supports TLS, which can be enabled via the following HOCON:

```
akka {
  loglevel = DEBUG
  actor {
    provider = Akka.Remote.RemoteActorRefProvider,Akka.Remote
  }
  remote {
    dot-netty.tcp {
      port = 0
      hostname = 127.0.0.1
      enable-ssl = true
      log-transport = true
      ssl {
        suppress-validation = true
        certificate {
          # valid ssl certificate must be installed on both hosts
          path = "<valid certificate path>" 
          password = "<certificate password>"
          # flags is optional: defaults to "default-flag-set" key storage flag
          # other available storage flags:
          #   exportable | machine-key-set | persist-key-set | user-key-set | user-protected
          flags = [ "default-flag-set" ] 
        }
      }
    }
  }
}
```

You can [read more about Akka.Remote's TLS support here](http://getakka.net/docs/remoting/security#akka-remote-with-tls-transport-layer-security-).

See [the complete DotNetty transport HOCON here](https://github.com/akkadotnet/akka.net/blob/dev/src/core/Akka.Remote/Configuration/Remote.conf#L318).

**Akka.Streams and Akka.Cluster.Tools RTMed**
Akka.Streams and Akka.Cluster.Tools have graduated from beta status to stable modules and their interfaces are now considered to be stable.

**CoordinatedShutdown**
One of the major improvements in Akka.NET 1.2 is the addition of the new `CoordinatedShutdown` plugin, which is designed to make it easier for nodes that are running Akka.Cluster to automatically exit a cluster gracefully whenever `ActorSystem.Terminate` is called or when the process the node is running in attempts to exit. `CoordinatedShutdown` is fully extensible and can be used to schedule custom shutdown operations as part of `ActorSystem` termination.

You can [read more about how to use `CoordinatedShutdown` here](http://getakka.net/docs/working-with-actors/coordinated-shutdown).

**Additional Changes**
In addition to the above changes, there have been a large number of performance improvements, bug fixes, and documentation improvements made to Akka.NET in 1.2. [Read the full list of changes in Akka.NET 1.2 here](https://github.com/akkadotnet/akka.net/milestone/13).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 17 | 4840 | 4460 | Alex Valuyskiy |
| 16 | 4046 | 1144 | Aaron Stannard |
| 12 | 8591 | 2984 | Sean Gilliam |
| 6 | 971 | 1300 | Sergey |
| 5 | 6787 | 2073 | Bartosz Sypytkowski |
| 4 | 6461 | 8403 | Arjen Smits |
| 4 | 333 | 125 | ravengerUA |
| 3 | 71 | 65 | Marc Piechura |
| 3 | 300 | 24 | Nick Chamberlain |
| 2 | 79 | 40 | Maxim Salamatko |
| 2 | 305 | 20 | Ismael Hamed |
| 1 | 136 | 12 | Sergey Kostrukov |
| 1 | 1015 | 45 | Lukas Rieger |
| 1 | 1 | 0 | siudeks |

#### 1.1.3 January 22 2017 ####
**Maintenance release for Akka.NET v1.1**

Akka.NET v1.1.3 features some new libraries and an enormous number of bug fixes.

**Akka.DistributedData Beta**
First, we've introduced an alpha of a new module intended for use with Akka.Cluster: Akka.DistributedData. The goal of this library is to make it possible to concurrently read and write replicated copies of the same entity across different nodes in the cluster using conflict-free replicated data types, often referred to as "CRDTs." These replicas can eventually be merged together in a fully consistent manner and are excellent choices for applications that require a high level of availability and partition tolerance.

The library is still a bit of a work in progress at the moment, but you are free to use it via the following command:

```
PS> Install-Package Akka.DistributedData -pre
```

**Akka.Serialization.Wire Deprecated; Replaced with Akka.Serialization.Hyperion**
Wire recently changed its license to GPLv3, which is a poor fit for a technology like Akka.NET. Therefore, our default serializer beginning in Akka.NET 1.5 will be [Hyperion](https://github.com/akkadotnet/Hyperion) instead. You can see how to set it up here: http://getakka.net/docs/Serialization#how-to-setup-hyperion-as-default-serializer

**Other bug fixes, performance improvements, and changes**
You can [see the full list of changes in Akka.NET 1.1.3 here](https://github.com/akkadotnet/akka.net/milestone/12).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 19 | 878 | 228 | Aaron Stannard |
| 10 | 41654 | 3428 | Sean Gilliam |
| 5 | 11983 | 4543 | Marc Piechura |
| 4 | 37 | 33 | Arjen Smits |
| 4 | 12742 | 300 | Bartosz Sypytkowski |
| 3 | 144 | 74 | Max |
| 2 | 99 | 8 | ZigMeowNyan |
| 2 | 7 | 7 | zbynek001 |
| 2 | 4 | 2 | Andrey Leskov |
| 2 | 225 | 767 | Alex Valuyskiy |
| 2 | 212 | 8 | Gordey Doronin |
| 1 | 8 | 499 | Sean Farrow |
| 1 | 5 | 5 | tomanekt |
| 1 | 4 | 2 | Andrew Young |
| 1 | 3 | 2 | boriskreminskimoldev |
| 1 | 28 | 3 | Roman Eisendle |
| 1 | 24 | 36 | Maxim Salamatko |
| 1 | 2 | 2 | Jeff |
| 1 | 190 | 38 | Sergey |
| 1 | 15 | 9 | voltcode |
| 1 | 12 | 2 | Alexander Pantyukhin |
| 1 | 107 | 0 | Mikhail Kantarovskiy |
| 1 | 101 | 0 | derrickcrowne |

#### 1.1.2 September 21 2016 ####
**Maintenance release for Akka.NET v1.1**

Akka.NET 1.1.2 introduces some exciting developments for Akka.NET users.

**Mono Support and Improved IPV4/6 Configuration**
First, Akka.NET 1.1.2 is the first release of Akka.NET to be production-certified for Mono. We've made some changes to Akka.Remote, in particular, to design it to work within some of the confines of Mono 4.4.2. For instance, we now support the following HOCON configuration value for the default Helios TCP transport:

```
 helios.tcp {
	  # Omitted for brevity
      transport-protocol = tcp

      port = 2552

      hostname = ""

	  public-hostname = ""

	  dns-use-ipv6 = false
	  enforce-ip-family = false
}
```

`helios.tcp.enforce-ip-family` is a new setting added to the `helios.tcp` transport designed to allow Akka.Remote to function in environments that don't support IPV6. This includes Mono 4.4.2, Windows Azure WebApps, and possibly others. When this setting is turned on and `dns-use-ipv6 = false`, all sockets will be forced to use IPV4 only instead of dual mode. If this setting is turned on and `dns-use-ipv6 = true`, all sockets opened by the Helios transport will be forced to use IPV6 instead of dual-mode.

Currently, as of Mono 4.4.2, this setting is turned on by default. Mono 4.6, when it's released, will allow dual-mode to work consistently again in the future.

We run the entire Akka.NET test suite on Mono and all modules pass.

**Akka.Cluster Downing Providers**
We've added a new feature to Akka.Cluster known as a "downing provider" - this is a pluggable strategy that you can configure via HOCON to specify how nodes in your Akka.NET cluster may automatically mark unreachable nodes as down.

Out of the box Akka.Cluster only provides the default "auto-down" strategy that's been included as part of Akka.Cluster in the past. However, you can now subclass the `Akka.Cluster.IDowningProvider` interface to implement your own strategies, which you can then load through HOCON:

```
# i.e.: akka.cluster.downing-provider-class = "Akka.Cluster.Tests.FailingDowningProvider, Akka.Cluster.Tests"
akka.cluster.downing-provider-class = "Fully-qualified-type-name, Assembly"
```

**Other Fixes**
We've also made significant improvements to the Akka.NET scheduler, more than doubling its performance and an significantly decreasing its memory allocation and garbage collection; updated Akka.Streams; fixed bugs in Akka.Cluster routers; and more. You [can read the full list of changes in 1.1.2 here](https://github.com/akkadotnet/akka.net/milestone/11).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 16 | 3913 | 1440 | ravengerUA |
| 9 | 2323 | 467 | Aaron Stannard |
| 9 | 12568 | 2865 | Marc Piechura |
| 4 | 12 | 5 | Michael Kantarovsky |
| 3 | 381 | 196 | Bartosz Sypytkowski |
| 2 | 99 | 0 | rogeralsing |
| 2 | 359 | 17 | Chris Constantin |
| 2 | 29 | 6 | Denys Zhuravel |
| 2 | 11 | 11 | Ismael Hamed |
| 1 | 74 | 25 | mrrd |
| 1 | 5 | 2 | Szymon Kulec |
| 1 | 48 | 65 | alexpantyukhin |
| 1 | 3 | 2 | Tamas Vajk |
| 1 | 2 | 0 | Julien Adam |
| 1 | 121 | 26 | andrey.leskov |
| 1 | 1020 | 458 | Sean Gilliam |
| 1 | 1 | 1 | Maciej Misztal |

#### 1.1.1 July 15 2016 ####
**Maintenance release for Akka.NET v1.1**

Akka.NET 1.1.1 addresses a number of bugs and issues reported by end users who made the upgrade from Akka.NET 1.0.* to Akka.NET 1.1. 

**DNS improvements**
The biggest set of fixes included in Akka.NET 1.1.1 deal with how the Helios transport treats IPV6 addresses and performs DNS resolution. In Akka.NET 1.0.* Helios only supported IPV4 addresses and would use that as the default for DNS. In Akka.NET 1.1 we upgraded to using Helios 2.1, which supports both IPV6 and IPV4, but *changed the default DNS resolution strategy to use IPV6.* This caused some breakages for users who were using the `public-hostname` setting inside Helios in combination with a `hostname` value that used an IPV4 address.

This is now fixed - Akka.NET 1.1.1 uses Helios 2.1.2 which defaults back to an IPV4 DNS resolution strategy for both inbound and outbound connections. We've also fixed the way we encode and format IPV6 addresses into `ActorPath` and `Address` string representations (although we [still have an issue with parsing IPV6 from HOCON](https://github.com/akkadotnet/akka.net/issues/2187).)

If you need to use IPV6 for DNS resolution, you can enable it by changing the following setting:

`akka.remote.helios.tcp.dns-use-ipv6 = true`

You can [see the full list of Akka.NET 1.1.1 changes here](https://github.com/akkadotnet/akka.net/milestone/9).

#### 1.1.0 July 05 2016 ####
**Feature Release for Akka.NET**

In Akka.NET 1.1 we introduce the following major changes:

* Akka.Cluster is no longer in beta; it is released as a fully stable module with a frozen API that is ready for production use.
* Akka.Remote now has a new Helios 2.1 transport that is up to 5x faster than the previous implementation and with tremendously lower memory consumption.
* The actor mailbox system has been replaced with the `MailboxType` system, which standardizes all mailbox implementations on a common core and instead allows for pluggable `IMessageQueue` implementations. This will make it easier to develop user-defined mailboxes and also has the added benefit of reducing all actor memory footprints by 34%.
* The entire router system has been updated, including support for new "controller" actors that can be used to adjust a router's routing table in accordance to external events (i.e. a router that adjusts whom it routes to based on CPU utilization, which will be implemented in Akka.Cluster.Metrics).

[Full list of Akka.NET 1.1 fixes and changes](https://github.com/akkadotnet/akka.net/milestone/6)

**API Changes**
There have been a couple of important API changes which will affect end-users upgrading from Akka.NET versions 1.0.*.

First breaking change deals with the `PriorityMailbox` base class, used by developers who need to prioritize specific message types ahead of others.

All user-defined instances of this type must now include the following constructor in order to work (using an example from Akka.NET itself:)

```csharp
public class IntPriorityMailbox : UnboundedPriorityMailbox
{
    protected override int PriorityGenerator(object message)
    {
        return message as int? ?? Int32.MaxValue;
    }

    public IntPriorityMailbox(Settings settings, Config config) : base(settings, config)
    {
    }
}
```

There must be a `MyMailboxType(Settings settings, Config config)` constructor on all custom mailbox types going forward, or a `ConfigurationException` will be thrown when trying to instantiate an actor who uses the mailbox type.

Second breaking change deals with Akka.Cluster itself. In the past you could access all manner of data from the `ClusterReadView` class (accessed via the `Cluster.ReadView` property) - such as the addresses of all other members, who the current leader was, and so forth.

Going forward `ClusterReadView` is now marked as `internal`, but if you need access to any of this data you can access the `Cluster.State` property, which will return a [`CurrentClusterState`](http://api.getakka.net/docs/stable/html/CFFD0D89.htm) object. This contains most of the same information that was previously available on `ClusterReadView`.

**Akka.Streams**
Another major part of Akka.NET 1.1 is the introduction of [Akka.Streams](http://getakka.net/docs/streams/introduction), a powerful library with a Domain-Specific Language (DSL) that allows you to compose Akka.NET actors and workflows into streams of events and messages. 

As of 1.1 Akka.Streams is now available as a beta module on NuGet.

We highly recommend that you read the [Akka.Streams Quick Start Guide for Akka.NET](http://getakka.net/docs/streams/quickstart) as a place to get started.

**Akka.Persistence.Query**
A second beta module is also now available as part of Akka.NET 1.1, Akka.Persistence.Query - this module is built on top of Akka.Streams and Akka.Persistence and allows users to query ranges of information directly from their underlying Akka.Persistence stores for more powerful types of reads, aggregations, and more.

Akka.Persistence.Query is available for all SQL implementations of Akka.Persistence and will be added to our other Akka.Persistence plugins shortly thereafter.

**Thank you!**
Thanks for all of your patience and support as we worked to deliver this to you - it's been a tremendous amount of work but we really appreciate the help of all of the bug reports, Gitter questions, StackOverflow questions, and testing that our users have done on Akka.NET and specifically, Akka.Cluster over the past two years. We couldn't have done this without you.

23 contributors since release v1.0.8

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 133 | 38124 | 7835 | Silv3rcircl3 |
| 112 | 25826 | 10493 | Chris Constantin |
| 70 | 45449 | 11556 | Bartosz Sypytkowski |
| 44 | 22804 | 13971 | ravengerUA |
| 40 | 9811 | 6396 | Aaron Stannard |
| 12 | 9539 | 6619 | Marc Piechura |
| 6 | 1692 | 959 | Sean Gilliam |
| 4 | 448 | 0 | alexpantyukhin |
| 3 | 772 | 4 | maxim.salamatko |
| 3 | 3 | 382 | Danthar |
| 2 | 40 | 46 | Vagif Abilov |
| 1 | 91 | 103 | rogeralsing |
| 1 | 3 | 3 | Jeff Cyr |
| 1 | 219 | 44 | Michael Kantarovsky |
| 1 | 2 | 1 | Juergen Hoetzel |
| 1 | 19 | 8 | tstojecki |
| 1 | 187 | 2 | Bart de Boer |
| 1 | 178 | 0 | Willem Meints |
| 1 | 17 | 1 | Kamil Wojciechowski |
| 1 | 120 | 7 | JeffCyr |
| 1 | 11 | 7 | corneliutusnea |
| 1 | 1 | 1 | Tamas Vajk |
| 1 | 0 | 64 | annymsMthd |


#### 1.0.8 April 26 2016 ####
**Maintenance release for Akka.NET v1.0.7**

Fixes an issue with the 1.0.7 release where the default settings for Akka.Persistence changed and caused potential breaking changes for Akka.Persistence users. Those changes have been reverted back to the same values as previous versions.

General fixes:
* [Recovered default journal & snapshot store to default config](https://github.com/akkadotnet/akka.net/pull/1864)
* [EndpointRegistry fixes](https://github.com/akkadotnet/akka.net/pull/1862)
* [eliminated allocations with StandardOutWriter](https://github.com/akkadotnet/akka.net/pull/1881)
* [ClusterSingletonManager - settings update](https://github.com/akkadotnet/akka.net/pull/1878)
* [Implement spec for standard mailbox combinations in Akka.NET](https://github.com/akkadotnet/akka.net/pull/1897)

**Commit Stats for v1.0.8**
| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 4 | 240 | 59 | Aaron Stannard |
| 3 | 268 | 1 | Danthar |
| 3 | 189 | 2810 | Silv3rcircl3 |
| 2 | 204 | 4 | Willem Meints |
| 2 | 161 | 108 | Bartosz Sypytkowski |
| 2 | 101 | 24 | Sean Gilliam |
| 1 | 25 | 16 | zbynek001 |

#### 1.0.7 April 4 2016 ####
**Maintenance release for Akka.NET v1.0.6**
The biggest changes in Akka.NET 1.0.7 have been made to Akka.Persistence, which is now designed to match the final stable release version in JVM Akka 2.4. Akka.Persistence is on-target to exit beta and become a fully mature module as of Akka.NET 1.5, due in May/June timeframe.

A quick note about 1.5 - JSON.NET will be replaced by Wire as the default serializer going forward, so if you want to be forward-compatible with 1.5 you will need to switch to using Wire today. [Learn how to switch to using Wire as the default Akka.NET serializer](http://getakka.net/docs/Serialization#how-to-setup-wire-as-default-serializer).

If you install 1.0.7 you may see the following warning appear:

> NewtonSoftJsonSerializer has been detected as a default serializer. 
> It will be obsoleted in Akka.NET starting from version 1.5 in the favor of Wire
for more info visit: http://getakka.net/docs/Serialization#how-to-setup-wire-as-default-serializer
> If you want to suppress this message set HOCON `{configPath}` config flag to on.

This release also fixes some issues with the Cluster.Tools and Cluster.Sharding NuGet packages, which weren't versioned correctly in previous releases.

**Fixes & Changes - Akka.NET Core**
* [https://github.com/akkadotnet/akka.net/pull/1667](https://github.com/akkadotnet/akka.net/pull/1667)
* [Akka IO: ByteIterator and ByteStringbuilder bug fixes](https://github.com/akkadotnet/akka.net/pull/1682)
* [Hocon Tripple quoted text - Fixes #1687](https://github.com/akkadotnet/akka.net/pull/1689)
* [Downgrade System.Collections.Immutable to 1.1.36](https://github.com/akkadotnet/akka.net/issues/1698)
* [Unify immutable collections](https://github.com/akkadotnet/akka.net/issues/1676) - Akka.NET core now depends on System.Collections.Immutable.
* [#1694 Added safe check in InboxActor when receive timeout is already expired](https://github.com/akkadotnet/akka.net/pull/1702)
* [Bugfix: DeadLetter filter with Type parameter should call IsInstanceOfType with the correct argument](https://github.com/akkadotnet/akka.net/pull/1707)
* [Akka.IO bind failed must notify bindCommander of failure](https://github.com/akkadotnet/akka.net/pull/1729)
* [ReceiveActor: Replaced Receive(Func<T, Task> handler) by ReceiveAsync(...) ](https://github.com/akkadotnet/akka.net/pull/1747)
* [External ActorSystem for Testkit event filters. ](https://github.com/akkadotnet/akka.net/pull/1753)
* [Fixed the ScatterGatherFirstCompleted router logic](https://github.com/akkadotnet/akka.net/pull/1769)
* [Issue #1766 - Lazy evaluation of ChildrenContainer.Children and ChildrenContainer.Stats](https://github.com/akkadotnet/akka.net/pull/1772)
* [[Dispatch] Support for 'mailbox-requirement' and 'mailbox-type' in dispatcher config](https://github.com/akkadotnet/akka.net/pull/1773)
* [Fixed within timeout for routers in default configuration](https://github.com/akkadotnet/akka.net/pull/1787)
* [Default MailboxType optimization](https://github.com/akkadotnet/akka.net/pull/1789)
* [Warning about JSON.NET obsolete in v1.5](https://github.com/akkadotnet/akka.net/pull/1811)
* [Issue #1828 Implemented NobodySurrogate](https://github.com/akkadotnet/akka.net/pull/1829)

**Fixes & Changes - Akka.Remote, Akka.Cluster, Et al**
* [Add the default cluster singleton config as a top-level fallback.](https://github.com/akkadotnet/akka.net/pull/1665)
* [Change ShardState to a class](https://github.com/akkadotnet/akka.net/pull/1677)
* [Cluster.Sharding: Take snapshots when configured](https://github.com/akkadotnet/akka.net/pull/1678)
* [added remote metrics](https://github.com/akkadotnet/akka.net/pull/1722)
* [Added a new argument to the MultiNodeTestRunner to filter specs](https://github.com/akkadotnet/akka.net/pull/1737)
* [close #1758 made Akka.Cluster.Tools and Akka.Cluster.Sharding use correct assembly version info and nuget dependencies](https://github.com/akkadotnet/akka.net/pull/1767)
* [Akka.Remote EndpointWriter backoff bugfix](https://github.com/akkadotnet/akka.net/pull/1777)
* [Akka.Cluster.TestKit (internal use only)](https://github.com/akkadotnet/akka.net/pull/1782)
* [Cluster.Tools.Singleton: Member.UpNumber fix](https://github.com/akkadotnet/akka.net/pull/1799)

**Fixes & Changes - Akka.Persistence**
* [Made JournalEntry.Payload an object and AtLeastOnceDeliverySemantic public](https://github.com/akkadotnet/akka.net/pull/1684)
* [Akka.Persistence - update code base to akka JVM v2.4](https://github.com/akkadotnet/akka.net/pull/1717)
* [Ensure internal stash is unstashed on writes after recovery](https://github.com/akkadotnet/akka.net/pull/1756)
* [Wrap user stash to avoid confusion between PersistentActor.UnstashAll and PersistentActor.Stash.UnstashAll](https://github.com/akkadotnet/akka.net/pull/1757)
* [Fixes initialization of LocalSnapshotStore directory](https://github.com/akkadotnet/akka.net/pull/1761)
* [Fixed global ActorContext in SqlJournal](https://github.com/akkadotnet/akka.net/pull/1760)

**Commit Stats for v1.0.7**

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 12 | 1718 | 2213 | Aaron Stannard |
| 11 | 2187 | 2167 | Silv3rcircl3 |
| 7 | 433 | 75 | JeffCyr |
| 6 | 2 | 1127 | Danthar |
| 6 | 10383 | 3054 | Chris Constantin |
| 3 | 510 | 25 | maxim.salamatko |
| 3 | 5 | 3 | Christopher Martin |
| 2 | 53 | 65 | rogeralsing |
| 2 | 50 | 1 | mukulsinghsaini |
| 2 | 2738 | 2035 | Sean Gilliam |
| 2 | 25 | 4 | Bartosz Sypytkowski |
| 2 | 2 | 2 | utcnow |
| 2 | 14 | 13 | zbynek001 |
| 2 | 130 | 126 | annymsMthd |
| 1 | 58 | 0 | Denis Kostikov |
| 1 | 48 | 43 | voltcode |
| 1 | 213 | 66 | Alex Koshelev |
| 1 | 2 | 2 | Tamas Vajk |
| 1 | 2 | 2 | Marc Piechura |
| 1 | 2 | 1 | Juergen Hoetzel |
| 1 | 19 | 8 | tstojecki |
| 1 | 13 | 13 | Willie Ferguson |
| 1 | 1 | 1 | ravengerUA |

#### 1.0.6 January 18 2016 ####
**Maintenance release for Akka.NET v1.0.5**
This patch consists of many bug fixes, performance improvements, as well as the addition of two brand new alpha modules for Akka.Cluster users.

**Akka.Cluster.Tools** and **Akka.Cluster.Sharding**
The biggest part of this release is the addition of [Akka.Cluster.Tools](http://getakka.net/docs/clustering/cluster-tools) and [Akka.Cluster.Sharding](http://getakka.net/docs/clustering/cluster-sharding), both of which are available now as pre-release packages on NuGet.

```
PM> Install-Package Akka.Cluster.Tools -pre
```
and

```
PM> Install-Package Akka.Cluster.Sharding -pre
```

Respectively, these two packages extend Akka.Cluster to do the following:

1. Distributed pub/sub (Akka.Cluster.Tools)
2. `ClusterClient` - subscribe to changes in cluster availability without actually being part of the cluster itself. (Akka.Cluster.Tools)
3. `ClusterSingleton` - guarantee a single instance of a specific actor throughout the cluster. (Akka.Cluster.Tools)
4. Sharding - partition data into durable stores (built on top of Akka.Persistence) in a manner that is fault-tolerant and recoverable across thecluster. (Akka.Cluster.Sharding)

Check out the documentation for more details!
* http://getakka.net/docs/clustering/cluster-tools
* http://getakka.net/docs/clustering/cluster-sharding

**Fixes & Changes - Akka.NET Core**
* [Fix incorrect serialization of Unicode characters in NewtonSoftJsonSerializer](https://github.com/akkadotnet/akka.net/pull/1508)
* [Fixed: Supervisorstrategy does not preserve stacktrace](https://github.com/akkadotnet/akka.net/issues/1499)
* [added initial performance specs using NBench](https://github.com/akkadotnet/akka.net/pull/1520)
* [Add wire back as contrib package + Serialization TestKit](https://github.com/akkadotnet/akka.net/pull/1503)
* [Implemented the RegisterOnTermination feature.](https://github.com/akkadotnet/akka.net/pull/1523)
* [Increased performance of DedicatedThreadPool](https://github.com/akkadotnet/akka.net/pull/1569)
* [#1605 updated Google.ProtocolBuffers to 2.4.1.555](https://github.com/akkadotnet/akka.net/pull/1634)
* [Clear current message - fixes #1609](https://github.com/akkadotnet/akka.net/pull/1613)
* [Rewrite of the AtomicReference ](https://github.com/akkadotnet/akka.net/pull/1615)
* [Implemented WhenTerminated and Terminate](https://github.com/akkadotnet/akka.net/pull/1614)
* [Implemented StartTime and Uptime](https://github.com/akkadotnet/akka.net/pull/1617)
* [API Diff with fixed Approval file](https://github.com/akkadotnet/akka.net/pull/1639)
* [Fixed: NullReferenceException in Akka.Util.Internal.Collections.ImmutableAvlTreeBase`2.RotateLeft](https://github.com/akkadotnet/akka.net/issues/1202)



**Fixes & Changes - Akka.Remote & Akka.Cluster**
It should be noted that we've improved the throughput from Akka.NET v1.0.5 to 1.0.6 by a factor of 8

* [Akka.Cluster.Tools & Akka.Cluster.Sharding with tests and examples](https://github.com/akkadotnet/akka.net/pull/1530)
* [Added UntrustedSpec](https://github.com/akkadotnet/akka.net/pull/1535)
* [Akka.Remote Performance - String.Format logging perf fix](https://github.com/akkadotnet/akka.net/pull/1540)
* [Remoting system upgrade](https://github.com/akkadotnet/akka.net/pull/1596)
* [PublicHostname defaults to IPAddress.Any when hostname is blank](https://github.com/akkadotnet/akka.net/pull/1621)
* [Removes code that overrides OFF log level with WARNING.](https://github.com/akkadotnet/akka.net/pull/1644)
* [fixes issue with Helios message ordering](https://github.com/akkadotnet/akka.net/pull/1638)
* [Fixed: Actor does not receive "Terminated" message if remoting is used and it is not monitored actor's parent](https://github.com/akkadotnet/akka.net/issues/1646)

**Fixes & Changes - Akka.Persistence**
* [Fixed racing conditions on sql-based snapshot stores](https://github.com/akkadotnet/akka.net/pull/1507)
* [Fix for race conditions in presistence plugins](https://github.com/akkadotnet/akka.net/pull/1543)
* [Fix #1522 Ensure extensions and persistence plugins are only registered/created once](https://github.com/akkadotnet/akka.net/pull/1648)

A special thanks to all of our contributors for making this happen!
18 contributors since release v1.0.5

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 22 | 3564 | 28087 | Aaron Stannard |
| 15 | 1710 | 1303 | rogeralsing |
| 6 | 569 | 95 | Silv3rcircl3 |
| 6 | 53594 | 4417 | Bartosz Sypytkowski |
| 5 | 1786 | 345 | Sean Gilliam |
| 3 | 786 | 159 | maxim.salamatko |
| 2 | 765 | 277 | JeffCyr |
| 2 | 44 | 53 | Chris Constantin |
| 2 | 14 | 2 | Simon Anderson |
| 1 | 84 | 4 | Bart de Boer |
| 1 | 6051 | 27 | danielmarbach |
| 1 | 6 | 2 | tstojecki |
| 1 | 3 | 5 | Ralf1108 |
| 1 | 27 | 0 | Andrew Skotzko |
| 1 | 2 | 2 | easuter |
| 1 | 2 | 1 | Danthar |
| 1 | 182 | 0 | derwasp |
| 1 | 179 | 0 | Onat Yigit Mercan |

#### 1.0.5 December 3 2015 ####
**Maintenance release for Akka.NET v1.0.4**
This release is a collection of bug fixes, performance enhancements, and general improvements contributed by 29 individual contributors.

**Fixes & Changes - Akka.NET Core**
* [Bugfix: Make the Put on the SimpleDnsCache idempotent](https://github.com/akkadotnet/akka.net/commit/2ed1d574f76491707deac236db3fd7c1e5af5757)
* [Add CircuitBreaker Initial based on akka 2.0.5](https://github.com/akkadotnet/akka.net/commit/7e16834ef0ff8551cdd3530eacb1016d40cb1cb8)
* [Fix for receive timeout in async await actor](https://github.com/akkadotnet/akka.net/commit/6474bd7dc3d27756e255d12ef21f331108d9922d)
* [akka-io: fixed High CPU load using the Akka.IO TCP server](https://github.com/akkadotnet/akka.net/commit/4af2cfbcaafa33ea04a1a8b1aa6486e78bd6f821)
* [akka-io: Stop select loop on idle](https://github.com/akkadotnet/akka.net/commit/e545780d36cfb805b2014746a2e97006894c2e00)
* [Serialization fixes](https://github.com/akkadotnet/akka.net/commit/6385cc20a3d310efc0bb2f9e29710c5b7bceaa87)
* [Fix issue #1301 - inprecise resizing of resizable routers](https://github.com/akkadotnet/akka.net/commit/cf714333b25190249f01d79bad606d4ce5863e47)
* [Stashing now checks message equality by reference](https://github.com/akkadotnet/akka.net/commit/884330dfb5d69b523f25a59b98450322fe3b34f4)
* [rewrote ActorPath.TryParseAddrss to now test Uris by try / catch and use Uri.TryCreate instead](https://github.com/akkadotnet/akka.net/commit/8eaf32147a08f213db818bf19d74ed9d1aadaed2)
* [Port EventBusUnsubscribers](https://github.com/akkadotnet/akka.net/commit/bd91bcd50d918e5e8ee4b085e53d603cfd46c89a)
* [Add optional PipeTo completion handlers](https://github.com/akkadotnet/akka.net/commit/dfb7f61026d5d0b2d23efe1dd73af820f70a1d1c)
* [Akka context props output to Serilog](https://github.com/akkadotnet/akka.net/commit/409cd7f4ed0b285827b681685af59ec19c5a4b73)


**Fixes & Changes - Akka.Remote, Akka.Cluster**
* [MultiNode tests can now be skipped by specifying a SkipReason](https://github.com/akkadotnet/akka.net/commit/75f966cb7d2f2c0d859e0e3a90a38d251a10c5e5)
* [Akka.Remote: Discard msg if payload size > max allowed.](https://github.com/akkadotnet/akka.net/commit/05f57b9b1ff256145bc085f94d49a591e51e1304)
* [Throw `TypeLoadException` when an actor type or dependency cannot be found in a remote actor deploy scenario](https://github.com/akkadotnet/akka.net/commit/ffed3eb088bc00f90a5e4b7367d4598fda007401)
* [MultiNode Test Visualizer](https://github.com/akkadotnet/akka.net/commit/7706bb242719b7f7197058e89f8579af5b82dfc3)
* [Fix for Akka.Cluster.Routing.ClusterRouterGroupSettings Mono Linux issue](https://github.com/akkadotnet/akka.net/commit/dbbd2ac9b16772af8f8e35d3d1c8bf5dcf354f42)
* [Added RemoteDeploymentWatcher](https://github.com/akkadotnet/akka.net/commit/44c29ccefaeca0abdc4fd1f81daf1dc27a285f66)
* [Akka IO Transport: framing support](https://github.com/akkadotnet/akka.net/commit/60b5d2a318b485652e0888190aaa930fe43b1bbc)
* [#1443 fix for cluster shutdown](https://github.com/akkadotnet/akka.net/commit/941688aead57266b454b76530a7fb5446f68e15d)

**Fixes & Changes - Akka.Persistence**
* [Fixes the NullReferenceException in #1235 and appears to adhere to the practice of including an addres with the serialized binary.](https://github.com/akkadotnet/akka.net/commit/3df119ff614c3298299f863e18efd6e0fa848858)
* [Port Finite State Machine DSL to Akka.Persistence](https://github.com/akkadotnet/akka.net/commit/dce684d907df86f5039eb2ca20727ab48d4b218a)
* [Become and BecomeStacked for ReceivePersistentActor](https://github.com/akkadotnet/akka.net/commit/b11dafc86eb9284c2d515fd9da3599fe463a5681)
* [Persistent actor stops on recovery failures](https://github.com/akkadotnet/akka.net/commit/03105719a8866e8eadac268bc8f813e738f989b9)
* [Fixed: data races inside sql journal engine](https://github.com/akkadotnet/akka.net/commit/f088f0c681fdc7ba1b4eaf7f823c2a9535d3045d)
* [fix sqlite.conf and readme](https://github.com/akkadotnet/akka.net/commit/c7e925ba624eee7e386855251169aecbafd6ae7d)
* [#1416 created ReceiveActor implementation of AtLeastOnceDeliveryActor base class](https://github.com/akkadotnet/akka.net/commit/4d1d79b568bdae6565423c3ed914f8a9606dc0e8)

A special thanks to all of our contributors, organized below by the number of changes made:

23369 5258  18111 Aaron Stannard
18827 16329 2498  Bartosz Sypytkowski
11994 9496  2498  Steffen Forkmann
6031  4637  1394  maxim.salamatko
1987  1667  320 Graeme Bradbury
1556  1149  407 Sean Gilliam
1118  1118  0 moliver
706 370 336 rogeralsing
616 576 40  Marek Kadek
501 5 496 Alex Koshelev
377 269 108 Jeff Cyr
280 208 72  willieferguson
150 98  52  Christian Palmstierna
85  63  22  Willie Ferguson
77  71  6 Emil Ingerslev
66  61  5 Grover Jackson
60  39  21  Alexander Pantyukhin
56  33  23  Uladzimir Makarau
55  54  1 rdavisau
51  18  33  alex-kondrashov
42  26  16  Silv3rcircl3
36  30  6 evertmulder
33  19  14  Filip Malachowicz
13  11  2 Suhas Chatekar
7 6 1 tintoy
4 2 2 Jonathan
2 1 1 neekgreen
2 1 1 Christopher Martin
2 1 1 Artem Borzilov 

#### 1.0.4 August 07 2015 ####
**Maintenance release for Akka.NET v1.0.3**

**Akka.IO**
This release introduces some major new features to Akka.NET, including [Akka.IO - a new set of capabilities built directly into the Akka NuGet package that allow you to communicate with your actors directly via TCP and UDP sockets](http://getakka.net/docs/IO) from external (non-actor) systems.

If you want to see a really cool example of Akka.IO in action, look at [this sample that shows off how to use the Telnet commandline to interact directly with Akka.NET actors](https://github.com/akkadotnet/akka.net/blob/dev/src/examples/TcpEchoService.Server).

**Akka.Persistence.MongoDb** and **Akka.Persistence.Sqlite**
Two new flavors of Akka.Persistence support are now available. You can install them via the commandline!

```
PM> Install-Package Akka.Persistence.MongoDb -pre
```
and

```
PM> Install-Package Akka.Persistence.Sqlite -pre
```

**Fixes & Changes - Akka.NET Core**
* [F# API - problem with discriminated union serialization](https://github.com/akkadotnet/akka.net/issues/999)
* [Fix Null Sender issue](https://github.com/akkadotnet/akka.net/issues/1212)
* [Outdated FsPickler version in Akka.FSharp can lead to runtime errors when other FsPickler-dependent packages are installed](https://github.com/akkadotnet/akka.net/issues/1206)
* [Add default Ask timeout to HOCON configuration](https://github.com/akkadotnet/akka.net/issues/1163)
* [Remote watching is repeated](https://github.com/akkadotnet/akka.net/issues/1090)
* [RemoteDaemon bug, not removing children ](https://github.com/akkadotnet/akka.net/pull/1068)
* [HOCON include support](https://github.com/akkadotnet/akka.net/pull/1169)
* [Replace SystemNanoTime with MonotonicClock](https://github.com/akkadotnet/akka.net/pull/1174)

**Akka.DI.StructureMap**
We now have support for the [StructureMap dependency injection framework](http://docs.structuremap.net/) out of the box. You can install it here!

```
PM> Install-Package Akka.DI.StructureMap
```

#### 1.0.3 June 12 2015 ####
**Bugfix release for Akka.NET v1.0.2.**

This release addresses an issue with Akka.Persistence.SqlServer and Akka.Persistence.PostgreSql where both packages were missing a reference to Akka.Persistence.Sql.Common. 

In Akka.NET v1.0.3 we've packaged Akka.Persistence.Sql.Common into its own NuGet package and referenced it in the affected packages.

#### 1.0.2 June 2 2015
**Bugfix release for Akka.NET v1.0.1.**

Fixes & Changes - Akka.NET Core
* [Routers seem ignore supervision strategy](https://github.com/akkadotnet/akka.net/issues/996)
* [Replaced DateTime.Now with DateTime.UtcNow/MonotonicClock](https://github.com/akkadotnet/akka.net/pull/1009)
* [DedicatedThreadScheduler](https://github.com/akkadotnet/akka.net/pull/1002)
* [Add ability to specify scheduler implementation in configuration](https://github.com/akkadotnet/akka.net/pull/994)
* [Added generic extensions to EventStream subscribe/unsubscribe.](https://github.com/akkadotnet/akka.net/pull/990)
* [Convert null to NoSender.](https://github.com/akkadotnet/akka.net/pull/993)
* [Supervisor strategy bad timeouts](https://github.com/akkadotnet/akka.net/pull/986)
* [Updated Pigeon.conf throughput values](https://github.com/akkadotnet/akka.net/pull/980)
* [Add PipeTo for non-generic Tasks for exception handling](https://github.com/akkadotnet/akka.net/pull/978)

Fixes & Changes - Akka.NET Dependency Injection
* [Added Extensions methods to ActorSystem and ActorContext to make DI more accessible](https://github.com/akkadotnet/akka.net/pull/966)
* [DIActorProducer fixes](https://github.com/akkadotnet/akka.net/pull/961)
* [closes akkadotnet/akka.net#1020 structuremap dependency injection](https://github.com/akkadotnet/akka.net/pull/1021)

Fixes & Changes - Akka.Remote and Akka.Cluster
* [Fixing up cluster rejoin behavior](https://github.com/akkadotnet/akka.net/pull/962)
* [Added dispatcher fixes for remote and cluster ](https://github.com/akkadotnet/akka.net/pull/983)
* [Fixes to ClusterRouterGroup](https://github.com/akkadotnet/akka.net/pull/953)
* [Two actors are created by remote deploy using Props.WithDeploy](https://github.com/akkadotnet/akka.net/issues/1025)

Fixes & Changes - Akka.Persistence
* [Renamed GuaranteedDelivery classes to AtLeastOnceDelivery](https://github.com/akkadotnet/akka.net/pull/984)
* [Changes in Akka.Persistence SQL backend](https://github.com/akkadotnet/akka.net/pull/963)
* [PostgreSQL persistence plugin for both event journal and snapshot store](https://github.com/akkadotnet/akka.net/pull/971)
* [Cassandra persistence plugin](https://github.com/akkadotnet/akka.net/pull/995)

**New Features:**

**Akka.TestKit.XUnit2**
Akka.NET now has support for [XUnit 2.0](http://xunit.github.io/)! You can install Akka.TestKit.XUnit2 via the NuGet commandline:

```
PM> Install-Package Akka.TestKit.XUnit2
```

**Akka.Persistence.PostgreSql** and **Akka.Persistence.Cassandra**
Akka.Persistence now has two additional concrete implementations for PostgreSQL and Cassandra! You can install either of the packages using the following commandline:

[Akka.Persistence.PostgreSql Configuration Docs](https://github.com/akkadotnet/akka.net/tree/dev/src/contrib/persistence/Akka.Persistence.PostgreSql)
```
PM> Install-Package Akka.Persistence.PostgreSql
```

[Akka.Persistence.Cassandra Configuration Docs](https://github.com/akkadotnet/akka.net/tree/dev/src/contrib/persistence/Akka.Persistence.Cassandra)
```
PM> Install-Package Akka.Persistence.Cassandra
```

**Akka.DI.StructureMap**
Akka.NET's dependency injection system now supports [StructureMap](http://structuremap.github.io/)! You can install Akka.DI.StructureMap via the NuGet commandline:

```
PM> Install-Package Akka.DI.StructureMap
```

#### 1.0.1 Apr 28 2015

**Bugfix release for Akka.NET v1.0.**

Fixes:
* [v1.0 F# scheduling API not sending any scheduled messages](https://github.com/akkadotnet/akka.net/issues/831)
* [PinnedDispatcher - uses single thread for all actors instead of creating persanal thread for every actor](https://github.com/akkadotnet/akka.net/issues/850)
* [Hotfix async await when awaiting IO completion port based tasks](https://github.com/akkadotnet/akka.net/pull/843)
* [Fix for async await suspend-resume mechanics](https://github.com/akkadotnet/akka.net/pull/836)
* [Nested Ask async await causes null-pointer exception in ActorTaskScheduler](https://github.com/akkadotnet/akka.net/issues/855)
* [Akka.Remote: can't reply back remotely to child of Pool router](https://github.com/akkadotnet/akka.net/issues/884)
* [Context.DI().ActorOf shouldn't require a parameterless constructor](https://github.com/akkadotnet/akka.net/issues/832)
* [DIActorContextAdapter uses typeof().Name instead of AssemblyQualifiedName](https://github.com/akkadotnet/akka.net/issues/833)
* [IndexOutOfRangeException with RoundRobinRoutingLogic & SmallestMailboxRoutingLogic](https://github.com/akkadotnet/akka.net/issues/908)

New Features:

**Akka.TestKit.NUnit**
Akka.NET now has support for [NUnit ](http://nunit.org/) inside its TestKit. You can install Akka.TestKit.NUnit via the NuGet commandline:

```
PM> Install-Package Akka.TestKit.NUnit
```

**Akka.Persistence.SqlServer**
The first full implementation of Akka.Persistence is now available for SQL Server.

[Read the full instructions for working with Akka.Persistence.SQLServer here](https://github.com/akkadotnet/akka.net/tree/dev/src/contrib/persistence/Akka.Persistence.SqlServer).

#### 1.0.0 Apr 09 2015

**Akka.NET is officially no longer in beta status**. The APIs introduced in Akka.NET v1.0 will enjoy long-term support from the Akka.NET development team and all of its professional support partners.

Many breaking changes were introduced between v0.8 and v1.0 in order to provide better future extensibility and flexibility for Akka.NET, and we will outline the major changes in detail in these release notes.

However, if you want full API documentation we recommend going to the following:

* **[Latest Stable Akka.NET API Docs](http://api.getakka.net/docs/stable/index.html "Akka.NET Latest Stable API Docs")**
* **[Akka.NET Wiki](http://getakka.net/wiki/ "Akka.NET Wiki")**

----

**Updated Packages with 1.0 Stable Release**

All of the following NuGet packages have been upgraded to 1.0 for stable release:

- Akka.NET Core
- Akka.FSharp
- Akka.Remote
- Akka.TestKit
- Akka.DI (dependency injection)
- Akka.Loggers (logging)

The following packages (and modules dependent on them) are still in *pre-release* status:

- Akka.Cluster
- Akka.Persistence

----
**Introducing Full Mono Support for Akka.NET**

One of the biggest changes in Akka.NET v1.0 is the introduction of full Mono support across all modules; we even have [Raspberry PI machines talking to laptops over Akka.Remote](https://twitter.com/AkkaDotNET/status/584109606714093568)!

We've tested everything using Mono v3.12.1 across OS X and Ubuntu. 

**[Please let us know how well Akka.NET + Mono runs on your environment](https://github.com/akkadotnet/akka.net/issues/694)!**

----

**API Changes in v1.0**

**All methods returning an `ActorRef` now return `IActorRef`**
This is the most significant breaking change introduced in AKka.NET v1.0. Rather than returning the `ActorRef` abstract base class from all of the `ActorOf`, `Sender` and other methods we now return an instance of the `IActorRef` interface instead.

This was done in order to guarantee greater future extensibility without additional breaking changes, so we decided to pay off that technical debt now that we're supporting these APIs long-term.

Here's the set of breaking changes you need to be aware of:

- Renamed:
  - `ActorRef`          --> `IActorRef`
  - `ActorRef.Nobody`   --> `ActorRefs.Nobody`
  - `ActorRef.NoSender` --> `ActorRefs.NoSender`
- `ActorRef`'s  operators `==` and `!=` has been removed. This means all expressions like `actorRef1 == actorRef2` must be replaced with `Equals(actorRef1, actorRef2)`
- `Tell(object message)`, i.e. the implicit sender overload, has been moved
to an extension method, and requires `using Akka.Actor;` to be accessible.
- Implicit cast from `ActorRef` to `Routee` has been replaced with `Routee.FromActorRef(actorRef)`

**`async` / `await` Support**

`ReceiveActor`s now support Async/Await out of the box.

```csharp
public class MyActor : ReceiveActor
{
       public MyActor()
       {
             Receive<SomeMessage>(async some => {
                    //we can now safely use await inside this receive handler
                    await SomeAsyncIO(some.Data);
                    Sender.Tell(new EverythingIsAllOK());                   
             });
       }
}
```

It is also possible to specify the behavior for the async handler, using `AsyncBehavior.Suspend` and  `AsyncBehavior.Reentrant` as the first argument.
When using `Suspend` the normal actor semantics will be preserved, the actor will not be able to process any new messages until the current async operation is completed.
While using `Reentrant` will allow the actor to multiplex messages during the `await` period.
This does not mean that messages are processed in parallel, we still stay true to "one message at a time", but each await continuation will be piped back to the actor as a message and continue under the actors concurrency constraint.

However, `PipeTo` pattern is still the preferred way to perform async operations inside an actor, as it is more explicit and clearly states what is going on.


**Switchable Behaviors**
In order to make the switchable behavior APIs more understandable for both `UntypedActor` and `ReceiveActor` we've updated the methods to the following:

``` C#
Become(newHandler); // become newHandler, without adding previous behavior to the stack (default)
BecomeStacked(newHandler); // become newHandler, without adding previous behavior to the stack (default)
UnbecomeStacked(); //revert to the previous behavior in the stack
```

The underlying behavior-switching implementation hasn't changed at all - only the names of the methods.

**Scheduler APIs**
The `Context.System.Scheduler` API has been overhauled to be both more extensible and understandable going forward. All of the previous capabilities for the `Scheduler` are still available, only in different packaging than they were before.

Here are the new APIs:

``` C#
Context.System.Scheduler
  .ScheduleTellOnce(TimeSpan delay, ICanTell receiver, object message, ActorRef sender);
  .ScheduleTellOnce(TimeSpan delay, ICanTell receiver, object message, ActorRef sender, ICancelable cancelable);
  .ScheduleTellRepeatedly(TimeSpan initialDelay, TimeSpan interval, ICanTell receiver, object message, ActorRef sender);
  .ScheduleTellRepeatedly(TimeSpan initialDelay, TimeSpan interval, ICanTell receiver, object message, ActorRef sender, ICancelable cancelable);

Context.System.Scheduler.Advanced
  .ScheduleOnce(TimeSpan delay, Action action);
  .ScheduleOnce(TimeSpan delay, Action action, ICancelable cancelable);
  .ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action);
  .ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action, ICancelable cancelable);
```

There's also a set of extension methods for specifying delays and intervals in milliseconds as well as methods for all four variants (`ScheduleTellOnceCancelable`, `ScheduleTellRepeatedlyCancelable`, `ScheduleOnceCancelable`, `ScheduleRepeatedlyCancelable`) that creates a cancelable, schedules, and returns the cancelable. 

**Akka.NET `Config` now loaded automatically from App.config and Web.config**
In previous versions Akka.NET users had to do the following to load Akka.NET HOCON configuration sections from App.config or Web.config:

```csharp
var section = (AkkaConfigurationSection)ConfigurationManager.GetSection("akka");
var config = section.AkkaConfig;
var actorSystem = ActorSystem.Create("MySystem", config);
```

As of Akka.NET v1.0 this is now done for you automatically:

```csharp
var actorSystem = ActorSystem.Create("MySystem"); //automatically loads App/Web.config, if any
```

**Dispatchers**
Akka.NET v1.0 introduces the `ForkJoinDispatcher` as well as general purpose dispatcher re-use.

**Using ForkJoinDispatcher**
ForkJoinDispatcher is special - it uses [`Helios.Concurrency.DedicatedThreadPool`](https://github.com/helios-io/DedicatedThreadPool) to create a dedicated set of threads for the exclusive use of the actors configured to use a particular `ForkJoinDispatcher` instance. All of the remoting actors depend on the `default-remote-dispatcher` for instance.

Here's how you can create your own ForkJoinDispatcher instances via Config:

```
myapp{
  my-forkjoin-dispatcher{
    type = ForkJoinDispatcher
    throughput = 100
    dedicated-thread-pool{ #settings for Helios.DedicatedThreadPool
      thread-count = 3 #number of threads
      #deadlock-timeout = 3s #optional timeout for deadlock detection
      threadtype = background #values can be "background" or "foreground"
    }
  }
}
}
```

You can then use this specific `ForkJoinDispatcher` instance by configuring specific actors to use it, whether it's via config or the fluent interface on `Props`:

**Config**
```
akka.actor.deploy{
     /myActor1{
       dispatcher = myapp.my-forkjoin-dispatcher
     }
}
```

**Props**
```csharp
var actor = Sys.ActorOf(Props.Create<Foo>().WithDispatcher("myapp.my-forkjoin-dispatcher"));
```

**FluentConfiguration [REMOVED]**
`FluentConfig` has been removed as we've decided to standardize on HOCON configuration, but if you still want to use the old FluentConfig bits you can find them here: https://github.com/rogeralsing/Akka.FluentConfig

**F# API**
The F# API has changed to reflect the other C# interface changes, as well as unique additions specific to F#.

In addition to updating the F# API, we've also fixed a long-standing bug with being able to serialize discriminated unions over the wire. This has been resolved.

**Interface Renames**
In order to comply with .NET naming conventions and standards, all of the following interfaces have been renamed with the `I{InterfaceName}` prefix.

The following interfaces have all been renamed to include the `I` prefix:

- [X] `Akka.Actor.ActorRefProvider, Akka` (Public)
- [X] `Akka.Actor.ActorRefScope, Akka` (Public)
- [X] `Akka.Actor.AutoReceivedMessage, Akka` (Public)
- [X] `Akka.Actor.Cell, Akka` (Public)
- [X] `Akka.Actor.Inboxable, Akka` (Public)
- [X] `Akka.Actor.IndirectActorProducer, Akka` (Public)
- [X] `Akka.Actor.Internal.ChildrenContainer, Akka` (Public)
- [X] `Akka.Actor.Internal.ChildStats, Akka` (Public)
- [X] `Akka.Actor.Internal.InternalSupportsTestFSMRef`2, Akka` (Public)
- [X] `Akka.Actor.Internal.SuspendReason+WaitingForChildren, Akka`
- [X] `Akka.Actor.Internals.InitializableActor, Akka` (Public)
- [X] `Akka.Actor.LocalRef, Akka`
- [X] `Akka.Actor.LoggingFSM, Akka` (Public)
- [X] `Akka.Actor.NoSerializationVerificationNeeded, Akka` (Public)
- [X] `Akka.Actor.PossiblyHarmful, Akka` (Public)
- [X] `Akka.Actor.RepointableRef, Akka` (Public)
- [X] `Akka.Actor.WithBoundedStash, Akka` (Public)
- [X] `Akka.Actor.WithUnboundedStash, Akka` (Public)
- [X] `Akka.Dispatch.BlockingMessageQueueSemantics, Akka` (Public)
- [X] `Akka.Dispatch.BoundedDequeBasedMessageQueueSemantics, Akka` (Public)
- [X] `Akka.Dispatch.BoundedMessageQueueSemantics, Akka` (Public)
- [X] `Akka.Dispatch.DequeBasedMailbox, Akka` (Public)
- [X] `Akka.Dispatch.DequeBasedMessageQueueSemantics, Akka` (Public)
- [X] `Akka.Dispatch.MessageQueues.MessageQueue, Akka` (Public)
- [X] `Akka.Dispatch.MultipleConsumerSemantics, Akka` (Public)
- [X] `Akka.Dispatch.RequiresMessageQueue`1, Akka` (Public)
- [X] `Akka.Dispatch.Semantics, Akka` (Public)
- [X] `Akka.Dispatch.SysMsg.SystemMessage, Akka` (Public)
- [X] `Akka.Dispatch.UnboundedDequeBasedMessageQueueSemantics, Akka` (Public)
- [X] `Akka.Dispatch.UnboundedMessageQueueSemantics, Akka` (Public)
- [X] `Akka.Event.LoggingAdapter, Akka` (Public)
- [X] `Akka.FluentConfigInternals, Akka` (Public)
- [X] `Akka.Remote.InboundMessageDispatcher, Akka.Remote`
- [X] `Akka.Remote.RemoteRef, Akka.Remote`
- [X] `Akka.Routing.ConsistentHashable, Akka` (Public)

**`ConsistentHashRouter` and `IConsistentHashable`**
Akka.NET v1.0 introduces the idea of virtual nodes to the `ConsistentHashRouter`, which are designed to provide more even distributions of hash ranges across a relatively small number of routees. You can take advantage of virtual nodes via configuration:

```xml
akka.actor.deployment {
	/router1 {
		router = consistent-hashing-pool
		nr-of-instances = 3
		virtual-nodes-factor = 17
	}
}
```

Or via code:

```csharp
var router4 = Sys.ActorOf(Props.Empty.WithRouter(
	new ConsistentHashingGroup(new[]{c},hashMapping: hashMapping)
	.WithVirtualNodesFactor(5)), 
	"router4");
```

**`ConsistentHashMapping` Delegate**
There are three ways to instruct a router to hash a message:
1. Wrap the message in a `ConsistentHashableEnvelope`;
2. Implement the `IConsistentHashable` interface on your message types; or
3. Or, write a `ConsistentHashMapper` delegate and pass it to a `ConsistentHashingGroup` or a `ConsistentHashingPool` programmatically at create time.

Here's an example, taken from the `ConsistentHashSpecs`:

```csharp
ConsistentHashMapping hashMapping = msg =>
{
    if (msg is Msg2)
    {
        var m2 = msg as Msg2;
        return m2.Key;
    }

    return null;
};
var router2 =
    Sys.ActorOf(new ConsistentHashingPool(1, null, null, null, hashMapping: hashMapping)
    .Props(Props.Create<Echo>()), "router2");
```

Alternatively, you don't have to pass the `ConsistentHashMapping` into the constructor - you can use the `WithHashMapping` fluent interface built on top of both `ConsistentHashingGroup` and `ConsistentHashingPool`:

```csharp
var router2 =
    Sys.ActorOf(new ConsistentHashingPool(1).WithHashMapping(hashMapping)
    .Props(Props.Create<Echo>()), "router2");
```

**`ConsistentHashable` renamed to `IConsistentHashable`**
Any objects you may have decorated with the `ConsistentHashable` interface to work with `ConsistentHashRouter` instances will need to implement `IConsistentHashable` going forward, as all interfaces have been renamed with the `I-` prefix per .NET naming conventions.

**Akka.DI.Unity NuGet Package**
Akka.NET now ships with dependency injection support for [Unity](http://unity.codeplex.com/).

You can install our Unity package via the following command in the NuGet package manager console:

```
PM> Install-Package Akka.DI.Unity
```

----

#### 0.8.0 Feb 11 2015

__Dependency Injection support for Ninject, Castle Windsor, and AutoFac__. Thanks to some amazing effort from individual contributor (**[@jcwrequests](https://github.com/jcwrequests "@jcwrequests")**), Akka.NET now has direct dependency injection support for [Ninject](http://www.ninject.org/), [Castle Windsor](http://docs.castleproject.org/Default.aspx?Page=MainPage&NS=Windsor&AspxAutoDetectCookieSupport=1), and [AutoFac](https://github.com/autofac/Autofac).

Here's an example using Ninject, for instance:

    // Create and build your container 
    var container = new Ninject.StandardKernel(); 
	container.Bind().To(typeof(TypedWorker)); 
	container.Bind().To(typeof(WorkerService));
    
    // Create the ActorSystem and Dependency Resolver 
	var system = ActorSystem.Create("MySystem"); 
	var propsResolver = new NinjectDependencyResolver(container,system);

	//Create some actors who need Ninject
	var worker1 = system.ActorOf(propsResolver.Create<TypedWorker>(), "Worker1");
	var worker2 = system.ActorOf(propsResolver.Create<TypedWorker>(), "Worker2");

	//send them messages
	worker1.Tell("hi!");

You can install these DI plugins for Akka.NET via NuGet - here's how:

* **Ninject** - `install-package Akka.DI.Ninject`
* **Castle Windsor** - `install-package Akka.DI.CastleWindsor`
* **AutoFac** - `install-package Akka.DI.AutoFac`

**Read the [full Dependency Injection with Akka.NET documentation](http://getakka.net/wiki/Dependency%20injection "Dependency Injection with Akka.NET") here.**

__Persistent Actors with Akka.Persistence (Alpha)__. Core contributor **[@Horusiath](https://github.com/Horusiath)** ported the majority of Akka's Akka.Persistence and Akka.Persistence.TestKit modules. 

> Even in the core Akka project these modules are considered to be "experimental," but the goal is to provide actors with a way of automatically saving and recovering their internal state to a configurable durable store - such as a database or filesystem.

Akka.Persistence also introduces the notion of *reliable delivery* of messages, achieved through the `GuaranteedDeliveryActor`.

Akka.Persistence also ships with an FSharp API out of the box, so while this package is in beta you can start playing with it either F# or C# from day one.

If you want to play with Akka.Persistence, please install any one of the following packages:

* **Akka.Persistence** - `install-package Akka.Persistence -pre`
* **Akka.Persistence.FSharp** - `install-package Akka.Persistence.FSharp -pre`
* **Akka.Persistence.TestKit** - `install-package Akka.Persistence.TestKit -pre`

**Read the [full Persistent Actors with Akka.NET documentation](http://getakka.net/wiki/Persistence "Persistent Actors with Akka.NET") here.**

__Remote Deployment of Routers and Routees__. You can now remotely deploy routers and routees via configuration, like so:

**Deploying _routees_ remotely via `Config`**:

	actor.deployment {
	    /blub {
	      router = round-robin-pool
	      nr-of-instances = 2
	      target.nodes = [""akka.tcp://${sysName}@localhost:${port}""]
	    }
	}

	var router = masterActorSystem.ActorOf(new RoundRobinPool(2).Props(Props.Create<Echo>()), "blub");

When deploying a router via configuration, just specify the `target.nodes` property with a list of `Address` instances for each node you want to deploy your routees.

> NOTE: Remote deployment of routees only works for `Pool` routers.

**Deploying _routers_ remotely via `Config`**:

	actor.deployment {
	    /blub {
	      router = round-robin-pool
	      nr-of-instances = 2
	      remote = ""akka.tcp://${sysName}@localhost:${port}""
	    }
	}

	var router = masterActorSystem.ActorOf(Props.Create<Echo>().WithRouter(FromConfig.Instance), "blub");

Works just like remote deployment of actors.

If you want to deploy a router remotely via explicit configuration, you can do it in code like this via the `RemoteScope` and `RemoteRouterConfig`:

**Deploying _routees_ remotely via explicit configuration**:

    var intendedRemoteAddress = Address.Parse("akka.tcp://${sysName}@localhost:${port}"
    .Replace("${sysName}", sysName)
    .Replace("${port}", port.ToString()));
    
     var router = myActorSystem.ActorOf(new RoundRobinPool(2).Props(Props.Create<Echo>())
    .WithDeploy(new Deploy(
		new RemoteScope(intendedRemoteAddress.Copy()))), "myRemoteRouter");

**Deploying _routers_ remotely via explicit configuration**:

    var intendedRemoteAddress = Address.Parse("akka.tcp://${sysName}@localhost:${port}"
    .Replace("${sysName}", sysName)
    .Replace("${port}", port.ToString()));
    
     var router = myActorSystem.ActorOf(
		new RemoteRouterConfig(
		new RoundRobinPool(2), new[] { new Address("akka.tcp", sysName, "localhost", port) })
        .Props(Props.Create<Echo>()), "blub2");

**Improved Serialization and Remote Deployment Support**. All internals related to serialization and remote deployment have undergone vast improvements in order to support the other work that went into this release.

**Pluggable Actor Creation Pipeline**. We reworked the plumbing that's used to provide automatic `Stash` support and exposed it as a pluggable actor creation pipeline for local actors.

This release adds the `ActorProducerPipeline`, which is accessible from `ExtendedActorSystem` (to be able to configure by plugins) and allows you to inject custom hooks satisfying following interface:


    interface IActorProducerPlugin {
	    bool CanBeAppliedTo(ActorBase actor);
	    void AfterActorCreated(ActorBase actor, IActorContext context);
	    void BeforeActorTerminated(ActorBase actor, IActorContext context);
    }

- **CanBeAppliedTo** determines if plugin can be applied to specific actor instance.
- **AfterActorCreated** is applied to actor after it has been instantiated by an `ActorCell` and before `InitializableActor.Init` method will (optionally) be invoked.
- **BeforeActorTerminated** is applied before actor terminates and before `IDisposable.Dispose` method will be invoked (for disposable actors) - **auto handling disposable actors is second feature of this commit**.

For common use it's better to create custom classes inheriting from `ActorProducerPluginBase` and `ActorProducerPluginBase<TActor>` classes.

Pipeline itself provides following interface:

    class ActorProducerPipeline : IEnumerable<IActorProducerPlugin> {
	    int Count { get; } // current plugins count - 1 by default (ActorStashPlugin)
	    bool Register(IActorProducerPlugin plugin)
	    bool Unregister(IActorProducerPlugin plugin)
	    bool IsRegistered(IActorProducerPlugin plugin)
	    bool Insert(int index, IActorProducerPlugin plugin)
    }

- **Register** - registers a plugin if no other plugin of the same type has been registered already (plugins with generic types are counted separately). Returns true if plugin has been registered.
- **Insert** - same as register, but plugin will be placed in specific place inside the pipeline - useful if any plugins precedence is required.
- **Unregister** - unregisters specified plugin if it has been found. Returns true if plugin was found and unregistered.
- **IsRegistered** - checks if plugin has been already registered.

By default pipeline is filled with one already used plugin - `ActorStashPlugin`, which replaces stash initialization/unstashing mechanism used up to this moment.

**MultiNodeTestRunner and Akka.Remote.TestKit**. The MultiNodeTestRunner and the Multi Node TestKit (Akka.Remote.TestKit) underwent some drastic changes in this update. They're still not quite ready for public use yet, but if you want to see what the experience is like you can [clone the Akka.NET Github repository](https://github.com/akkadotnet/akka.net) and run the following command:

````
C:\akkadotnet> .\build.cmd MultiNodeTests
````

This will automatically launch all `MultiNodeSpec` instances found inside `Akka.Cluster.Tests`. We'll need to make this more flexible to be able to run other assemblies that require multinode tests in the future.

These tests are not enabled by default in normal build runs, but they will at some point in the future.

Here's a sample of the output from the console, to give you a sense of what the reporting looks like:

![image](https://cloud.githubusercontent.com/assets/326939/6075685/5f7c56b2-ad8c-11e4-9d93-8216a8cbabaf.png)

The MultiNodeTestRunner uses XUnit internally and will dynamically deploy as many processes are needed to satisfy any individual test. Has been tested with up to 6 processes.


#### 0.7.1 Dec 13 2014
__Brand New F# API__. The entire F# API has been updated to give it a more native F# feel while still holding true to the Erlang / Scala conventions used in actor systems. [Read more about the F# API changes](https://github.com/akkadotnet/akka.net/pull/526).

__Multi-Node TestKit (Alpha)__. Not available yet as a NuGet package, but the first pass at the Akka.Remote.TestKit is now available from source, which allow you to test your actor systems running on multiple machines or processes.

A multi-node test looks like this

    public class InitialHeartbeatMultiNode1 : InitialHeartbeatSpec
    {
    }
    
    public class InitialHeartbeatMultiNode2 : InitialHeartbeatSpec
    {
    }
    
    public class InitialHeartbeatMultiNode3 : InitialHeartbeatSpec
    {
    }
    
    public abstract class InitialHeartbeatSpec : MultiNodeClusterSpec
The MultiNodeTestRunner looks at this, works out that it needs to create 3 processes to run 3 nodes for the test.
It executes NodeTestRunner in each process to do this passing parameters on the command line. [Read more about the multi-node testkit here](https://github.com/akkadotnet/akka.net/pull/497).

__Breaking Change to the internal api: The `Next` property on `IAtomicCounter<T>` has been changed into the function `Next()`__ This was done as it had side effects, i.e. the value was increased when the getter was called. This makes it very hard to debug as the debugger kept calling the property and causing the value to be increased.

__Akka.Serilog__ `SerilogLogMessageFormatter` has been moved to the namespace `Akka.Logger.Serilog` (it used to be in `Akka.Serilog.Event.Serilog`).
Update your `using` statements from `using Akka.Serilog.Event.Serilog;` to `using Akka.Logger.Serilog;`.

__Breaking Change to the internal api: Changed signatures in the abstract class `SupervisorStrategy`__. The following methods has new signatures: `HandleFailure`, `ProcessFailure`. If you've inherited from `SupervisorStrategy`, `OneForOneStrategy` or `AllForOneStrategy` and overridden the aforementioned methods you need to update their signatures.

__TestProbe can be implicitly casted to ActorRef__. New feature. Tests requiring the `ActorRef` of a `TestProbe` can now be simplified:
``` C#
var probe = CreateTestProbe();
var sut = ActorOf<GreeterActor>();
sut.Tell("Akka", probe); // previously probe.Ref was required
probe.ExpectMsg("Hi Akka!");
```

__Bugfix for ConsistentHashableEvenlope__. When using `ConsistentHashableEvenlope` in conjunction with `ConsistentHashRouter`s, `ConsistentHashableEvenlope` now correctly extracts its inner message instead of sending the entire `ConsistentHashableEvenlope` directly to the intended routee.

__Akka.Cluster group routers now work as expected__. New update of Akka.Cluster - group routers now work as expected on cluster deployments. Still working on pool routers. [Read more about Akka.Cluster routers here](https://github.com/akkadotnet/akka.net/pull/489).

#### 0.7.0 Oct 16 2014
Major new changes and additions in this release, including some breaking changes...

__Akka.Cluster__ Support (pre-release) - Akka.Cluster is now available on NuGet as a pre-release package (has a `-pre` suffix) and is available for testing. After installing the the Akka.Cluster module you can add take advantage of clustering via configuration, like so:

    akka {
        actor {
          provider = "Akka.Cluster.ClusterActorRefProvider, Akka.Cluster"
        }

        remote {
          log-remote-lifecycle-events = DEBUG
          helios.tcp {
        hostname = "127.0.0.1"
        port = 0
          }
        }

        cluster {
          seed-nodes = [
        "akka.tcp://ClusterSystem@127.0.0.1:2551",
        "akka.tcp://ClusterSystem@127.0.0.1:2552"]

          auto-down-unreachable-after = 10s
        }
      }


And then use cluster-enabled routing on individual, named routers:

    /myAppRouter {
     router = consistent-hashing-pool
      nr-of-instances = 100
      cluster {
        enabled = on
        max-nr-of-instances-per-node = 3
        allow-local-routees = off
        use-role = backend
      }
    }

For more information on how clustering works, please see https://github.com/akkadotnet/akka.net/pull/400

__Breaking Changes: Improved Stashing__ - The old `WithUnboundedStash` and `WithBoundedStash` interfaces have been slightly changed and the `CurrentStash` property has been renamed to `Stash`. Any old stashing code can be replaced with the following in order to continue working:

    public IStash CurrentStash { get { return Stash; } set { Stash=value; } }

The `Stash` field is now automatically populated with an appropriate stash during the actor creation process and there is no need to set this field at all yourself.

__Breaking Changes: Renamed Logger Namespaces__ - The namespaces, DLL names, and NuGet packages for all logger add-ons have been changed to `Akka.Loggers.Xyz`. Please install the latest NuGet package (and uninstall the old ones) and update your Akka HOCON configurations accordingly.

__Serilog Support__ - Akka.NET now has an official [Serilog](http://serilog.net/) logger that you can install via the `Akka.Logger.Serilog` package. You can register the serilog logger via your HOCON configuration like this:

     akka.loggers=["Akka.Logger.Serilog.SerilogLogger, Akka.Logger.Serilog"]

__New Feature: Priority Mailbox__ - The `PriorityMailbox` allows you to define the priority of messages handled by your actors, and this is done by creating your own subclass of either the `UnboundedPriorityMailbox` or `BoundedPriorityMailbox` class and implementing the `PriorityGenerator` method like so:

    public class ReplayMailbox : UnboundedPriorityMailbox
    {
        protected override int PriorityGenerator(object message)
        {
            if (message is HttpResponseMessage) return 1;
            if (!(message is LoggedHttpRequest)) return 2;
            return 3;
        }
    }

The smaller the return value from the `PriorityGenerator`, the higher the priority of the message. You can then configure your actors to use this mailbox via configuration, using a fully-qualified name:


    replay-mailbox {
     mailbox-type: "TrafficSimulator.PlaybackApp.Actors.ReplayMailbox,TrafficSimulator.PlaybackApp"
    }

And from this point onward, any actor can be configured to use this mailbox via `Props`:

    Context.ActorOf(Props.Create<ReplayActor>()
                        .WithRouter(new RoundRobinPool(3))
                        .WithMailbox("replay-mailbox"));

__New Feature: Test Your Akka.NET Apps Using Akka.TestKit__ - We've refactored the testing framework used for testing Akka.NET's internals into a test-framework-agnostic NuGet package you can use for unit and integration testing your own Akka.NET apps. Right now we're scarce on documentation so you'll want to take a look at the tests inside the Akka.NET source for reference.

Right now we have Akka.TestKit adapters for both MSTest and XUnit, which you can install to your own project via the following:

MSTest:

    install-package Akka.TestKit.VsTest

XUnit:

    install-package Akka.TestKit.Xunit

__New Feature: Logging to Standard Out is now done in color__ - This new feature can be disabled by setting `StandardOutLogger.UseColors = false;`.
Colors can be customized: `StandardOutLogger.DebugColor = ConsoleColor.Green;`.
If you need to print to stdout directly use `Akka.Util.StandardOutWriter.Write()` instead of `Console.WriteLine`, otherwise your messages might get printed in the wrong color.

#### 0.6.4 Sep 9 2014
* Introduced `TailChoppingRouter`
* All `ActorSystem` extensions now take an `ExtendedActorSystem` as a dependency - all third party actor system extensions will need to update accordingly.
* Fixed numerous bugs with remote deployment of actors.
* Fixed a live-lock issue for high-traffic connections on Akka.Remote and introduced softer heartbeat failure deadlines.
* Changed the configuration chaining process.
* Removed obsolete attributes from `PatternMatch` and `UntypedActor`.
* Laying groundwork for initial Mono support.

#### 0.6.3 Aug 13 2014
* Made it so HOCON config sections chain properly
* Optimized actor memory footprint
* Fixed a Helios bug that caused Akka.NET to drop messages larger than 32kb

#### 0.6.2 Aug 05 2014
* Upgraded Helios dependency
* Bug fixes
* Improved F# API
* Resizeable Router support
* Inbox support - an actor-like object that can be subscribed to by external objects
* Web.config and App.config support for Akka HOCON configuration

#### 0.6.1 Jul 09 2014
* Upgraded Helios dependency
* Added ConsistentHash router support
* Numerous bug fixes
* Added ReceiveBuilder support

#### 0.2.1-beta Mars 22 2014
* Nuget package
