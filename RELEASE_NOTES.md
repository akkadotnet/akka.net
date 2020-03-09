#### 1.3.18 March 09 2020 ####
**Maintenance Release for Akka.NET 1.3**

1.3.18 consists of non-breaking bugfixes and additions that have been contributed against the [Akka.NET v1.4.0 milestone](https://github.com/akkadotnet/akka.net/milestone/17) thus far.

This patch includes some important fixes for Akka.Remote including

* [Akka Bugfix: Atomicity bug and bad performance in AddressTerminatedTopic](https://github.com/akkadotnet/akka.net/issues/4306)
* [Akka Bugfix: Actor reference leak in AddressTerminatedTopic](https://github.com/akkadotnet/akka.net/issues/4304)
* [Akka.Remote Bugfix: DotNetty.Codecs.CorruptedFrameException: negative pre-adjustment length field](https://github.com/akkadotnet/akka.net/issues/3879)
* [Akka.Remote Bugfix: "The given key was not present in the dictionary" exception](https://github.com/akkadotnet/akka.net/issues/4246)

To [see the full set of changes in Akka.NET v1.3.18, click here](https://github.com/akkadotnet/akka.net/releases/tag/1.3.18).