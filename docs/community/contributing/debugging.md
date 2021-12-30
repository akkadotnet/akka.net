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
