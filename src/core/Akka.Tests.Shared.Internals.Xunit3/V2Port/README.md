# V2 port test kit

Test helpers for a serializer port (#8675, #8714): a legacy serializer gets a V2 twin at id + 40, with the same manifests.
See `Akka.Tests/Serialization/V2PortKit` for a worked example.

## Writing a port spec

1. Derive from `V2PortSpec` and implement `CreateLegacy`, `CreateV2`, `Cases` (one `V2PortCase` per manifest, with
   fixed values: no `Guid.NewGuid()`, no `DateTime.Now`) and `Golden` (`GoldenBytes.For("GoldenBytes/<Serializer>")`).
   A real port registers both serializers through its module's C# table, with the V2 row read-only (no bindings), so
   the spec needs no extra config. The worked example's fake serializers aren't in a module table, so it passes a
   `SerializationSetup` through the `V2PortSpec(ActorSystemSetup, ITestOutputHelper)` constructor.
2. Put the spec in `[Collection(DynamicTypeLoadingCollection.Name)]`. One check flips a process-wide switch, so the
   spec must not run in parallel with others.
3. For interop, `MixedBindingPair.Create(name, v2Config, legacyConfig)` starts one V2 node and one legacy node, and
   `AssertRoundTripsBothWays(cases, v2Id, legacyId)` checks both directions. Use `MixedBindingPair.BindTo("alias", types)`
   for explicit bindings.

`V2PortSpec` checks that the id is legacy + 40 inside 40-79, manifest parity, V2 and legacy round trips, that both ids
decode, that the binding stays legacy while the V2 row is read-only, that the V2 id resolves with dynamic type loading
off, and golden V2 and legacy bytes.

## Golden capture

Golden files pin the exact bytes each serializer writes, one `.hex` file per case, 16 bytes per row. They live next to
the spec's source file: `GoldenBytes.For("GoldenBytes/Foo")` in `FooSpec.cs` reads `GoldenBytes/Foo/legacy/<case>.hex`
and `GoldenBytes/Foo/v2/<case>.hex` in the same folder.

A normal run only compares. Setting `AKKA_GOLDEN_CAPTURE=1` makes the golden tests write the files instead of comparing.

**1. Capture the legacy bytes first, before you touch the legacy serializer.** These prove that today's bytes still
decode after the port.

```bash
AKKA_GOLDEN_CAPTURE=1 dotnet test src/core/<TestProject> -c Release \
  --filter "DisplayName~Should_MatchGoldenLegacyBytes"
git add <spec folder>/GoldenBytes/<Serializer>/legacy
git commit -m "Capture legacy golden bytes for <Serializer>"
```

**2. Capture the V2 bytes once the V2 serializer works.**

```bash
AKKA_GOLDEN_CAPTURE=1 dotnet test src/core/<TestProject> -c Release \
  --filter "DisplayName~Should_MatchGoldenV2Bytes"
git diff --stat   # only v2/*.hex files should appear
```

Read the new files before you commit them. They become the wire format once a release ships them.

**3. Run without the variable.** Every golden test must pass.

```bash
dotnet test src/core/<TestProject> -c Release --filter "DisplayName~Should_MatchGolden"
```

### Rules

- Never set `AKKA_GOLDEN_CAPTURE` in CI or in committed config. Capture overwrites files silently.
- Capture with a filter, as above, so only the files you mean to change get written. Running without a filter rewrites
  both the legacy and the V2 files.
- When a golden test fails, it prints the expected and actual rows side by side and marks the rows that differ with `!`.
  If you didn't mean to change the format, fix the serializer, not the file. A shipped V2 format can only grow: add
  fields, never change or reuse existing ones.
- If a legacy file changes in `git diff`, stop. The legacy format must never change.
- The files are found through the spec's source path (`[CallerFilePath]`), so run the tests from a checkout of the
  repo, not from copied binaries.
