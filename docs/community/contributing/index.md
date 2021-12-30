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

### Review Someone Else's Pull Request

Reviewing pull requests takes a lot of time, especially larger ones that touch multiple areas of the Akka.NET code. Anyone can jump in and review anyone else's pull request on Akka.NET's GitHub repositories, not just the core developers and maintainers - thus you can be of great help to the project by reviewing someone else's pull request.

### What Makes For an Effective Pull Request Review?

An effective PR review is design to look for _substantial reasons not to merge a pull request_.

Good reasons not to merge a pull request are:

1. The code included in the PR doesn't actually work or is currently failing its tests;
2. The code might not be secure;
3. The code might be caused a related set of tests to fail;
4. The code isn't sufficiently covered by tests to check against future regressions;
5. The code really needs to have documentation to go along with it;
6. The PR doesn't comport with our standards for the project;
7. The PR takes a dependency on a third party library that we don't really want to include with Akka.NET; or
8. The code implements a feature or a change that really isn't aligned with the project's goals.

Those are good reasons not to merge a pull request, but the goal of the reviewer is to explain that to the original author and to _help the author resolve those objections_ through constructive, helpful feedback.

During the course of your review, you should comment on the individual changes in the file and provide the following types of feedback to the developer:

* Ask clarifying questions - "why did you choose to do this this way?"
* Request additional comments in the code or documentation - "you need to add an XML-DOC comment for this;" "you need to add some comments explaining why you chose this;" or "you should add some documentation for this to the website."
* Explicitly link to areas in our guidelines this pull request violated - i.e. "[you haven't completed the API approval check](xref:making-public-api-changes#approving-a-new-change) for this pull request."
* Suggest ways in which this code could be better written to comport with best practices or Akka.NET idioms - i.e. "you should really use the `ReceiveActor` API instead of using `PatternMatch`."
* Look for areas that aren't sufficiently well-tested and help the contributor understand what's needed to properly check against regressions.
* Point contributors to our "[Debugging Akka.NET](xref:debugging-akkadotnet-core)" documentation if they run into trouble with getting their tests to pass.

## Creating Contributions That Will Be Merged

First, we strongly recommend reading the following two blog posts prior to contributing to Akka.NET - these generally explain how we use GitHub and the workflow we've used for years to manage the project:

1. "[How to Use Github Professionally](https://petabridge.com/blog/use-github-professionally/)"
2. "[Learning the Github Workflow](https://petabridge.com/blog/github-workflow/)"

Odds are, someone who is not you is going to end up maintaining the code or documentation you contribute.

Therefore, you have to convince those long-term maintainers to:

* Decide that your proposed changes are worth the perpetual maintenance effort;
* Spend hours carefully reviewing your propose changes;
* Spend even more time quality-controlling and testing overhead of your changes; and
* Fleshing out the documentation, examples, and tutorials to accommodate your changes.

This isn't being mean - it's what is necessary to maintain Akka.NET's professional standards and to conserve the scarce amount of time maintainers and core developers have to produce new releases that satisfy those standards.

The rest of this guide will provide you with some suggestions on how to create contributions that will be successfully merged.

### File a GitHub Issue Before You Code

**This is especially important if you want to contribute a new feature to Akka.NET**.

The pull requests that will almost certainly never be merged into the Akka.NET repository are for "drive-by" features that were never socialized, discussed, or proposed to maintainers in writing prior to being written. Why would we take the time to carefully review something that no one asked for?

Save yourself and everyone else a lot of time and trouble by proposing it in the form [a new GitHub issue](https://github.com/akkadotnet/akka.net/issues/new/choose) first.

The issue templates will force you to:

* Spell out why you think this change would be valuable;
* What problems not having this change causes;
* What the current alternatives or work-arounds are in Akka.NET; and
* Any other reasons why solving this issue is important, urgent, or otherwise useful.

Maintainers of Akka.NET _want_ to make Akka.NET as valuable for as many users as possible, so it's in our interest to consider these. Having _other users_ of Akka.NET jump in and support your issue ([which you can socialize in our chatroom](https://gitter.im/akkadotnet/akka.net)) further adds to this "usefulness" or "urgency" signal that maintainers seek.

But most importantly, starting your work by filing an issue first gives maintainers a chance to give you useful feedback on it. For instance, maybe we've already tried something like this in the past and abandoned it for reasons you're unaware of. Sharing knowledge, experience, and creativity in both directions creates an outcome that is stronger than the sum of the parts - so give that process a chance to happen _first_ before the code exists.

It's much more difficult to have that creative conversation around a piece of code that's already set in stone, more or less. Increase your odds of success by engaging the other project members first - even for bugs and other small changes.

### Review Our Contribution Standards and Processes

If you're on this webpage you're already doing the right thing - please see all of the following documents if they're relevant to your changes:

* [Building Akka.NET](xref:building-and-distributing)
* [Akka.NET Coding Guidelines](xref:contributor-guidelines)
* [Akka.NET API Compatibility Guidelines](xref:making-public-api-changes)
* [Akka.NET Wire Compatibility Guidelines](xref:wire-compatibility)
* [Akka.NET Documentation Guidelines](xref:documentation-guidelines)

### Review Your Own Pull Requests

One thing that will greatly, greatly improve the odds of your pull requests being merged: _review your own PRs first_.

When you review your own pull request, here is what we want to know about each change:

1. Why was this necessary?
2. What does it do differently than before?
3. What are some non-obvious things you'd want the next developer to know about this code?
4. How is this change safe?

These _greatly_ reduce the amount of guesswork and inspection the other maintainers have to make and will greatly improve the speed with which these pull requests can be merged.
