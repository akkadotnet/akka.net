## Why

Akka.NET's current serialization API (`Serializer` / `SerializerWithStringManifest`) is `byte[]`-based. Every serialization produces a `byte[]` allocation, and every deserialization requires a `byte[]` input. With the new Akka.Streams transport (Spec 3) using `IBufferWriter<byte>` for writes and `ReadOnlySequence<byte>` for reads, the serializer must speak the same language to achieve zero-copy end-to-end. The V2 API makes `SerializerV2` the new foundation — V1 serializers are adapted to it, not the reverse. A POC at github.com/Aaronontheweb/AkkaSerializationPoC (PR #42) has validated the MessagePack-based approach with 22% faster deserialization than the interface-based alternative.

## What Changes

- **New `SerializerV2` base class in core Akka** — independent (does NOT extend `Serializer`), codec-agnostic, uses `IBufferWriter<byte>` / `ReadOnlySequence<byte>` as primary API, with `ToBinary()` / `FromBinary()` bridge methods for backward compat
- **New `SerializerV1Adapter : SerializerV2`** in core Akka — wraps legacy `Serializer` / `SerializerWithStringManifest` to participate in V2 infrastructure
- **New `Akka.Serialization.V2` NuGet package** — `MessagePackSerializer : SerializerV2` base class, sealed `AkkaWriter` / `AkkaReader` wrapping MessagePack, attributes (`[AkkaSerializable]`, `[AkkaField]`, `[AkkaSerializer]`)
- **Modify `Serialization.cs`** — use `SerializerV2` as internal storage type, auto-wrap V1 serializers in `SerializerV1Adapter`, `FindSerializerFor()` returns `SerializerV2`
- **Modify `MessageSerializer.cs`** — use V2 dispatch (call `Manifest()` directly, no `is SerializerWithStringManifest` type check)
- **Mechanical port of simple internal Protobuf serializers** — change base class to `SerializerV2`, use `proto.WriteTo(IBufferWriter<byte>)` / `Parser.ParseFrom(ReadOnlySequence<byte>)`. Same serializer IDs, same wire format.
- **Source generator deferred** — validate API with hand-written serializers first

### What does NOT change

- The `Serializer` and `SerializerWithStringManifest` classes remain in the codebase for backward compat (wrapped in adapter)
- Wire format for existing serializers (same Protobuf bytes, same serializer IDs, same manifests)
- HOCON serializer registration (`akka.actor.serializers`, `akka.actor.serialization-bindings`, `akka.actor.serialization-identifiers`)
- `SerializationSetup` programmatic registration API
- Persistence data compatibility (journals store serializerId + manifest + payload — V1 data readable forever)

## Capabilities

### New Capabilities

- `serializer-v2-base`: The `SerializerV2` base class with `IBufferWriter<byte>` / `ReadOnlySequence<byte>` API, `SerializerV1Adapter`, and infrastructure changes to `Serialization.cs` and `MessageSerializer.cs`. Lives in core Akka.
- `messagepack-serializer`: The `MessagePackSerializer : SerializerV2` intermediate class, sealed `AkkaWriter`/`AkkaReader` wrapping MessagePack, and serializable type attributes. Lives in `Akka.Serialization.V2` package.

### Modified Capabilities

## Impact

- **Akka core** (`src/core/Akka/Serialization/`): New `SerializerV2.cs`, `SerializerV1Adapter.cs`. Modified `Serialization.cs` (internal storage type, auto-wrapping), `MessageSerializer.cs` (V2 dispatch).
- **Akka.Remote**: `MessageSerializer.cs` simplified — calls `Manifest()` directly. In Spec 3, `EndpointWriter` uses `serializer.Serialize(FrameBufferWriter)` directly.
- **New package**: `Akka.Serialization.V2` with `MessagePackSerializer`, `AkkaWriter`, `AkkaReader`, attributes.
- **NuGet dependencies**: `MessagePack` added to `Akka.Serialization.V2` (not core Akka). `System.IO.Pipelines` / `System.Memory` already in core from Spec 1.
- **Internal serializers**: Mechanical base class change for simple ones (same wire format). Complex ones with nested payloads via `WrappedPayloadSupport` deferred until V2 API is validated.
- **API surface**: `FindSerializerFor()` return type changes from `Serializer` to `SerializerV2`.
- **Test suites**: All existing serialization tests must pass (V1 serializers auto-wrapped transparently). New tests for V2 round-trip, adapter, and hand-written MessagePack serializers.
