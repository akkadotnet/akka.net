---
uid: building-and-distributing
title: Building Akka.NET Repositories
---

# Building Akka.NET Repositories

Akka.NET's build system is a modified version of [Petabridge's `dotnet new` template](https://github.com/petabridge/petabridge-dotnet-new), in particular [the Petabridge.Library template](https://github.com/petabridge/Petabridge.Library/) - we typically keep our build system in sync with the documentation you can find there.

> [!TIP]
> All repositories in the [Akka.NET Github organization](https://github.com/akkadotnet) use a nearly identical build process. Type `build.cmd help` or `build.sh help` in the root of any repository to see a full list of supported build instructions.

## Supported Commands

This project supports a wide variety of commands.

To list on Windows:

```console
build.cmd help
```

To list on Linux / OS X:

```console
build.sh help
```

However, please see this readme for full details.

### Summary

* `build.[cmd|sh] all` - runs the entire build system minus documentation: `NBench`, `Tests`, and `Nuget`.
* `build.[cmd|sh] buildrelease` - compiles the solution in `Release` mode.
* `build.[cmd|sh] runtests` - compiles the solution in `Release` mode and runs the unit test suite (all projects that end with the `.Tests.csproj` suffix) but only under the .NET Framework configuration. All of the output will be published to the `./TestResults` folder.
* `build.[cmd|sh] runtestsnetcore` - compiles the solution in `Release` mode and runs the unit test suite (all projects that end with the `.Tests.csproj` suffix) but only under the .NET Core configuration. All of the output will be published to the `./TestResults` folder.
* `build.[cmd|sh] MultiNodeTests` - compiles the solution in `Release` mode and runs the [multi-node unit test suite](xref:multi-node-testing) (all projects that end with the `.Tests.csproj` suffix) but only under the .NET Framework configuration. All of the output will be published to the `./TestResults/multinode` folder.
* `build.[cmd|sh] MultiNodeTestsNetCore` - compiles the solution in `Release` mode and runs the [multi-node unit test suite](xref:multi-node-testing) (all projects that end with the `.Tests.csproj` suffix) but only under the .NET Core configuration. All of the output will be published to the `./TestResults/multinode` folder.
* `build.[cmd|sh] MultiNodeTestsNetCore spec={className}` - compiles the solution in `Release` mode and runs the [multi-node unit test suite](xref:multi-node-testing) (all projects that end with the `.Tests.csproj` suffix) but only under the .NET Core configuration. Only tests that match the `{className}` will run. All of the output will be published to the `./TestResults/multinode` folder. This is a very useful setting for running multi-node tests locally.
* `build.[cmd|sh] nbench` - compiles the solution in `Release` mode and runs the [NBench](https://nbench.io/) performance test suite (all projects that end with the `.Tests.Performance.csproj` suffix). All of the output will be published to the `./PerfResults` folder.
* `build.[cmd|sh] nuget` - compiles the solution in `Release` mode and creates Nuget packages from any project that does not have `<IsPackable>false</IsPackable>` set and uses the version number from `RELEASE_NOTES.md`.
* `build.[cmd|sh] nuget nugetprerelease=dev` - compiles the solution in `Release` mode and creates Nuget packages from any project that does not have `<IsPackable>false</IsPackable>` set - but in this instance all projects will have a `VersionSuffix` of `-beta{DateTime.UtcNow.Ticks}`. It's typically used for publishing nightly releases.
* `build.[cmd|sh] nuget nugetpublishurl=$(nugetUrl) nugetkey=$(nugetKey)` - compiles the solution in `Release` modem creates Nuget packages from any project that does not have `<IsPackable>false</IsPackable>` set using the version number from `RELEASE_NOTES.md`and then publishes those packages to the `$(nugetUrl)` using NuGet key `$(nugetKey)`.
* `build.[cmd|sh] DocFx` - compiles the solution in `Release` mode and then uses [DocFx](http://dotnet.github.io/docfx/) to generate website documentation inside the `./docs/_site` folder. Use the `./serve-docs.cmd` on Windows to preview the documentation.

This build script is powered by [FAKE](https://fake.build/); please see their API documentation should you need to make any changes to the [`build.fsx`](https://github.com/akkadotnet/akka.net/blob/dev/build.fsx) file.

### Incremental Builds

Akka.NET is a large project, so it's often necessary to run tests incrementally in order to reduce the total end-to-end build time during development. In Akka.NET this is accomplished using [the Incrementalist project](https://github.com/petabridge/Incrementalist) - which can be invoked by adding the `incremental` option to any `build.sh` or `build.cmd` command:

```console
PS> build.cmd MultiNodeTestsNetCore spec={className} incremental
```

This option will work locally on Linux or Windows.

### Release Notes, Version Numbers, Etc

This project will automatically populate its release notes in all of its modules via the entries written inside [`RELEASE_NOTES.md`](https://github.com/akkadotnet/akka.net/blob/dev/RELEASE_NOTES.md) and will automatically update the versions of all assemblies and NuGet packages via the metadata included inside [`common.props`](https://github.com/akkadotnet/akka.net/blob/dev/src/common.props).

#### RELEASE_NOTES.md

```text
#### 0.1.0 October 05 2019 ####
First release
```

In this instance, the NuGet and assembly version will be `0.1.0` based on what's available at the top of the `RELEASE_NOTES.md` file.

#### RELEASE_NOTES.md

```text
#### 0.1.0-beta1 October 05 2019 ####
First release
```

But in this case the NuGet and assembly version will be `0.1.0-beta1`.

If you add any new projects to the solution created with this template, be sure to add the following line to each one of them in order to ensure that you can take advantage of `common.props` for standardization purposes:

```xml
<Import Project="..\common.props" />
```

### Conventions

The attached build script will automatically do the following based on the conventions of the project names added to this project:

* Any project name ending with `.Tests` will automatically be treated as a [XUnit2](https://xunit.github.io/) project and will be included during the test stages of this build script;
* Any project name ending with `.Tests.Performance` will automatically be treated as a [NBench](https://github.com/petabridge/NBench) project and will be included during the test stages of this build script; and
* Any project meeting neither of these conventions will be treated as a NuGet packaging target and its `.nupkg` file will automatically be placed in the `bin\nuget` folder upon running the `build.[cmd|sh] all` command.

## Triggering Builds and Updates on Akka.NET Github Repositories

## Routine Updates and Pull Requests

Akka.NET uses Azure DevOps to run its builds and the conventions it uses are rather sample:

1. All pull requests should be created on their own feature branch and should be sent to Akka.NET's `dev` branch;
2. Always review your own pull requests so other developers understand why you made the changes;
3. Any pull request that gets merged into the `dev` branch will appear in the [Akka.NET Nightly Build that evening](xref:nightly-builds); and
4. Always `squash` any merges into the `dev` branch in order to preserve a clean commit history.

Please read "[How to Use Github Professionally](https://petabridge.com/blog/use-github-professionally/)" for some more general ideas on how to work with a project like Akka.NET on Github.
