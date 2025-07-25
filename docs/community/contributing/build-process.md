---
uid: building-and-distributing
title: Building Akka.NET Repositories
---

# Building Akka.NET Repositories

Akka.NET has migrated from using FAKE build scripts to using the .NET CLI for building and testing. This approach provides better integration with modern .NET tooling and improved performance.

> [!NOTE]
> We have dropped the F# FAKE build script (`build.fsx`) in favor of using the .NET CLI directly. The build system now uses standard .NET commands and tools.

## Prerequisites

* .NET SDK (as specified in `global.json`)
* PowerShell

## Building the Solution

### Basic Build Commands

To build the entire solution:

```bash
# Build in Debug mode (default)
dotnet build

# Build in Release mode
dotnet build -c Release

# Build with warnings as errors
dotnet build -warnaserror
```

For more information, see the [dotnet build documentation](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-build).

### Building Specific Projects

```bash
# Build a specific project
dotnet build src/core/Akka/Akka.csproj

# Build with specific configuration
dotnet build src/core/Akka/Akka.csproj -c Release
```

## Running Tests

### All Tests

```bash
# Run all tests in Release mode
dotnet test -c Release

# Run tests with specific framework
dotnet test -c Release --framework net8.0
dotnet test -c Release --framework net48
```

For more information, see the [dotnet test documentation](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-test).

### Specific Test Projects

```bash
# Run tests for a specific project
dotnet test src/core/Akka.Tests/Akka.Tests.csproj -c Release

# Run tests with filtering
dotnet test -c Release --filter DisplayName="TestName"
dotnet test -c Release --filter "FullyQualifiedName~TestClass"
```

### Multi-Node Tests

```bash
# Run multi-node tests
dotnet test -c Release --framework net8.0 --filter "Category=MultiNodeTest"

# Run specific multi-node test class
dotnet test -c Release --filter "FullyQualifiedName~ClusterSpec"
```

### Performance Tests

```bash
# Run performance tests (NBench)
dotnet test -c Release --filter "Category=Performance"
```

## Incremental Builds with Incrementalist

Akka.NET is a large project, so it's often necessary to run tests incrementally to reduce build time during development. The project uses [Incrementalist](https://github.com/petabridge/Incrementalist) for optimized builds.

### Using Incrementalist

```bash
# Run only tests for changed projects
dotnet incrementalist run --config .incrementalist/testsOnly.json -- test -c Release --no-build --framework net8.0

# Run multi-node tests incrementally
dotnet incrementalist run --config .incrementalist/mutliNodeOnly.json -- test -c Release --no-build --framework net8.0

# Run all tests incrementally
dotnet incrementalist run --config .incrementalist/incrementalist.json -- test -c Release --no-build
```

### Incrementalist Configuration

The project includes several Incrementalist configuration files:

* `.incrementalist/incrementalist.json` - General incremental build configuration
* `.incrementalist/testsOnly.json` - Configuration for running only tests
* `.incrementalist/mutliNodeOnly.json` - Configuration for multi-node tests only

For more information about Incrementalist, visit the [GitHub repository](https://github.com/petabridge/Incrementalist).

## Creating NuGet Packages

```bash
# Create packages in Release mode
dotnet pack -c Release

# Create packages with version suffix (for nightly builds)
dotnet pack -c Release -p:VersionSuffix=beta$(Get-Date -Format "yyyyMMddHHmmss")

# Create packages for specific project
dotnet pack src/core/Akka/Akka.csproj -c Release
```

For more information, see the [dotnet pack documentation](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-pack).

## Documentation Generation

```bash
# Generate documentation using DocFX
dotnet docfx metadata ./docs/docfx.json --warningsAsErrors
dotnet docfx build ./docs/docfx.json --warningsAsErrors

# Serve documentation locally (Windows)
./serve-docs.cmd

# Serve documentation locally (PowerShell - works on Windows, macOS, and Linux)
./serve-docs.ps1
```

For more information about DocFX, see the [DocFX documentation](https://dotnet.github.io/docfx/).

## Release Notes and Version Management

The project uses a PowerShell script (`build.ps1`) to handle release notes and version updates:

```powershell
# Update release notes and version information
./build.ps1
```

This script:

* Reads release notes from `RELEASE_NOTES.md`
* Updates version information in `Directory.Build.props`
* Prepares the project for building with the correct version

> [!NOTE]
> PowerShell is now available on Linux and macOS, so the `build.ps1` script can be run on all supported platforms.

## CI/CD Integration

The project uses Azure DevOps for continuous integration. The build pipelines use the same .NET CLI commands shown above, ensuring consistency between local development and CI/CD environments.

### Common CI Commands

```bash
# Restore tools
dotnet tool restore

# Build solution
dotnet build -c Release

# Run tests with results output
dotnet test -c Release --framework net8.0 --logger:trx --results-directory TestResults

# Create packages
dotnet pack -c Release -o $(Build.ArtifactStagingDirectory)/nuget
```

For more information about .NET tools, see the [dotnet tool documentation](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-tool).

## Troubleshooting

If you were previously using the FAKE build scripts, here are the equivalent .NET CLI commands:

| FAKE Command | .NET CLI Equivalent |
|--------------|---------------------|
| `build.cmd buildrelease` | `dotnet build -c Release` |
| `build.cmd runtests` | `dotnet test -c Release --framework net48` |
| `build.cmd runtestsnetcore` | `dotnet test -c Release --framework net8.0` |
| `build.cmd nuget` | `dotnet pack -c Release` |
| `build.cmd DocFx` | `dotnet docfx build ./docs/docfx.json` |

The new approach provides better integration with modern .NET tooling and improved performance through incremental builds.
