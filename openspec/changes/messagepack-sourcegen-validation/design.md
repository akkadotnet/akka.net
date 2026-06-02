## Context

The POC at `Aaronontheweb/AkkaSerializationPoC` validated the preferred direction for generated serialization:

- MessagePack is the default codec.
- `AkkaWriter` and `AkkaReader` are concrete sealed wrappers over MessagePack.
- There is no generalized codec abstraction layer.
- Source generation provides compile-time validation and avoids reflection.

The source generator should not be developed against hypothetical serialization APIs. It should run after `serializer-v2` makes V2 canonical and after classic remoting and persistence are bridged. That lets generated serializers validate the exact API Artery will consume.

## Goals / Non-Goals

**Goals:**

- Implement user-facing source-generated MessagePack serialization on top of `SerializerV2`.
- Validate generated serializers through `Serialization`, classic remoting, events, and snapshots.
- Confirm V2 API details before Artery envelopes are built.
- Support AOT-oriented, reflection-free serializer code.
- Preserve V1/V2 coexistence.

**Non-Goals:**

- Replacing all built-in protobuf serializers.
- Adding MessagePack dependency to core Akka.
- Implementing Artery envelopes.
- Removing V1 serializer support.

## Decisions

### 1. MessagePack Package Outside Core Akka

`Akka.Serialization.V2` owns MessagePack dependencies, attributes, writer/reader helpers, and source generator integration.

Core Akka owns only `SerializerV2` and compatibility infrastructure.

### 2. Sealed Writer / Reader

Use sealed `AkkaWriter` and `AkkaReader` classes rather than codec interfaces.

Rationale: the POC showed this improves JIT devirtualization and keeps the API simpler.

### 3. Sourcegen Validates V2 API Before Artery

Generated serializers must prove:

- bytes-written/result reporting works,
- unknown-size fallback works,
- manifests work,
- V1 adapter coexistence works,
- persistence can store and recover generated payloads,
- classic remoting can send generated payloads.

### 4. Version-Tolerant Schema

Fields are explicitly indexed using `[AkkaField(index)]`. Generated code should support unknown trailing fields where possible.

### 5. Cross-Assembly Composition Is Required

Real Akka.NET applications use multiple assemblies. The generator must support serializable messages and generated serializers across assembly boundaries.

## Risks / Trade-offs

**Generator complexity**: keep diagnostics focused and add incrementally.

**MessagePack conventions**: document DateTime, Guid, decimal, nullable, collection, and nested object conventions.

**API churn**: if sourcegen finds V2 API problems, fix V2 before Artery starts.

**Persistence compatibility**: generated serializers must not compromise stored payload readability.
