#### 1.3.17 December 20 2019 ####
**Maintenance Release for Akka.NET 1.3**

1.3.17 consists of non-breaking bugfixes and additions that have been contributed against the [Akka.NET v1.4.0 milestone](https://github.com/akkadotnet/akka.net/milestone/17) thus far.

This patch includes some important fixes for Akka.Remote and Akka.Cluster users, including

* [Akka.Remote: Fix Endpoint receive buffer stack overflow](https://github.com/akkadotnet/akka.net/pull/4089)
* [Akka.Remote: Don't log aborted connection as disassociation error](https://github.com/akkadotnet/akka.net/pull/4101)
* [Akka.Cluster: Added delayed heartbeat logging](https://github.com/akkadotnet/akka.net/pull/4057)
* [Akka.Cluster: Remove string interpolation from cluster logs](https://github.com/akkadotnet/akka.net/pull/4084)
* [Akka.Cluster: Add timeout to abort joining of seed nodes](https://github.com/akkadotnet/akka.net/pull/3863)
* [Akka.Cluster.Sharding: Fix state non-empty check when starting HandOffStopper](https://github.com/akkadotnet/akka.net/pull/4043)

To [see the full set of changes in Akka.NET v1.3.17, click here](https://github.com/akkadotnet/akka.net/pull/4112).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 4 | 231 | 74 | Ismael Hamed |
| 3 | 72 | 70 | Jonathan Nagy |
| 2 | 2 | 9 | Aaron Stannard |
| 1 | 92 | 10 | Valdis Zobēla |
| 1 | 29 | 2 | Igor Fedchenko |