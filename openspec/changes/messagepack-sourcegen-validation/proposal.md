## Why

`SerializerV2` must be proven before Artery envelopes depend on it. The best proof is the source-generated MessagePack serializer path from the POC: it exercises the real API with non-trivial payloads, manifests, size hints, generated code, AOT-oriented patterns, and integration through remoting and persistence.

This change runs after the atomic `serializer-v2` foundation. It is not a late optimization. It is the validation gate for the V2 API shape.

## What Changes

- Add `Akka.Serialization.V2` package for user-facing generated serializers.
- Use direct MessagePack-CSharp reader/writer cursors in generated serializers.
- Add attributes such as `[AkkaSerializable]`, `[AkkaField]`, and serializer configuration attributes.
- Add a Roslyn incremental source generator that emits V2 serializers.
- Register generated serializers explicitly through per-serializer generated helpers; do not use runtime assembly scanning.
- Include `IActorRef` field support so generated payloads exercise Akka's transport-aware actor-ref serialization rules.
- Validate generated serializers through `Serialization.cs`, classic remoting, persisted events, and persisted snapshots.
- Validate generated payloads inside existing Akka.Delivery and DistributedData wrapper serializers where practical.
- Add an initial benchmark POC using real C# protocol-family messages before attempting the full spec.
- Validate V2 API details needed by Artery: manifests, size hints, unknown-size fallback, bytes-written/result semantics, oversized payload handling, and V1 coexistence.

### What Does Not Change

- Core Akka does not take a MessagePack dependency.
- Artery envelopes are not implemented here.
- Existing classic remoting, persistence, Akka.Delivery, and DistributedData wire formats are not replaced by default.
- Classic remoting and persistence bridge work belongs to the preceding `serializer-v2` foundation.
- V1 serializers remain supported through `SerializerV1Adapter`.

## Capabilities

### New Capabilities

- `messagepack-sourcegen-validation`: Source-generated MessagePack serializers for Akka.NET 1.6 and the integration tests proving `SerializerV2` is ready for Artery.

## Impact

- **New package**: `src/core/Akka.Serialization.V2/`
- **Tests**: generated serializer tests, remoting tests, persistence event tests, persistence snapshot tests, cross-assembly generator tests.
- **Benchmarks**: initial generated MessagePack benchmark using a real protocol family; direct cursor benchmark comparisons can follow after the POC slice.
- **Build**: source generator package/project added to solution and pack targets.
- **Documentation**: update migration and usage docs for generated serializers.
