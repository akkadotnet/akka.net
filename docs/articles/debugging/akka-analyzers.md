---
uid: akka-analyzers
title: Akka.Analyzers - Roslyn Analyzers and Code Fixes for Akka.NET
---

# Akka.Analyzers

As of Akka.NET v1.5.15 we now include [Akka.Analyzers](https://github.com/akkadotnet/akka.analyzers) as a package dependency for the core Akka library, which means any projects that reference anything depending on [`Akka`](https://www.nuget.org/packages/Akka) will automatically pull in all of Akka.Analyzer's rules and code fixes.

Akka.Analyzer is a [Roslyn Analysis and Code Fix](https://learn.microsoft.com/en-us/visualstudio/extensibility/getting-started-with-roslyn-analyzers) package, which means that it leverages the .NET compiler platform ("Roslyn") to detect Akka.NET-specific anti-patterns and problems during _compilation_, rather than at run-time.

## Supported Rules

| Id     | Title                                                  | Severity | Category     |
|--------|--------------------------------------------------------|----------|--------------|
| [AK1000](xref:AK1000) | Do not use `new` to create actors.                     | Error    | Actor Design |
| [AK1001](xref:AK1001) | Should always close over `Sender` when using `PipeTo`. | Error    | Actor Design |
| [AK2000](xref:AK2000) | Do not use `Ask` with `TimeSpan.Zero` for timeout.     | Error    | API Usage    |
