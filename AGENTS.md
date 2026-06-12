# AGENTS.md

This file provides guidance to Claude Code (claude.ai/code) and other coding agents when
working with code in this repository. `CLAUDE.md` is a symlink to this file, so both names
resolve to the same guidance.

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

### API Approvals
- Run API approval tests when making public API changes: `dotnet test -c Release src/core/Akka.API.Tests`
- Approval files live at `src/core/Akka.API.Tests/CoreAPISpec.ApproveCore.approved.txt` (and sibling `*.approved.txt` files)
- A diff viewer (WinMerge, TortoiseMerge, etc.) makes reviewing/approving API changes easier
- Follow **extend-only** design — don't modify existing public APIs, only extend them
- Mark deprecated APIs with `[Obsolete("Obsolete since v{current-akka-version}")]`

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
- **`/src/**/*.Tests/`** - xUnit test projects
- **`/docs/`** - Public-facing documentation; contributor policies and style guides live under `docs/community/contributing/`

### Key Architectural Concepts
- **Actor Model**: Message-driven, hierarchical supervision, location transparency
- **Fault Tolerance**: Supervision strategies, let-it-crash philosophy
- **Distribution**: Remote actors, clustering, sharding
- **Reactive Streams**: Backpressure-aware stream processing
- **Event Sourcing**: Persistence with journals and snapshots

## Code Style and Conventions

### C# Style
- Allman style braces (opening brace on new line)
- 4 spaces indentation, no tabs
- Private fields prefixed with underscore `_fieldName`; PascalCase for public/protected members
- Use `var` when the type is apparent
- No `this.` qualifier unless necessary
- Sort `using` statements with `System.*` first
- XML doc comments on public APIs
- Default to `sealed` classes and records
- Enable `#nullable enable` in new/modified files
- Never use `async void`, `.Result`, or `.Wait()` — these cause deadlocks
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

### General Conventions
- Keep pull requests small and focused (< 300 lines when possible)
- Fix warnings instead of suppressing them
- Treat `TBD` comments as action items to be resolved
- Benchmark performance-critical changes with BenchmarkDotNet
- Avoid adding new dependencies without a license/security check

## Akka.NET TestKit Guidelines
- Actor tests should derive from `AkkaSpec` or `TestKit` to access actor-testing facilities
- **Always use async TestKit methods** (e.g. `ExpectMsgAsync`, `ExpectNoMsgAsync`, `AwaitAssertAsync`, `FishForMessageAsync`, `ResolveOne`) — never the synchronous variants (`ExpectMsg`, `ExpectNoMsg`, `AwaitAssert`, `.Result`, `.Wait()`)
- Pass `ITestOutputHelper output` to the test constructor and forward it to the base: `public MySpec(ITestOutputHelper output) : base(config, output)` — this captures all test output, including actor-system logs
- Configure logging in tests as needed: `akka.loglevel = DEBUG` or `akka.loglevel = INFO`
- Use `EventFilter` to assert on log messages (e.g. `await EventFilter.Error().ExpectOneAsync(async () => { /* test code */ })`)
- For dead letters, use `EventFilter.DeadLetter()` (e.g. `await EventFilter.DeadLetter().ExpectAsync(1, async () => { /* code that should dead-letter */ })`)
- Use `TestProbe` for lightweight test actors to verify interactions
- Set explicit timeouts on message expectations to avoid long-running tests
- Tests should clean up after themselves (stop created actors, reset state)
- Multi-node tests live in separate `*.Tests.MultiNode.csproj` projects
- To verify specialized message wrappers, check the log form `wrapped in [$TypeName]`

## Development Workflow

### Git Branches
- **`dev`** - Main development branch (default for PRs)
- **`v1.4`**, **`v1.3`**, etc. - Version maintenance branches for older releases
- Feature branches: `feature/description`
- Bugfix branches: `fix/description`

### Git Repository Management
- Remotes:
  - `akkadotnet` / `upstream` → `https://github.com/akkadotnet/akka.net.git` (main repository)
  - `origin` → your fork (e.g. `https://github.com/yourusername/akka.net.git`)
- Sync with upstream:
  - `git fetch akkadotnet` (or `upstream`)
  - `git checkout dev`
  - `git merge akkadotnet/dev`
- Create a feature branch:
  - `git checkout -b feature/your-feature-name`
  - `git push -u origin feature/your-feature-name`

### Making Changes
1. Always read existing code patterns in the module you're modifying
2. Follow existing conventions for that specific module
3. Add/update tests for your changes
4. Run incremental tests before committing
5. Ensure API compatibility tests pass for core changes
6. If the change is breaking, record it in `BREAKING_CHANGES_V1.6.md` in the same change (see below)

### Tracking Breaking Changes (v1.6 cycle)
Until a stable **v1.6.0** ships, **every** change that goes into the `dev` branch and
introduces a **breaking behavior, wire-format, or public-API change** MUST be documented in
[`BREAKING_CHANGES_V1.6.md`](BREAKING_CHANGES_V1.6.md) (repo root), in the **same PR** that
makes the change. Use the entry format described in that file (status, component, type,
change, migration). This ledger is retired once `v1.6.0` is released, when its contents are
folded into the release notes / upgrade guide.

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
- `BREAKING_CHANGES_V1.6.md` - Running list of v1.6 breaking changes (until v1.6.0 ships)
