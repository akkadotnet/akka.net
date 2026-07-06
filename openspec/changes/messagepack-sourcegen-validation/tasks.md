## 1. Package Setup

- [x] 1.1 Create `src/core/Akka.Serialization.V2/` project
- [x] 1.2 Add MessagePack dependency to the new package only
- [x] 1.3 Add project to solution
- [x] 1.4 Add test project for generated serialization
- [x] 1.5 Configure pack/build metadata

## 2. Direct MessagePack Conventions

- [x] 2.1 Use direct `MessagePackWriter` cursors in generated serializers
- [x] 2.2 Use direct `MessagePackReader` cursors in generated serializers
- [x] 2.3 Implement primitive read/write conventions
- [x] 2.4 Implement DateTime, DateTimeOffset, Guid, decimal conventions
- [x] 2.5 Implement nullable handling
- [x] 2.6 Implement object/field framing helpers
- [x] 2.7 Implement unknown-field skip support
- [x] 2.8 Add round-trip tests for supported built-in type conventions
- [x] 2.9 Encode `[AkkaField]` indexes as explicit MessagePack field IDs

## 3. MessagePack Serializer Base

- [x] 3.1 Add `MessagePackSerializer : SerializerV2`
- [x] 3.2 Add generic protocol-scoped serializer base if needed by generator design
- [x] 3.3 Bridge V2 buffer API to direct MessagePack reader/writer generated hot path
- [x] 3.4 Validate bytes-written/result behavior
- [x] 3.5 Validate unknown-size fallback behavior
- [x] 3.6 Validate manifest behavior
- [x] 3.7 Define exact-or-unknown `SizeHint` semantics

## 4. Attributes And Diagnostics

- [x] 4.1 Add `[AkkaSerializable]`
- [x] 4.2 Add `[AkkaField(index)]`
- [x] 4.3 Add serializer marker/configuration attributes
- [x] 4.4 Add per-serializer explicit registration shape; no assembly scanning
- [ ] 4.5 Add diagnostics for missing field indexes
- [x] 4.6 Add diagnostics for duplicate field indexes
- [x] 4.7 Add diagnostics for unsupported member types
- [ ] 4.8 Add diagnostics for invalid constructors or inaccessible members
- [x] 4.9 Add `[AkkaEnvelopePayload]` marker for serializer-boundary fields

## 5. Source Generator

- [x] 5.1 Implement Roslyn incremental source generator
- [x] 5.2 Generate serializer class for annotated messages
- [x] 5.3 Generate manifest dispatch
- [x] 5.4 Generate write methods
- [x] 5.5 Generate read methods
- [x] 5.6 Support nested generated types with their own explicit field IDs
- [ ] 5.7 Support immutable and read-only collection types selected for 1.6 MVP
- [x] 5.8 Support `IActorRef` fields using transport-aware path serialization
- [x] 5.9 Support explicit cross-assembly composition via per-serializer registrations
- [ ] 5.10 Support init-only property or field assignment for immutable message shapes
- [ ] 5.11 Reject unsupported mutable, factory-only, or arbitrary polymorphic message shapes with diagnostics
- [x] 5.12 Implement exact generated size calculators for schemas whose full encoded size can be proven
- [x] 5.13 Support `[AkkaEnvelopePayload]` fields through runtime Akka serializer lookup
- [x] 5.14 Support foreign-type formatters via [AkkaSerializerFormatter] escape hatch (AddressFormatter/ActorPathFormatter built-ins, byte-compatible with Artery control-message wire format)
- [x] 5.15 Honor declared accessibility of serializer partial classes (internal serializers)

## 6. Integration Validation

- [x] 6.1 Register generated serializer through explicit programmatic setup
- [x] 6.2 Verify generated helpers expose a discoverable per-serializer registration path
- [x] 6.3 Round-trip generated payload through `Serialization.cs`
- [ ] 6.4 Send generated payload over classic remoting
- [ ] 6.5 Persist and recover generated event payload
- [ ] 6.6 Save and load generated snapshot payload
- [x] 6.7 Verify V1 and generated V2 serializers coexist
- [x] 6.8 Verify oversized payload behavior is deterministic (OversizedPayloadDeterminismSpec: encode-time PayloadSizeExceededException via PooledPayloadWriter maxCapacity — see design.md Decision 12 here and serializer-v2 design.md Decision 12 for the writer mechanism)
- [ ] 6.9 Validate generated payloads inside Akka.Delivery wrappers
- [ ] 6.10 Validate generated payloads inside DistributedData wrappers
- [x] 6.11 Validate opaque non-MessagePack payload metadata inside a generated MessagePack wrapper
- [x] 6.12 Validate attribute-driven nested generated envelopes carrying generated V2 and custom V1 payloads
- [x] 6.13 Avoid byte-array copy when deserializing V2 envelope payloads from MessagePack `bin` fields

## 7. POC Benchmark

- [x] 7.1 Add a benchmark using real C# types in a protocol family
- [x] 7.2 Compare generated MessagePack serialization against an existing baseline serializer
- [x] 7.3 Report payload size and allocation/throughput signals
- [x] 7.4 Stop after the benchmark POC for human review before completing the full spec
- [x] 7.5 Add envelope payload composition benchmark scenarios for V2, V1, and pre-captured payloads
- [x] 7.6 Run real BenchmarkDotNet for attribute-driven nested envelope payload scenarios
- [x] 7.7 Add same-shape V1 versus generated V2 nested envelope benchmark scenarios

