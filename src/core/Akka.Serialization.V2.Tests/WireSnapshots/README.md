# Wire-Format Snapshots

This folder holds **committed wire-format artifacts** for Akka.Serialization.V2. Each
`<case>.verified.txt` file is a human-reviewable annotated hex dump of the exact MessagePack
bytes a hardcoded, deterministic message serializes to today. They are asserted against by
[`WireFormatSnapshotSpec`](../WireFormatSnapshotSpec.cs) using
[Verify](https://github.com/VerifyTests/Verify) (`Verify.XunitV3`).

## Why this exists

Unit tests that round-trip a message (serialize, then deserialize, then assert equality) do not
catch every wire-format regression: a change that reorders fields, alters MessagePack framing, or
shifts an encoding convention can still round-trip correctly through the *same* build while
silently breaking compatibility with every *other* build already deployed. These snapshots pin the
literal bytes, so any change to the wire format -- intentional or not -- fails a test and shows an
exact byte-level diff in the pull request, independent of whether the change happens to round-trip.

This gate is deliberately broader than the inline `GOLDEN:` byte-array assertions already present
in specs such as `CollectionFieldSpec` and `ImmutableCollectionFieldSpec` (which hand-pin a
handful of illustrative shapes close to the code they guard). This corpus's job is breadth: one
snapshot per structurally distinct shape the generator supports, in one place, so a reviewer can
scan the diff stat of a PR and see exactly which wire shapes moved.

It is also intended to anchor the eventual protobuf-to-MessagePack migration's byte-golden tests:
the same annotated-hex-dump format and update procedure apply there.

## File format

Each file is plain UTF-8 text:

```
case: <case-name>
message-type: <friendly CLR type name>
manifest: <serializer manifest string>
serializer-id: <Akka serializer id>
byte-count: <n>

<offset>  <16 space-separated hex byte pairs, split at the 8-byte midpoint>  |<ASCII, '.' for non-printable>|
...
```

Offsets and byte columns follow the conventional hex-dump layout (16 bytes per row). The header
block (`case`/`message-type`/`manifest`/`serializer-id`/`byte-count`) is what actually makes a
diff reviewable: a change that only reorders fields, for example, shows up as a byte-level diff
under an unchanged header, while a change that (for instance) bumps a manifest string shows up in
the header line itself.

## Update procedure

1. Make the wire-format change.
2. Run the spec: `dotnet test src/core/Akka.Serialization.V2.Tests -c Release --filter WireFormatSnapshotSpec`.
   Every case whose bytes changed fails and writes a `<case>.received.txt` file next to the
   `.verified.txt` it disagreed with (`.received.txt` is gitignored -- it is a scratch/diff
   artifact, never committed).
3. **Read the diff.** The failure output (or a diff of `<case>.received.txt` against
   `<case>.verified.txt`) shows exactly which bytes moved. Confirm the change is the one you
   intended -- this is the entire point of the gate.
4. Approve by replacing the verified file with the received one, for every case that should
   change:
   ```bash
   for f in src/core/Akka.Serialization.V2.Tests/WireSnapshots/*.received.txt; do
     mv "$f" "${f%.received.txt}.verified.txt"
   done
   ```
5. Re-run the spec to confirm it passes, then commit the updated `.verified.txt` file(s) as part
   of the same PR that made the wire-format change, so the reviewer sees the byte diff alongside
   the code diff.

CI only ever asserts against the committed `.verified.txt` files -- it never generates or
approves them. A missing or stale snapshot is a failing test, not a silently-regenerated file.

## Hash-ordering exclusion (`ImmutableHashSet<T>` / `ImmutableDictionary<TKey,TValue>`)

`ImmutableHashSet<T>` and `ImmutableDictionary<TKey,TValue>` iterate in hash-bucket order, which is
**not guaranteed stable across .NET runtimes, versions, or even process runs** for some key/element
types. A multi-element instance of either kind can legally serialize to a different byte sequence
on a different machine while still being semantically identical (same set/map contents). Snapshot
tests must be able to run identically on any contributor's or CI machine, so this corpus
deliberately restricts `ImmutableHashSet<T>`/`ImmutableDictionary<TKey,TValue>` cases to
**single-element instances** (see `immutable-hashset-single-element` and
`immutable-dictionary-single-entry`), where there is only one possible iteration order and the byte
sequence is therefore stable. Multi-element hash-ordering round-trip coverage (by value, not by
byte) already exists in `ImmutableCollectionFieldSpec`
(`Should_round_trip_multi_element_immutable_hashset_by_value` and
`Should_round_trip_multi_entry_immutable_dictionary_by_value`); it is intentionally not duplicated
here.

Every other collection shape in this corpus (`T[]`, `List<T>`, `IReadOnlyList<T>`,
`ImmutableArray<T>`, `ImmutableList<T>`, `IReadOnlyCollection<T>`) is sequence-typed and formally
preserves declaration order, so multi-element instances are always safe to byte-snapshot.

`Dictionary<TKey,TValue>` (`collection-dictionary-non-string-key`, three entries) is a partial
exception worth calling out explicitly: the BCL does not formally contract enumeration order for
`Dictionary<TKey,TValue>`, but CoreCLR's implementation is well known to enumerate small,
never-had-a-removal dictionaries in insertion order, and has been stable across every .NET Core /
.NET 5+ release -- a materially different reliability profile than `ImmutableHashSet`/
`ImmutableDictionary`'s hash-bucket order, which varies with element/key hash codes rather than
insertion sequence. If this ever proves too fragile in practice (for example after a future BCL
change), narrow that one case to a single entry the same way the two `Immutable*` cases already
are -- the round-trip-by-value coverage in `CollectionFieldSpec` remains the source of truth for
multi-entry `Dictionary` semantics either way.
