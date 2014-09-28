#### 0.6.5

#### 0.6.4 Sep 9 2014
* Introduced `TailChoppingRouter`
* All `ActorSystem` extensions now take an `ExtendedActorSystem` as a dependency - all thirdy party actor system extensions will need to update accordingly.
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
