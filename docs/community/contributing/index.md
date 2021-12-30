---
uid: contributing-to-akkadotnet
title: Contributing to Akka.NET
---

# Contributing to Akka.NET

We welcome contributions to all of the Akka.NET organization projects from third party contributors, and this area of the documentation is designed to help explain:

* How you can contribute to the Akka.NET project and
* How to ensure that your contributions will be accepted.

## How to Contribute

What are the ways you can contribute to the Akka.NET project?

### File a Bug Report

One of the most valuable ways you can contribute to Akka.NET is to file a high quality bug report on any of our repositories. [Our GitHub issue templates](https://github.com/akkadotnet/.github/tree/master/.github/ISSUE_TEMPLATE) will help you structure this information in a useful, valuable way for us.

### Fix Bugs and Other Small Problems

Akka.NET's code base is large and touches on some conceptually difficult areas of computing (concurrency, serialization, distributed systems, performance) but despite that most of the bugs reported in our issue trackers tend to be fairly small changes. _Finding_ the bug is usually the hardest part.

If you want to help, take a look at any open issues with the "[confirmed bug](https://github.com/akkadotnet/akka.net/issues?q=is%3Aissue+is%3Aopen+label%3A%22confirmed+bug%22)" or "[potential bug](https://github.com/akkadotnet/akka.net/issues?q=is%3Aissue+is%3Aopen+label%3A%22potential+bug%22)" labels and offer to fix it in the comments.

The Akka.NET project also classifies some easier / more conceptually straight-forward issues with the following two labels:

* "[up for grabs](https://github.com/akkadotnet/akka.net/issues?q=is%3Aissue+is%3Aopen+label%3A%22up+for+grabs%22)" - it's not currently assigned and anyone should feel free to claim this issue and begin working on it.
* "[good for first time contributors](https://github.com/akkadotnet/akka.net/labels/good%20for%20first-time%20contributors)" - these are issues that someone totally new to working with the central Akka.NET repository could successfully resolve.

Here are [some tips that might help you debug Akka.NET itself](xref:debugging-akkadotnet-core).

### Documentation Improvements

We have a number of open issues for improving the Akka.NET documentation, examples, and tutorials - which you can find via the ["docs" issue label](https://github.com/akkadotnet/akka.net/labels/docs).

We have a [very detailed guide on how to contribute to the Akka.NET documentation here](xref:documentation-guidelines).

### Improve Performance

Akka.NET treats performance as a feature of the framework and therefore we're always looking for ways to improve:

1. Message processing throughput in-memory, over Akka.Remote, Akka.Persistence, and Akka.Streams;
2. Message processing latency over `Ask<T>`, Akka.Remote, Akka.Persistence, and Akka.Streams;
3. New actor allocation overhead;
4. Actor memory footprint; and
5. Idle CPU consumption in Akka.Cluster. 

You can find various open performance issues that have been reported by Akka.NET users and contributors by looking for the "[perf](https://github.com/akkadotnet/akka.net/labels/perf)" label.

We use a combination of [BenchmarkDotNet](https://benchmarkdotnet.org/), [NBench](https://nbench.io/), and some custom benchmark programs to measure these facets of Akka.NET's performance.

You can find all of our benchmarks inside the [`/src/benchmark` directory](https://github.com/akkadotnet/akka.net/tree/dev/src/benchmark).

We welcome any and all help in working to improve these performance issues so long as those performance fixes don't compromise our [API compatibility](xref:making-public-api-changes) or [wire compatibility](xref:wire-compatibility) guidelines.

### Port a Missing Feature or Add a New One

We welcome porting additional features from the [original Akka project](https://akka.io/) or proposing entirely new features, but this is a larger project and you should read the rest of this document before attempting it.

## Creating Contributions That Will be Merged

First, we strongly recommend reading the following two blog posts prior to contributing to Akka.NET - these generally explain how we use GitHub and the workflow we've used for years to manage the project:

1. "[How to Use Github Professionally](https://petabridge.com/blog/use-github-professionally/)"
2. "[Learning the Github Workflow](https://petabridge.com/blog/github-workflow/)"