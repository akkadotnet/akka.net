# V2 port test kit

Test helpers for a serializer port (#8675, #8714): a legacy serializer gets a V2 twin at id + 40, with the same manifests.

## Writing a port spec

1. Derive from `V2PortSpec` and implement `CreateLegacy`, `CreateV2` and `Cases` (one `V2PortCase` per manifest, with
   fixed values: no `Guid.NewGuid()`, no `DateTime.Now`). Pass `output` (and a `Config` or `ActorSystemSetup`) to the base
   constructor. Both serializers must be registered as a deployed node has them: a module's table, with the V2 row
   read-only (no bindings). Serializers outside a module table need a `SerializationSetup`.
2. Put the spec in `[Collection(DynamicTypeLoadingCollection.Name)]`. One check flips a process-wide switch.
3. For interop, `MixedBindingPair.Create(name, v2Config, legacyConfig)` starts one V2 node and one legacy node, and
   `AssertRoundTripsBothWays(cases, v2Id, legacyId)` checks both directions. `MixedBindingPair.BindTo` builds bindings.

The base class checks the id (legacy + 40, inside 40-79), manifest parity, V2 and legacy round trips, both ids decoding,
the binding staying legacy, the V2 id resolving with dynamic type loading off, and the golden snapshots below.

## Golden snapshots

The two golden tests write a hex dump of each case (`HexDumpFormatter`) and check it with [Verify](https://github.com/VerifyTests/Verify),
as `Akka.Serialization.V2.Tests/WireSnapshots` does. Files land next to the derived spec, in `Snapshots/`:
`<Spec>.<case>.legacy.verified.txt` and `<Spec>.<case>.v2.verified.txt`. Override `SnapshotPrefix` to change `<Spec>`.

First run, or a changed format: the test fails and writes `<name>.received.txt` (gitignored) beside the `.verified.txt`.
Read the diff, then accept by moving received over verified, the same step `WireSnapshots/README.md` uses:

```bash
for f in <spec folder>/Snapshots/*.received.txt; do mv "$f" "${f%.received.txt}.verified.txt"; done
```

1. **Capture the legacy snapshots first, before you touch the legacy serializer**, and commit them. They prove today's bytes still decode.
   ```bash
   dotnet test src/core/<TestProject> -c Release --filter "DisplayName=Should_MatchGoldenLegacyBytes_When_LegacySerializes"
   ```
2. **Capture the V2 snapshots** once the V2 serializer works. Read them before you commit: a shipped format is the wire format.
   ```bash
   dotnet test src/core/<TestProject> -c Release --filter "DisplayName=Should_MatchGoldenV2Bytes_When_V2Serializes"
   ```
3. Run both without accepting anything. They must pass.

Rules: never edit a `.verified.txt` by hand to make a test pass. If a legacy snapshot changes in `git diff`, stop: the
legacy format must never change. A shipped V2 format only grows (add fields, never change or reuse them). CI only compares;
a missing or stale snapshot fails the test.
