---
uid: akka-analyzers
title: Akka.Analyzers - Roslyn Analyzers and Code Fixes for Akka.NET
---

# Akka.Analyzers

As of Akka.NET v1.5.15 we now include [Akka.Analyzers](https://github.com/akkadotnet/akka.analyzers) as a package dependency for the core Akka library, which means any projects that reference anything depending on [`Akka`](https://www.nuget.org/packages/Akka) will automatically pull in all of Akka.Analyzer's rules and code fixes.

Akka.Analyzer is a [Roslyn Analysis and Code Fix](https://learn.microsoft.com/en-us/visualstudio/extensibility/getting-started-with-roslyn-analyzers) package, which means that it leverages the .NET compiler platform ("Roslyn") to detect Akka.NET-specific anti-patterns and problems during _compilation_, rather than at run-time.

## Supported Rules

| Id                    | Title                                                                                                                   | Severity | Category     |
|-----------------------|-------------------------------------------------------------------------------------------------------------------------|----------|--------------|
| [AK1000](xref:AK1000) | Do not use `new` to create actors.                                                                                      | Error    | Actor Design |
| [AK1002](xref:AK1002) | Must not await `Self.GracefulStop()` inside `ReceiveAsync<T>()` or `ReceiveAnyAsync`.                                   | Error    | Actor Design |
| [AK1003](xref:AK1003) | `ReceiveAsync<T>()` or `ReceiveAnyAsync()` message handler without async lambda body.                                   | Warning  | Actor Design |
| [AK1004](xref:AK1004) | `ScheduleTellOnce()` and `ScheduleTellRepeatedly()` can cause memory leak if not properly canceled                      | Warning  | Actor Design |
| [AK1005](xref:AK1005) | Must close over `Sender` or `Self`                                                                                      | Warning  | Actor Design |
| [AK1006](xref:AK1006) | Should not call `Persist()` or `PersistAsync()` inside a loop                                                           | Warning  | Actor Design |
| [AK1007](xref:AK1007) | `Timers.StartSingleTimer()` and `Timers.StartPeriodicTimer()` must not be used inside AroundPreRestart() or PreRestart() | Error    | Actor Design |
| [AK2000](xref:AK2000) | Do not use `Ask` with `TimeSpan.Zero` for timeout.                                                                      | Error    | API Usage    |
| [AK2001](xref:AK2001) | Do not use automatically handled messages in inside `Akka.Cluster.Sharding.IMessageExtractor`s.                         | Warning  | API Usage    |

## Deprecated Rules

| Id                    | Title                                                  | Severity | Category     |
|-----------------------|--------------------------------------------------------|----------|--------------|
| [AK1001](xref:AK1001) | Should always close over `Sender` when using `PipeTo`. | Error    | Actor Design |
