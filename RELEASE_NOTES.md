#### 1.3.15 September 23 2019 ####
**Maintenance Release for Akka.NET 1.3**

1.3.15 consists of non-breaking bugfixes and additions that have been contributed against the [Akka.NET v1.4.0 milestone](https://github.com/akkadotnet/akka.net/milestone/17) thus far.

This really only includes one major fix: [a major issue with Akka.Remote, which caused unnecessary `Quarantine` events](https://github.com/akkadotnet/akka.net/issues/3905).

We highly recommend upgrading to this build if you're using Akka.Remote or Akka.Cluster.

To [see the full set of changes in Akka.NET v1.3.15, click here](https://github.com/akkadotnet/akka.net/pull/3931).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 3 | 443 | 196 | Aaron Stannard |