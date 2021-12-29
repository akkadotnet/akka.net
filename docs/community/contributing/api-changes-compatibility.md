---
uid: making-public-api-changes
title: Making Public API Changes
---

# Making Public API Changes

Akka.NET follows the [practical semantic versioning methodology](https://aaronstannard.com/oss-semver/), and as such the most important convention we have to be mindful of is accurately communicating to our users whether or not Akka.NET is compatible with previous versions of the API.

Here is what that entails:

* Not all `public` types are part of the "public API" - some public types that are marked with the `InternalApi` attribute or live inside an `.Internal` namespace, for instance, might be public for reasons that have to do with extensibility or completeness but they are not part of the supported public API. We may make breaking changes on those APIs even between revision releases because they're explicitly advertised as not for public use.
* Everything else that is `public`, including components that can be loaded via reflection, is generally considered to be part of the public API.

As such, we have automated procedures designed to ensure that accidental breaking / incompatible changes to the Akka.NET public API can't sail through the pull request process without some human acknowledgement first.

## Akka.NET API Versioning Policy

We do our best to follow "practical semantic versioning" - here is what that means in practice for Akka.NET and its plugins:

1. **No surprise breaking changes under any circumstances** - we don't let things happen to Akka.NET users by accident. This means observing the lesson of [Chesterton's Fence](https://fs.blog/chestertons-fence/): unless an API is explicitly marked as `InternalApi` or is included inside an `.Internal` namespace assume that it's actively used by downstream plugins or applications and therefore can't be broken on a moments' notice. Users _have_ to be given a heads-up that this is going to happen so they can plan accordingly. This is true for revision, minor versions, and major versions. Therefore, [socialize your proposals for changing public APIs](https://petabridge.com/blog/use-github-professionally/) on Github long before you ever submit a pull request so the Akka.NET team can help plan your changes into a future release.
2. **New or extended public APIs can be introduced during any release** - we try to observe extend-only design as best we can throughout Akka.NET's API and wire formats, which means in essence never removing or changing the meaning of an existing API but always being free to add new ones. This can always be done throughout major, minor, or revision releases.
3. **`Obsolete` APIs can be removed between minor or major versions** - if you want to remove an `Obsolete` API it needs to be done between major / minor versions and explicitly documented in the release notes.
4. **Removing deprecated binaries only happens between minor or major versions** - in the Akka.NET v1.4 lifecycle we deprecated the `Akka.DI.*` plugins and replaced them with a consolidated `Akka.DependencyInjection` implementation. We stopped shipping updates to `Akka.DI.Autofac` as part of this effort. However, we had to ensure that all `Akka.DI.*` plugins still worked over the course of the v1.4 lifespan since we hadn't announced a planned change to users yet. Upgrade cycles happen gradually and we need to give users time to adapt to changes in the ecosystem. We can't pull the rug out from under users all at once. Thus, we still ship updates to those core `Akka.DI.*` libraries up until Akka.NET v1.5.

This document outlines how to comply with said procedures.

## API Change Procedures

The goal of this process is to make conscious decisions about API changes and force the discovery of those changes during the pull request review. Here is how the process works:

* Uses [ApiApprovals](http://jake.ginnivan.net/apiapprover/) and [ApprovalTests](https://github.com/approvals/ApprovalTests.Net) to generate a public API of a given assembly.
* The public API gets approved by a human into a `*.approved.txt` file.
* Every time the API approval test runs the API is generated again into a `*.received.txt` file. If the two files don't match the test fails on the CI server or locally. Locally on the dev's machine the predefined Diff viewer pops up (never happens on CI) and the dev has to approve the API changes (therefore making a conscious decision)
* Each PR making public API changes will contain the `*.approved.txt` file in the DIFF and all reviewers can easily see the breaking changes on the public API.

In Akka.NET, the API approval tests can be found in the following test assembly:

    src/core/Akka.API.Tests

The approval file is located at:

    src/core/Akka.API.Tests/CoreAPISpec.ApproveCore.approved.txt

To generate a new approval file:

```shell
PS> cd src/core/Akka.API.Tests
PS> dotnet test -c Release --framework net6.0
```

You'll need to make sure you have an appropriate mergetool installed in order to update the `.approved.txt` files. We recommend [WinMerge](https://winmerge.org/) or [TortoiseMerge](https://tortoisesvn.net/TortoiseMerge.html).

### Approving a New Change

After modifying some code in Akka.NET that results in a public API change - this can be any change, such as adding an overload to a public method or adding a new public class, you will immediately see an API change when you attempt to run the `Akka.API.Tests` unit tests:

![Failed API approval test](~/images/api-diff-fail.png)

The tests will fail, because the `.approved.txt` file doesn't match the new `.received.txt`, but you will be prompted by [ApprovalTests](https://github.com/approvals/ApprovalTests.Net) to view the diff between the two files in your favorite diff viewer:

![API difference as seen in a diff viewer like TortoiseMerge or WinMerge](~/images/api-diff-viewer.png)

After you've merged the changes generated from your code into the `approved.txt` file, the tests will pass:

![Passed API approval test](~/images/api-diff-approve.png)

And then once you've merged in those changes, added them to a Git commit, and sent them in a pull request then other Akka.NET contributors will review your pull request and view the differences between the current `approved.txt` file and the one included in your PR:

![approved.txt differences as reported by Git](~/images/diff-results.png)

## Unacceptable API Changes

The following types of API changes will generally not be approved:

1. Any breaking modification to a commonly used public interface;
2. Modifying existing public API member signatures - extension is fine, modification is not;
3. Renaming public classes or members; and
4. Changing an access modifier from public to private / internal / protected on any member that is or is meant to be used.

## How to Safely Introduce Public API Changes: Extend-Only Design

So if we need to expose a new member and / or deprecate an existing member inside the public API, how can this be done safely?

At the center of it all is [extend-only design](https://aaronstannard.com/extend-only-design/):

1. **Previous functionality, schema, or behavior is immutable** and not open for modification. Anything you made available as a public release lives on with its current behavior, API, and definitions and isn't able to be changed.
2. **New functionality, schema, or behavior can be introduced through new constructs only** and ideally those should be opt-in.
3. **Old functionality can only be removed after a long period of time** and that's measured in years.

How do these resolve some of the frustrating problems around versioning?

1. Old behavior, schema, and APIs are always available and supported even in newer versions of the software;
2. New behavior is introduced as opt-in extensions that may or may not be used by the code; and
3. Both new and old code pathways are supported concurrently.

What does this look like in practice?

1. Old, no-longer-recommended methods still function but are marked with an `Obsolete` attribute;
2. New methods are made opt-in if their behavior differs significantly from previous implementations; and
3. We add new overloads when we need to pass in new values or parameters, rather than change existing method signatures.
