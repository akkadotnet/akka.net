## Why

Akka.NET's current serialization API (`Serializer` / `SerializerWithStringManifest`) is `byte[]`-based. Every serialization produces a `byte[]` allocation, and every deserialization requires a `byte[]` input. With Milestone 1 (`modernize-akka-io-tcp`) now flowing `ReadOnlySequence<byte>` through Akka.IO and Akka.Streams, and the Streams TCP transport (Spec 3) planned to use `IBufferWriter<byte>` for writes and `ReadOnlySequence<byte>` for reads, the serializer must speak the same language to achieve zero-copy end-to-end.

This change establishes the **`SerializerV2` foundation** — base class, V1 adapter, infrastructure changes, and a single hand-rolled reference implementation — so that downstream specs can build on a stable buffer-aware API. The codec story (MessagePack runtime, `AkkaWriter`/`AkkaReader`, attributes, source generator) is bundled into a separate future change because it stands or falls as a unit and shouldn't lock in surface area before the foundation is validated.

## What Changes

- **New `SerializerV2` base class in core Akka** — independent (does NOT extend `Serializer`), uses `IBufferWriter<byte>` / `ReadOnlySequence<byte>` as primary API, with virtual `ToBinary()` / `FromBinary()` bridge methods for backward compat.
- **New `SerializerV1Adapter : SerializerV2`** in core Akka — wraps legacy `Serializer` / `SerializerWithStringManifest` to participate in V2 infrastructure. V1 serializers stay V1; the adapter just routes them through V2 dispatch.
- **Modify `Serialization.cs`** — use `SerializerV2` as internal storage type, auto-wrap V1 serializers in `SerializerV1Adapter` on registration, `FindSerializerFor()` returns `SerializerV2`.
- **Modify `MessageSerializer.cs`** — use V2 dispatch (call `Manifest()` directly, no `is SerializerWithStringManifest` type check).
- **Port `ByteArraySerializer` and `PrimitiveSerializers` to `SerializerV2`** as the V2 reference implementation — both are hand-rolled (UTF-8 / `BitConverter` / identity), no `Google.Protobuf` involved. Same IDs, byte-identical wire format.
- **New standalone benchmark in `src/benchmark/`** — synthetic Remote-shaped envelope (serializer ID + manifest + payload) round-tripped V2-direct (`Serialize(IBufferWriter)` → `Deserialize(ReadOnlySequence)`) vs V1-bridge (`ToBinary()` → `FromBinary()`), simulating what `EndpointWriter` will do in Spec 3 without depending on Akka.Remote. Validates that the V2 API earns its keep on allocations and throughput before downstream specs build on it.

### What does NOT change

- The `Serializer` and `SerializerWithStringManifest` classes remain in the codebase for backward compat (wrapped in adapter).
- Wire format for existing serializers (same Protobuf bytes, same serializer IDs, same manifests).
- HOCON serializer registration (`akka.actor.serializers`, `akka.actor.serialization-bindings`, `akka.actor.serialization-identifiers`).
- `SerializationSetup` programmatic registration API.
- Persistence data compatibility (journals store serializerId + manifest + payload — V1 data readable forever).

### Deferred to a future change (`serializer-v2-codegen`)

The following were originally planned for this milestone and have been moved out, to ship together with the source generator as a single user-facing feature:

- `Akka.Serialization.V2` NuGet package
- `MessagePackSerializer : SerializerV2` intermediate class
- Sealed `AkkaWriter` / `AkkaReader` wrapping MessagePack
- `[AkkaSerializable]` / `[AkkaField]` / `[AkkaSerializer]` attributes
- MessagePack NuGet dependency
- Roslyn source generator
- Mechanical port of remaining simple Protobuf serializers (`ClusterMessageSerializer`, `SystemMessageSerializer`) — trivial, but no urgency until they need the V2 path

The rationale: hand-rolling serializers via `AkkaWriter`/`AkkaReader` is not a workflow we expect end users to adopt — codegen is the user story. Shipping the runtime API without the codegen would lock in surface area we'd very likely revisit once we have the codegen requirements in hand. Better to design that surface once, when it's needed.

## Capabilities

### New Capabilities

- `serializer-v2-base`: The `SerializerV2` base class with `IBufferWriter<byte>` / `ReadOnlySequence<byte>` API, `SerializerV1Adapter`, infrastructure changes to `Serialization.cs` and `MessageSerializer.cs`, V2 ports of `ByteArraySerializer` and `PrimitiveSerializers`. Lives in core Akka (no new package).

### Modified Capabilities

(none — V2 is purely additive at the runtime level; the only public-API change is the `FindSerializerFor()` return type)

## Impact

- **Akka core** (`src/core/Akka/Serialization/`): New `SerializerV2.cs`, `SerializerV1Adapter.cs`. Modified `Serialization.cs` (internal storage type, auto-wrapping). V2 port of `ByteArraySerializer.cs`.
- **Akka.Remote** (`src/core/Akka.Remote/Serialization/`): `MessageSerializer.cs` simplified — calls `Manifest()` directly. V2 port of `PrimitiveSerializers.cs`. `EndpointWriter` continues to use the `byte[]` bridge until Spec 3 wires direct buffer access.
- **NuGet dependencies**: None added. `System.Memory` / `System.IO.Pipelines` already in core from Milestone 1.
- **API surface**: `FindSerializerFor()` return type changes from `Serializer` to `SerializerV2`. Mitigated for callers that need the original V1 serializer via `SerializerV1Adapter.Inner`.
- **Test suites**: All existing serialization tests must pass (V1 serializers auto-wrapped transparently). New tests for V2 round-trip, adapter behavior, and the V2-ported primitives. New benchmark project for the transport-envelope harness.
