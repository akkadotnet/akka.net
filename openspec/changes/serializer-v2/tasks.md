## 1. SerializerV2 API

- [ ] 1.1 Create `SerializerV2` in `src/core/Akka/Serialization/SerializerV2.cs`
- [ ] 1.2 Define buffer-first serialization API using `IBufferWriter<byte>`
- [ ] 1.3 Define `ReadOnlySequence<byte>` deserialization API
- [x] 1.4 Define direct manifest API for all V2 serializers
- [ ] 1.5 Define `SizeHint` with explicit unknown-size support
- [ ] 1.6 Decide and implement bytes-written/result reporting for `Serialize`
- [ ] 1.7 Decide sync vs async V2 API shape before downstream sourcegen and Artery work
- [ ] 1.8 Add `ToBinary` / `FromBinary` bridge methods for compatibility paths
- [ ] 1.9 Add unit tests for native V2 serializer round-trip, manifest, size hint, and bridge methods
- [x] 1.10 Require new native V2 serializers to emit non-empty non-CLR manifests; allow legacy serializer-id ports and `SerializerV1Adapter` to preserve empty legacy manifests

## 2. SerializerV1Adapter

- [ ] 2.1 Create `SerializerV1Adapter : SerializerV2`
- [ ] 2.2 Preserve inner serializer `Identifier`
- [ ] 2.3 Preserve `SerializerWithStringManifest.Manifest` behavior
- [ ] 2.4 Preserve `IncludeManifest` type-manifest behavior for non-string-manifest V1 serializers
- [ ] 2.5 Delegate V1 serialization to `ToBinary`
- [ ] 2.6 Delegate V1 deserialization to `FromBinary`
- [ ] 2.7 Preserve `Serialization.CurrentTransportInformation` behavior
- [ ] 2.8 Expose `Inner` for migration and tests
- [ ] 2.9 Add adapter tests for built-in V1 serializers and custom V1 serializers

## 3. Serialization.cs V2-First Infrastructure

- [ ] 3.1 Change serializer ID map to store `SerializerV2`
- [ ] 3.2 Change serializer type map to store `SerializerV2`
- [ ] 3.3 Wrap HOCON-registered V1 serializers in `SerializerV1Adapter`
- [ ] 3.4 Store native V2 serializers directly
- [ ] 3.5 Update `SerializationSetup` handling for V1 and V2 serializers
- [x] 3.6 Preserve public `FindSerializerFor()` compatibility and add internal V2 lookup
- [x] 3.7 Preserve public `FindSerializerForType()` compatibility and add internal V2 type lookup
- [x] 3.8 Update `Serialization.ManifestFor` or replace it with V2-first manifest dispatch
- [x] 3.9 Add internal `Serialize(object, IBufferWriter<byte>)` V2 path
- [x] 3.10 Add internal `Deserialize(ReadOnlySequence<byte>, serializerId, manifest)` V2 path
- [ ] 3.11 Update error messages for missing serializer IDs to remain compatible and actionable
- [x] 3.12 Update API approval baselines for intentional 1.6 API break

## 4. Classic Akka.Remote V2 Bridge

- [ ] 4.1 Update `src/core/Akka.Remote/MessageSerializer.cs` to use V2 serializer lookup and manifest dispatch
- [ ] 4.2 Preserve existing classic protobuf `Payload` wire format
- [ ] 4.3 Preserve existing classic deserialization behavior and error handling
- [ ] 4.4 Update `src/core/Akka.Remote/Serialization/WrappedPayloadSupport.cs` to use V2
- [ ] 4.5 Update nested payload serialization to preserve serializer ID + manifest + payload semantics
- [ ] 4.6 Keep `AkkaPduCodec` classic wire-compatible
- [ ] 4.7 Add classic remoting tests using V1 adapter payloads
- [ ] 4.8 Add classic remoting tests using native V2 payloads
- [ ] 4.9 Verify existing Akka.Remote tests pass

## 5. Akka.Persistence V2 Bridge

- [ ] 5.1 Update `PersistenceMessageSerializer.GetPersistentPayload` to use V2
- [ ] 5.2 Update `PersistenceMessageSerializer.GetPayload` to deserialize through V2
- [ ] 5.3 Update `PersistenceSnapshotSerializer` to use V2
- [ ] 5.4 Update `LocalSnapshotStore` wrapper serializer type and calls for V2
- [ ] 5.5 Preserve protobuf persistence envelope format for events and snapshots
- [ ] 5.6 Add tests proving old V1-serialized events remain readable
- [ ] 5.7 Add tests proving old V1-serialized snapshots remain readable
- [ ] 5.8 Add tests proving native V2 events persist and recover
- [ ] 5.9 Add tests proving native V2 snapshots save and load
- [ ] 5.10 Verify existing Akka.Persistence tests pass

## 6. Compile Fallout Across Repo

- [ ] 6.1 Fix DistributedData serializer call sites that store or call `Serializer`
- [x] 6.2 Fix Akka.Delivery chunk serialization call sites
- [ ] 6.3 Fix cluster and sharding serializer tests that assert concrete serializer types
- [ ] 6.4 Fix any code that type-checks against `SerializerWithStringManifest`
- [ ] 6.5 Fix any code that assumes `FindSerializerForType()` returns a V1 serializer
- [ ] 6.6 Keep V1 access possible through `SerializerV1Adapter.Inner` only where required

## 7. Optional Low-Risk Internal Serializer Ports

- [x] 7.1 Port `ByteArraySerializer` to native V2 if trivial
- [ ] 7.2 Port simple primitive serializers to native V2 if trivial
- [ ] 7.3 Port `SystemMessageSerializer` to native V2 only if compatibility tests are already in place
- [x] 7.4 Verify byte-identical output for any internal serializer ported in this milestone
- [x] 7.5 Preserve `ByteArraySerializer` legacy empty-manifest reads and writes for serializer id `4`
- [ ] 7.6 Revisit `SerializerV2 : Serializer` hierarchy before adding more native production V2 serializers

## 8. Validation

- [ ] 8.1 Run `dotnet build -warnaserror`
- [x] 8.2 Run focused Akka.Serialization tests
- [ ] 8.3 Run focused Akka.Remote tests
- [ ] 8.4 Run focused Akka.Persistence tests
- [x] 8.5 Run `dotnet test -c Release src/core/Akka.API.Tests`
- [ ] 8.6 Document any remaining V1-only compatibility boundaries for the sourcegen milestone