POC benchmark evidence: short BenchmarkDotNet run completed after switching generated payloads to explicit `[AkkaField]` field-id maps. The field-id implementation measured generated MessagePack serialize at ~585 ns and deserialize at ~1.05 us, versus Newtonsoft.Json serialize at ~20.3 us and deserialize at ~24.6 us. Generated allocations were ~904-920 B versus JSON at ~10.8-13.1 KB. Payload size logged at ~128-130 bytes versus JSON at ~411-413 bytes. A later direct `MessagePackReader` / `MessagePackWriter` refactor measured generated serialize at ~362 ns and deserialize at ~612 ns, with generated allocations reduced to ~856-888 B. Evidence log: `BenchmarkDotNet.Artifacts/Akka.Benchmarks.Serialization.GeneratedMessagePackSerializerBenchmarks-20260603-040856.log`.

Nested envelope benchmark evidence: real BenchmarkDotNet run for `*GeneratedMessagePackSerializerBenchmarks.NestedEnvelope_*` completed after adding `[AkkaEnvelopePayload]`, exact generated `SizeHint`, pooled V2 payload staging for MessagePack `bin` fallback, V2 `ReadOnlySequence<byte>` payload deserialization, and same-shape V1 comparison scenarios. Payload sizes logged at ~233-234 bytes for nested generated V2 payload envelopes, 150 bytes for nested tiny custom V1 payload envelopes, and ~267-268 bytes for nested same-shape custom V1 payload envelopes. Report: `BenchmarkDotNet.Artifacts/results/Akka.Benchmarks.Serialization.GeneratedMessagePackSerializerBenchmarks-report-github.md`.

| Method                                                           | Mean       | Error    | StdDev   | Gen0   | Allocated |
|----------------------------------------------------------------- |-----------:|---------:|---------:|-------:|----------:|
| NestedEnvelope_generated_payload_serialize                       |   628.4 ns | 11.94 ns | 15.52 ns | 0.1078 |     904 B |
| NestedEnvelope_generated_payload_deserialize_and_recover         |   938.0 ns | 17.96 ns | 18.45 ns | 0.1392 |    1176 B |
| NestedEnvelope_custom_payload_serialize                          |   391.2 ns |  7.83 ns | 12.19 ns | 0.0973 |     816 B |
| NestedEnvelope_custom_payload_deserialize_and_recover            |   545.9 ns | 10.87 ns | 18.46 ns | 0.0849 |     712 B |
| NestedEnvelope_custom_same_shape_payload_serialize               |   757.9 ns | 15.72 ns | 46.11 ns | 0.2241 |    1880 B |
| NestedEnvelope_custom_same_shape_payload_deserialize_and_recover | 1,006.0 ns | 20.14 ns | 25.47 ns | 0.1659 |    1400 B |

## 8. Documentation And Validation

- [ ] 8.1 Document generated serializer usage
- [ ] 8.2 Document supported types and versioning rules
- [ ] 8.3 Document migration from V1 serializers
- [x] 8.4 Run focused generated serialization tests
- [ ] 8.5 Run focused Akka.Remote tests using generated serializers
- [ ] 8.6 Run focused Akka.Persistence tests using generated serializers
- [x] 8.7 Record any V2 API changes required before Artery starts (recorded: foreign-type formatter escape hatch + public `MessagePackSizes` + declared-accessibility emission [design.md Decision 11], encode-time oversized-payload determinism [design.md Decision 12], sync-for-1.6 API sign-off [serializer-v2 design.md Decision 13], Manifest invariant [serializer-v2 design.md Decision 14]; the `PooledPayloadWriter` buffer/ownership contract landed separately as serializer-v2 Decision 12, PR #8322)
- [ ] 8.8 Add Akka.Hosting registration extension after Akka.Hosting is inlined into the main Akka.NET repository
- [ ] 8.9 Package runtime and generator assets as one user-facing NuGet package

## 9. Post-#8325 Gaps

Found while attempting to swap Artery's control messages (`ArteryControlMessageSerializer`) onto
generated serializers: a deliberately fieldless heartbeat message and an `[AkkaSerializable]`
struct used as a nested field both broke codegen. Both gaps are now closed.

- [x] 9.1 Add `AllowEmpty` opt-in to `[AkkaSerializable]` so a deliberately fieldless top-level
      protocol message (Artery's `ArteryHeartbeat`/`ArteryHeartbeatRsp` -- "arrival IS the signal")
      is not hard-rejected by AKKASG004; the guardrail still fires by default for messages that
      don't opt in
- [x] 9.2 Fix `IsReferenceLike`/`GetLocalType`/`GetConstructorArgument`/`DefaultValue`/size-and-write
      codegen to thread the annotated type's is-value-type through for `FieldKind.Object`, mirroring
      the formatter escape hatch's `IsTargetValueType`, so an `[AkkaSerializable] readonly record
      struct` (mirroring Artery's `UniqueAddress`) can be used as a required or optional nested field
      without generating an `Inner?`-vs-`Inner` mismatch (CS1503)
