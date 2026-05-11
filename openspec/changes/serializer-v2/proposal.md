## Why

Akka.NET's current serialization API (`Serializer` / `SerializerWithStringManifest`) is `byte[]`-based. Every serialization produces a `byte[]` allocation, and every deserialization requires a `byte[]` input. That makes it impossible for the remoting transport to integrate serialization directly into its outbound framing loop.

PR #8203 showed that serializer improvements are meaningful, but also that `SerializerV2` by itself is not the full win. The real architectural target is an outbound remoting path where the transport-owned writer loop can ask a serializer to write directly into a destination it already owns. `SerializerV2` is therefore a foundation for the transport redesign, not an end-state on its own.

Read-side buffer pooling remains explicitly out of scope. The V2 read API exists so deserializers can consume `ReadOnlySequence<byte>` efficiently, but inbound payload bytes are still copied before they cross actor-visible lifetime boundaries.

## What Changes

- **New `SerializerV2` base class in core Akka** — independent (does NOT extend `Serializer`), codec-agnostic, uses `IBufferWriter<byte>` / `ReadOnlySequence<byte>` as primary API, with `ToBinary()` / `FromBinary()` bridge methods for backward compat
- **New `SerializerV1Adapter : SerializerV2`** in core Akka — wraps legacy `Serializer` / `SerializerWithStringManifest` to participate in V2 infrastructure
- **New `Akka.Serialization.V2` NuGet package** — `MessagePackSerializer : SerializerV2` base class, sealed `AkkaWriter` / `AkkaReader` wrapping MessagePack, attributes (`[AkkaSerializable]`, `[AkkaField]`, `[AkkaSerializer]`)
- **Modify `Serialization.cs`** — use `SerializerV2` as internal storage type, auto-wrap V1 serializers in `SerializerV1Adapter`, `FindSerializerFor()` returns `SerializerV2`
- **Modify `MessageSerializer.cs`** — use V2 dispatch (call `Manifest()` directly, no `is SerializerWithStringManifest` type check)
- **Mechanical port of simple internal Protobuf serializers** — change base class to `SerializerV2`, use `proto.WriteTo(IBufferWriter<byte>)` / `Parser.ParseFrom(ReadOnlySequence<byte>)`. Same serializer IDs, same wire format.
- **Transport integration deferred behind a spike** — the production remoting write path will only adopt direct `Serialize(IBufferWriter<byte>, object)` once the integrated outbound pipeline has been benchmarked and selected as the transport direction
- **Source generator deferred** — validate API with hand-written serializers first

### What does NOT change

- The `Serializer` and `SerializerWithStringManifest` classes remain in the codebase for backward compat (wrapped in adapter)
- Wire format for existing serializers (same Protobuf bytes, same serializer IDs, same manifests)
- HOCON serializer registration (`akka.actor.serializers`, `akka.actor.serialization-bindings`, `akka.actor.serialization-identifiers`)
- `SerializationSetup` programmatic registration API
- Persistence data compatibility (journals store serializerId + manifest + payload — V1 data readable forever)
- Read-side remoting semantics: inbound bytes are not leased across actor boundaries

## Capabilities

### New Capabilities

- `serializer-v2-base`: The `SerializerV2` base class with `IBufferWriter<byte>` / `ReadOnlySequence<byte>` API, `SerializerV1Adapter`, and infrastructure changes to `Serialization.cs` and `MessageSerializer.cs`. Lives in core Akka.
- `messagepack-serializer`: The `MessagePackSerializer : SerializerV2` intermediate class, sealed `AkkaWriter`/`AkkaReader` wrapping MessagePack, and serializable type attributes. Lives in `Akka.Serialization.V2` package.

### Modified Capabilities

## Impact

- **Akka core** (`src/core/Akka/Serialization/`): New `SerializerV2.cs`, `SerializerV1Adapter.cs`. Modified `Serialization.cs` (internal storage type, auto-wrapping), `MessageSerializer.cs` (V2 dispatch).
- **Akka.Remote**: `MessageSerializer.cs` simplified — calls `Manifest()` directly. The production transport path may later call `serializer.Serialize(FrameBufferWriter)` directly, but that integration is intentionally sequenced after the outbound write-loop spike.
- **New package**: `Akka.Serialization.V2` with `MessagePackSerializer`, `AkkaWriter`, `AkkaReader`, attributes.
- **NuGet dependencies**: `MessagePack` added to `Akka.Serialization.V2` (not core Akka). `System.IO.Pipelines` / `System.Memory` already in core from Spec 1.
- **Internal serializers**: Mechanical base class change for simple ones (same wire format). Complex ones with nested payloads via `WrappedPayloadSupport` remain deferred until the transport integration point is proven.
- **API surface**: `FindSerializerFor()` return type changes from `Serializer` to `SerializerV2`.
- **Benchmarks**: serializer changes are now paired with a transport-owned outbound writer spike, not judged purely in isolation.
- **Test suites**: All existing serialization tests must pass (V1 serializers auto-wrapped transparently). New tests for V2 round-trip, adapter, and hand-written MessagePack serializers.
