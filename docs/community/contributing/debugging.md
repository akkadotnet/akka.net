---
uid: debugging-akkadotnet-core
title: Debugging Akka.NET
---

# Debugging Akka.NET

> [!NOTE]
> This article is intended to provide advice to OSS contributors working on Akka.NET itself, not necessarily end-users of the software. End users might still find this advice helpful, however.

## Racy Unit Tests

Akka.NET's test suite is quite large and periodically experiences intermittent "racy" failures as a result of various issues. This is a problem for the project as a whole because it causes us not to carefully investigate periodic and intermittent test failures as thoroughly as we should.

You can view [the test flip rate report for Akka.NET on Azure DevOps here](https://dev.azure.com/dotnet/Akka.NET/_test/analytics?definitionId=84&contextType=build).

What are some common reasons that test flip and how can we debug or fix them?

### Cause 1: Expecting Messages in Fixed Order

One common reason for tests to experience high flip rates is that they expect events to happen in a fixed order, whereas due to arbitrary scheduling that's not always the case.

For example:

![]

### Cause 2: Not Accounting for System Message Processing Order

An important caveat when working with Akka.NET actors: system messages always get processed ahead of user-defined messages. `Context.Watch` or `Context.Stop` are examples of methods frequently called from user code which produce system messages.

Thus, we can get into trouble if we aren't careful about how we write our tests.

An example of a buggy test:

[!code-csharp[BuggySysMsgSpec](../../../src/core/Akka.Docs.Tests/Debugging/RacySpecs.cs?name=PoorSysMsgOrdering)]

Because system messages jump the line there is no guarantee that this actor will ever successfully process their system message - it depends on the whims on the `ThreadPool` and how long it takes this actor to get activated, hence why it's racy.

There are various ways to rewrite this test to function correctly without any raciness, but the easiest way to do this is to re-arrange the assertions:

[!code-csharp[CorrectSysMsgOrdering](../../../src/core/Akka.Docs.Tests/Debugging/RacySpecs.cs?name=CorrectSysMsgOrdering)]

In the case of `Context.Watch` and `ExpectTerminated`, there's a second way we can rewrite this test which doesn't require us to alter the fundamental structure of the original buggy test:

[!code-csharp[PoisonPillSysMsgOrdering](../../../src/core/Akka.Docs.Tests/Debugging/RacySpecs.cs?name=PoisonPillSysMsgOrdering)]

The bottom line in this case is that specs can be racy because system messages don't follow the ordering guarantees of the other 99.99999% of user messages. This particular issue is most likely to occur when you're writing specs that look for `Terminated` messages or ones that test supervision strategies, both of which necessitate system messages behind the scenes.