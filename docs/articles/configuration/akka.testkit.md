## Akka.TestKit Configuration
Below is the default HOCON configuration for the base `Akka.TestKit` package.

[!code[Akka.TestKit.dll HOCON Configuration](../../../src/core/Akka.TestKit/Internal/Reference.conf)]

Additionally, it's also possible to change the default [`IScheduler` implementation](../../api/Akka.Actor.IScheduler.yml) in the `Akka.TestKit` to use [a virtualized `TestScheduler` implementation](../../api/Akka.TestKit.TestScheduler.yml) that Akka.NET developers can use to artificially advance time forward. To swap in the `TestScheduler`, developers will want to include the HOCON below:

[!code[Akka.TestKit.dll TestScheduler HOCON Configuration](../../../src/core/Akka.TestKit/Configs/TestScheduler.conf)]
