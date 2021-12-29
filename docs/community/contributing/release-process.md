---
uid: releasing-packages
title: Creating New Releases of Akka.NET Packages
---

# Creating New Releases of Akka.NET Packages

The process for creating new NuGet releases of Akka.NET or any of its projects is standardized across all repositories in the [Akka.NET GitHub organization](https://github.com/akkadotnet/).

![Akka.NET NuGet package release process](/images/community/build-instructions/release-process.png)

## Update `RELEASE_NOTES.md`

If the current `RELEASE_NOTES.md` file looks like this:

```yml
#### 1.4.30 December 20 2021 ####
Akka.NET v1.4.30 is a minor release that contains some enhancements for Akka.Streams and some bug fixes.

New features:
* [Akka: Added StringBuilder pooling in NewtonsoftJsonSerializer](https://github.com/akkadotnet/akka.net/pull/4929)
* [Akka.TestKit: Added InverseFishForMessage](https://github.com/akkadotnet/akka.net/pull/5430)
* [Akka.Streams: Added custom frame sized Flow to Framing](https://github.com/akkadotnet/akka.net/pull/5444)
* [Akka.Streams: Allow Stream to be consumed as IAsyncEnumerable](https://github.com/akkadotnet/akka.net/pull/4742) 

Bug fixes:
* [Akka.Cluster: Reverted startup sequence change](https://github.com/akkadotnet/akka.net/pull/5437)

If you want to see the [full set of changes made in Akka.NET v1.4.30, click here](https://github.com/akkadotnet/akka.net/milestone/61).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 6 | 75 | 101 | Aaron Stannard |
| 2 | 53 | 5 | Brah McDude |
| 2 | 493 | 12 | Drew |
| 1 | 289 | 383 | Andreas Dirnberger |
| 1 | 220 | 188 | Gregorius Soedharmo |
| 1 | 173 | 28 | Ismael Hamed |
```

And we want to release a new 1.4.31 revision of Akka.NET, we'd append this to the _top_ of the `RELEASE_NOTES.md` file:

```yml
#### 1.4.31 December 20 2021 ####
Akka.NET v1.4.30 is a minor release that contains some bug fixes.

Akka.NET v1.4.30 contained a breaking change that broke binary compatibility with all Akka.DI plugins.
Even though those plugins are deprecated that change is not compatible with our SemVer standards 
and needed to be reverted. We regret the error.

Bug fixes:
* [Akka: Reverted Props code refactor](https://github.com/akkadotnet/akka.net/pull/5454)

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 1 | 9 | 2 | Gregorius Soedharmo |
```

For each release of Akka.NET or any of its plugins we include the following in the notes for each release:

1. The new version number;
2. The publication date;
3. A human-written explanation for the changes;
4. A link to each of the major changes introduced in the release;
5. If necessary, a link to the GitHub Issue Milestone for this release; and
6. For the Akka.NET main project only, we run the `/tools/contributors.sh` script to generate the list of contributions by username.

This data will be embedded into the `<ReleaseNotes>` NuGet metadata tag (via `common.props` or `Directory.Build.props`) and also in the GitHub Release artifacts listed on the repository.

<!-- markdownlint-disable titlecase-rule -->
## Update `dev` and `master` Branches
<!-- markdownlint-enable titlecase-rule -->

The `dev` branch contains the most recent "unshipped" changes - the `master` branch contains the most recent "shipped" changes. To do a new release we need to:

1. Merge the updated `RELEASE_NOTES.md` into `dev` via a "squash and merge" pull request and
2. Merge the entire `dev` branch into `master` via a "merge commit" pull request.

## Add a Version-Specific Tag

Next we need to add an appropriate version tag to the repository:

    git tag -a {version} -m "{project} {version}"
    git push upstream --tags

Once this tag is pushed to the central repository it will kick off an Azure DevOps job that will run the [build system](xref:building-and-distributing)'s `build.cmd NuGet` command with the appropriate keys and publish destinations.
