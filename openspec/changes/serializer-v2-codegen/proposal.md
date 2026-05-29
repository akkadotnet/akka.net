## Status: Placeholder

This change captures work that was originally part of `serializer-v2` (Milestone 2 of the 1.6 transport epic) and was split out on 2026-05-10. It is **not yet specified in detail** — the runtime API surface depends on the source generator's requirements, and we deliberately want to design both together rather than locking in the runtime first and then redesigning it once codegen lands.

This file exists so that:

- The deferred work has a stable home in the openspec tree
- `serializer-v2`'s proposal can refer to it by name
- A future planning session can flesh it out without reconstructing context

## Why

Akka.NET end users need an ergonomic way to define their own V2 serializers. Hand-rolling a serializer against the `SerializerV2` buffer API (writing `Serialize(IBufferWriter<byte>, object)` and `Deserialize(ReadOnlySequence<byte>, string)` directly) is acceptable for internal serializers but is not the workflow we expect end users to adopt. The user story is: annotate a record/class with `[AkkaSerializable]`, mark a serializer class with `[AkkaSerializer]`, and have a Roslyn source generator emit the `Write` / `Read` methods that drive `AkkaWriter` / `AkkaReader` over MessagePack.

The runtime layer (`MessagePackSerializer`, `AkkaWriter`, `AkkaReader`, the attributes, the `Akka.Serialization.V2` package) and the codegen layer must be designed together, because the runtime API surface is the codegen's emission target — if the runtime API has the wrong shape for what the generator wants to emit, we redesign both. Better to do it once.

## What this change will cover (sketch)

The detailed proposal will be written when the change is scheduled. Anticipated scope:

- `Akka.Serialization.V2` NuGet package (separate from core Akka)
- `MessagePackSerializer : SerializerV2` intermediate base class
- `MessagePackSerializer<TProtocol>` generic variant for protocol scoping
- Sealed `AkkaWriter` / `AkkaReader` classes wrapping MessagePack-CSharp
- `[AkkaSerializable]` attribute on user message types
- `[AkkaField(index)]` attribute for stable field indexing
- `[AkkaSerializer]` attribute marking a partial serializer class as a codegen target
- Roslyn incremental source generator emitting the `Write` / `Read` overrides
- MSBuild integration so end-user projects pick up the generator transparently
- Mechanical port of remaining Protobuf-based internal serializers to `SerializerV2`:
  - `ClusterMessageSerializer`
  - `SystemMessageSerializer`
  - The four `WrappedPayloadSupport` serializers (Sharding, PubSub, ReliableDelivery, Misc) — these need additional design for the nested-payload zero-copy path

## Why not ship the runtime layer alone first?

Considered and rejected. Shipping `Akka.Serialization.V2` with `AkkaWriter` / `AkkaReader` / attributes but no codegen means:

- Public surface area gets locked in before we know what the generator wants to emit against
- End users either hand-roll codecs (poor ergonomics, no real adopters) or wait for the generator anyway
- Any redesign of `AkkaWriter` to fit codegen is a breaking change to a published package

Source generator-only without the runtime is impossible — they're two halves of the same feature.

## Predecessors

- `serializer-v2` — establishes the `SerializerV2` foundation, V1 adapter, infrastructure, and the V2 reference implementations (`ByteArraySerializer`, `PrimitiveSerializers`). Must be archived before this change starts.

## Anticipated successors / dependencies on this change

- The full performance story for `WrappedPayloadSupport` serializers depends on this change shipping (nested-payload zero copy needs the V2 buffer API on the inner serializer)
- The user-facing migration guide for "how to write a V2 serializer in Akka.NET 1.6" depends on the codegen ergonomics being in place
