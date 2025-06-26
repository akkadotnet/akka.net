# Akka.NET Agent Guidelines

## Build/Test Commands
- Build solution: `dotnet build`
- Build with warnings as errors: `dotnet build -warnaserror`
- Run all tests: `dotnet test -c Release` 
- Run specific test: `dotnet test -c Release --filter DisplayName="TestName"` or `dotnet test path/to/project.csproj`
- Format check: `dotnet format --verify-no-changes`

## Git Repository Management
- Setup remotes:
  - `git remote add upstream https://github.com/akkadotnet/akka.net.git` (main repository)
  - `git remote add origin https://github.com/yourusername/akka.net.git` (your fork)
- Sync with upstream:
  - `git fetch upstream` (get latest changes from main repo)
  - `git checkout dev` (switch to dev branch)
  - `git merge upstream/dev` (merge changes from upstream)
  - `git push origin dev` (update your fork)
- Create feature branch:
  - `git checkout -b feature/your-feature-name` (create and switch to new branch)
  - `git push -u origin feature/your-feature-name` (push branch to your fork)

## Code Style Guidelines
- Use Allman style brackets for C# code (opening brace on new line)
- 4 spaces for indentation
- Prefer "var" everywhere when type is apparent
- Private fields start with `_` (underscore), PascalCase for public/protected members
- No "this." qualifier when unnecessary
- Use exceptions for error handling (IllegalStateException for invalid states)
- Sort using statements with System.* appearing first
- XML comments for public APIs
- Name tests with descriptive `DisplayName=` attributes
- Default to `sealed` classes and records for data objects
- Enable nullability in new/modified files with `#nullable enable`
- Never use `async void`, `.Result`, or `.Wait()` - these cause deadlocks
- Always pass `CancellationToken` in async methods

## API Approvals
- Run API approval tests when making public API changes: `dotnet test -c Release src/core/Akka.API.Tests`
- Approval files are located at `src/core/Akka.API.Tests/CoreAPISpec.ApproveCore.approved.txt`
- Install a diff viewer like WinMerge or TortoiseMerge to approve API changes
- Follow extend-only design principles - don't modify existing public APIs, only extend them
- Mark deprecated APIs with `[Obsolete("Obsolete since v{current-akka-version}")]`

## Conventions
- Stay close to JVM Akka where applicable but be .NET idiomatic
- Use Task<T> instead of Future, TimeSpan instead of Duration
- Include unit tests with changes
- Preserve public API and wire compatibility
- Keep pull requests small and focused (<300 lines when possible)
- Fix warnings instead of suppressing them
- Treat TBD comments as action items to be resolved
- Benchmark performance-critical code changes with BenchmarkDotNet
- Avoid adding new dependencies without license/security checks

## Akka.NET TestKit Guidelines
- Actor tests should derive from `AkkaSpec` or `TestKit` to access actor testing facilities
- Pass `ITestOutputHelper output` to the constructor and base constructor: `public MySpec(ITestOutputHelper output) : base(config, output)`
- Use the `ITestOutputHelper` output for debugging: it captures all test output including actor system logs
- Configure proper logging in tests: `akka.loglevel = DEBUG` or `akka.loglevel = INFO`
- Use `EventFilter` to assert on log messages (e.g., `EventFilter.Error().ExpectOne(() => { /* test code */ });`)
- For testing deadletters, use `EventFilter.DeadLetter().Expect(1, () => { /* code that should produce dead letter */ });`
- Test message assertions using `ExpectMsg<T>()`, `ExpectNoMsg()`, or `FishForMessage<T>()`
- Set explicit timeouts for message expectations to avoid long-running tests
- Use `TestProbe` to create lightweight test actors to verify interactions
- Tests should clean up after themselves (stop created actors, reset state)
- To test specialized message types, verify the type wrapper in logs: `wrapped in [$TypeName]`

## Repository Landmarks
- `src/` - All runtime / library code
- `src/benchmark/` - Micro-benchmarks (BenchmarkDotNet)
- `src/…Tests/` - xUnit test projects
- `docs/community/contributing/` - Contributor policies & style guides
- `docs/` - Public facing documentation