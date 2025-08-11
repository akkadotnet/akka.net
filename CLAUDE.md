# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build and Test Commands

### Building the Solution
```bash
# Standard build
dotnet build
dotnet build -c Release

# Build with warnings as errors (CI validation)
dotnet build -warnaserror
```

### Running Tests
```bash
# Run all tests
dotnet test -c Release

# Run tests for specific framework
dotnet test -c Release --framework net8.0
dotnet test -c Release --framework net48

# Run specific test by name
dotnet test -c Release --filter DisplayName="TestName"

# Run tests in a specific project
dotnet test path/to/project.csproj -c Release
```

### Incremental Testing (for changed code only)
```bash
# Run only unit tests for changed projects
dotnet incrementalist run --config .incrementalist/testsOnly.json -- test -c Release --no-build --framework net8.0

# Run only multi-node tests for changed projects
dotnet incrementalist run --config .incrementalist/mutliNodeOnly.json -- test -c Release --no-build --framework net8.0
```

### Code Quality
```bash
# Format check
dotnet format --verify-no-changes

# API compatibility check
dotnet test -c Release src/core/Akka.API.Tests
```

### Documentation
```bash
# Generate API documentation
dotnet docfx metadata ./docs/docfx.json --warningsAsErrors
dotnet docfx build ./docs/docfx.json --warningsAsErrors
```

## High-Level Architecture

### Project Structure
- **`/src/core/`** - Core actor framework components
  - `Akka/` - Base actor system, routing, dispatchers, configuration
  - `Akka.Remote/` - Distributed actor communication and serialization
  - `Akka.Cluster/` - Clustering, gossip protocols, distributed coordination
  - `Akka.Persistence/` - Event sourcing, snapshots, journals
  - `Akka.Streams/` - Reactive streams with backpressure
  - `Akka.TestKit/` - Testing utilities for actor systems

- **`/src/contrib/`** - Contributed modules (DI integrations, serializers, cluster extensions)
- **`/src/benchmark/`** - Performance benchmarks using BenchmarkDotNet
- **`/src/examples/`** - Sample applications demonstrating patterns

### Key Architectural Concepts
- **Actor Model**: Message-driven, hierarchical supervision, location transparency
- **Fault Tolerance**: Supervision strategies, let-it-crash philosophy
- **Distribution**: Remote actors, clustering, sharding
- **Reactive Streams**: Backpressure-aware stream processing
- **Event Sourcing**: Persistence with journals and snapshots

### Testing Patterns
- Inherit from `AkkaSpec` or use `TestKit` for actor tests
- Use `TestProbe` for creating lightweight test actors
- Use `EventFilter` for asserting log messages
- Pass `ITestOutputHelper output` to test constructors for debugging
- Multi-node tests use separate projects (*.Tests.MultiNode.csproj)

## Code Style and Conventions

### C# Style
- Allman style braces (opening brace on new line)
- 4 spaces indentation, no tabs
- Private fields prefixed with underscore `_fieldName`
- Use `var` when type is apparent
- Default to `sealed` classes and records
- Enable `#nullable enable` in new/modified files
- Never use `async void`, `.Result`, or `.Wait()`
- Always pass `CancellationToken` through async call chains

### API Design
- Maintain compatibility with JVM Akka while being .NET idiomatic
- Use `Task<T>` instead of Future, `TimeSpan` instead of Duration
- Extend-only design - don't modify existing public APIs
- Preserve wire format compatibility for serialization
- Include unit tests with all changes

### Test Naming
- Use `DisplayName` attribute for descriptive test names
- Follow pattern: `Should_ExpectedBehavior_When_Condition`

## Development Workflow

### Git Branches
- **`dev`** - Main development branch (default for PRs)
- **`v1.4`**, **`v1.3`**, etc. - Version maintenance branches for older releases
- Feature branches: `feature/description`
- Bugfix branches: `fix/description`

### Making Changes
1. Always read existing code patterns in the module you're modifying
2. Follow existing conventions for that specific module
3. Add/update tests for your changes
4. Run incremental tests before committing
5. Ensure API compatibility tests pass for core changes

### Target Frameworks
- **.NET 8.0** - Primary target
- **.NET 6.0** - Library compatibility
- **.NET Framework 4.8** - Legacy support
- **.NET Standard 2.0** - Library compatibility

## Important Files
- `Directory.Build.props` - MSBuild properties, package versions
- `global.json` - .NET SDK version (8.0.403)
- `xunit.runner.json` - Test configuration (60s timeout, no parallelization)
- `.incrementalist/*.json` - Incremental build configurations
- `RELEASE_NOTES.md` - Version history and changelog