## Why

Akka.NET 1.6 intentionally makes `SerializerV2` the canonical serialization abstraction. The current `Serializer` / `SerializerWithStringManifest` API is `byte[]`-based, which forces allocations on every serialization and deserialization path. That is incompatible with the larger 1.6 goals: source-generated serializers, AOT-friendly code, lower allocation remoting, and a future Artery-style transport.

This change is not only a core serialization API change. Once `Serialization.FindSerializerFor()` returns `SerializerV2`, classic Akka.Remote, Akka.Persistence, DistributedData, delivery, tests, and any caller that stores or type-checks serializers are affected immediately. Therefore this change is the atomic V2 foundation: core API plus required compatibility bridges.

## What Changes

- **New `SerializerV2` base class in core Akka** - codec-agnostic, independent from `Serializer`, and based on `IBufferWriter<byte>` / `ReadOnlySequence<byte>`.
- **New `SerializerV1Adapter : SerializerV2`** - wraps legacy `Serializer` / `SerializerWithStringManifest` implementations.
- **`Serialization.cs` becomes V2-first** - internal dictionaries store `SerializerV2`; HOCON and `SerializationSetup` V1 serializers are auto-wrapped; `FindSerializerFor()` and `FindSerializerForType()` return V2.
- **Classic remoting V2 bridge** - `Akka.Remote.MessageSerializer` and nested payload helpers use V2 while preserving the existing classic protobuf wire format.
- **Akka.Persistence V2 bridge** - persistence event and snapshot serializers use V2 while preserving existing stored event and snapshot compatibility.
- **Call-site fallout fixed in the same change** - delivery, DistributedData, tests, and other direct serializer call sites compile and preserve behavior.
- **API shape decisions are finalized before sourcegen / Artery** - bytes-written reporting, unknown `SizeHint`, manifest semantics, V1 fallback behavior, and sync/async serializer behavior are settled here.

### What Does Not Change

- Existing V1 serializer classes remain in the codebase.
- Existing V1 serializer registrations continue to work through `SerializerV1Adapter`.
- Classic Akka.Remote wire compatibility is preserved for the classic transport path.
- Existing Akka.Persistence journal events and snapshots remain readable.
- HOCON serializer registration remains supported.
- `SerializationSetup` remains supported, updated to participate in the V2-first model.
- MessagePack source generation is not implemented in this change; it is the next validation gate.
- Artery envelopes are not implemented in this change.

## Capabilities

### New Capabilities

- `serializer-v2-foundation`: V2 base API, V1 adapter, `Serialization.cs` V2-first infrastructure, classic remoting bridge, persistence bridge, and required call-site updates.

### Modified Capabilities

- `classic-remoting-serialization`: classic remoting uses V2 payload serialization while retaining classic protobuf envelope compatibility.
- `persistence-payload-serialization`: persistence payloads use V2 serialization while retaining stored data compatibility.

## Impact

- **Akka core** (`src/core/Akka/Serialization/`): add `SerializerV2.cs`, `SerializerV1Adapter.cs`; modify `Serialization.cs`, `SerializationSetup.cs`, and tests.
- **Akka.Remote**: update `MessageSerializer.cs`, `Serialization/WrappedPayloadSupport.cs`, and affected tests. Classic `AkkaPduCodec` remains wire-compatible.
- **Akka.Persistence**: update `PersistenceMessageSerializer.cs`, `PersistenceSnapshotSerializer.cs`, `LocalSnapshotStore.cs`, and persistence serialization tests.
- **Other modules**: update delivery chunk serialization, DistributedData serializer usages, and tests that assert concrete serializer types.
- **API surface**: `FindSerializerFor()` and `FindSerializerForType()` intentionally return `SerializerV2` in 1.6.
- **API approval tests**: update baselines for the intentional breaking API change.
