---
uid: releasing-packages
title: Creating New Releases of Akka.NET Packages
---

# Creating New Releases of Akka.NET Packages
The process for creating new NuGet releases of Akka.NET or any of its projects is standardized:

1. Update the `RELEASE_NOTES.md` file to include a summary of all relevant changes and the new updated version number;
2. Merge the `dev` branch into the `master` branch _by creating a merge commit_ to the history in `master` matches `dev`;
3. Create a `git tag` that matches the version number in the `RELEASE_NOTES.md` file; and
4. Push the `tag` to the main Github repository.

This will trigger a new NuGet release to be created, with the release notes from the `RELEASE_NOTES.md` file copied into the body of the NuGet package description.
