# V2 port test kit

Shared checks for a serializer port: a legacy serializer gets a V2 twin at id + 40 with the same manifests.
Part of #8675 and #8714. Test-only; nothing here ships.

## How a port uses it

1. **Capture the legacy bytes first**, before you change anything. Build a `V2PortCase` list (message plus its
   legacy manifest, deterministic values only), then run a spec that calls
   `V2PortSpecs.AssertLegacyBytesMatchGolden(...)` with `AKKA_GOLDEN_CAPTURE=legacy`. It needs only the legacy
   serializer. Commit the `.hex` files in their own commit.
2. **Write the V2 serializer**, then derive from `V2PortSpec` (see `Akka.Tests/Serialization/V2PortKit/FakeV2PortSpec.cs`).
   You supply `CreatePort(system)` (legacy factory, V2 factory, corpus), plus a `LegacyGolden` and a `V2Golden`
   folder from `GoldenBytes.For("GoldenBytes/X", GoldenKind.Legacy | GoldenKind.V2)`.
3. **Capture the V2 bytes** with `AKKA_GOLDEN_CAPTURE=v2` and review them in `git diff`. A normal run only compares,
   and fails with a hex diff when the wire changes.
4. **Check mixed nodes** with `MixedBindingPair.Create(name, v2Config, legacyConfig)`, then
   `pair.AssertRoundTripsBothWays(cases, v2Id, legacyId)`. Before the global switch (#8713), pass
   `MixedBindingPair.BindTo("x-v2", types)` to the V2 side and `BindTo("x", types)` to the legacy side. After it, pass
   the switch's config to the V2 side. The helper does not know the switch's key name.
5. **Persistence or Delivery ports:** use `WireFormatJournal.Config` (a journal that stores bytes, unlike `inmem`) and
   `ReliableDeliveryExchange.RunAsync(this, messages)` under `akka.actor.serialize-messages = on`.

## What `V2PortSpec` checks

| Test | Check |
|---|---|
| `Should_UseReservedIdBlock_When_PortIsDeclared` | V2 id = legacy id + 40, inside 40-79 (`V2Port.IdOffset`, `V2IdMin`, `V2IdMax`) |
| `Should_ReuseLegacyManifests_When_V2SerializesCorpus` | manifest parity |
| `Should_RoundTripEveryManifest_When_V2Serializes` | `ToBinary`, `Serialize` and `Deserialize` agree; `SizeHint` exact |
| `Should_RoundTripEveryManifest_When_LegacySerializes` | legacy still works |
| `Should_DecodeBothIds_When_NodeHoldsBothSerializers` | `Serialization.Deserialize(bytes, id, manifest)` for both ids |
| `Should_ResolveExpectedBinding_When_FindingSerializerForCorpus` | `FindSerializerFor*` gives legacy while the V2 row is read-only (`V2Port.ExpectedBinding`) |
| `Should_ResolveV2Id_When_DynamicTypeLoadingIsOff` | V2 id resolves and round trips with `Akka.DynamicTypeLoading` off |
| `Should_MatchGoldenV2Bytes_When_V2Serializes` | V2 bytes unchanged |
| `Should_MatchGoldenLegacyBytes_When_LegacySerializes` | legacy bytes unchanged (`V2Port.CompareLegacyBytes = false` for non-deterministic output) |
| `Should_DecodeGoldenLegacyBytes_When_V2CapableNodeReads` | the V2-capable node decodes the pre-port legacy bytes |

Every check is also a method on `V2PortSpecs`, for ports that need another shape.

## Notes

- The dynamic-type-loading check flips a process-wide AppContext switch. Put the spec in a collection that does not run
  beside others (`[Collection(DynamicTypeLoadingCollection.Name)]` in `Akka.Tests`).
- Golden files sit next to the spec's source (found with `[CallerFilePath]`), so a spec reads and writes the working tree.
- Corpus messages must be deterministic. Give a `V2PortCase` a name when two cases share a manifest.
