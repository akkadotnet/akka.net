## 1. Package Setup

- [x] 1.1 Create `src/core/Akka.Serialization.V2/` project
- [x] 1.2 Add MessagePack dependency to the new package only
- [x] 1.3 Add project to solution
- [x] 1.4 Add test project for generated serialization
- [x] 1.5 Configure pack/build metadata

## 2. AkkaWriter / AkkaReader

- [x] 2.1 Port sealed `AkkaWriter` from POC direction
- [x] 2.2 Port sealed `AkkaReader` from POC direction
- [x] 2.3 Implement primitive read/write methods
- [x] 2.4 Implement DateTime, DateTimeOffset, Guid, decimal conventions
- [ ] 2.5 Implement nullable handling
- [x] 2.6 Implement object/field framing helpers
- [x] 2.7 Implement unknown-field skip support
- [x] 2.8 Add round-trip tests for supported built-in types
- [x] 2.9 Encode `[AkkaField]` indexes as explicit MessagePack field IDs

## 3. MessagePack Serializer Base

- [x] 3.1 Add `MessagePackSerializer : SerializerV2`
- [x] 3.2 Add generic protocol-scoped serializer base if needed by generator design
- [x] 3.3 Bridge V2 buffer API to direct MessagePack reader/writer generated hot path
- [x] 3.4 Validate bytes-written/result behavior
- [ ] 3.5 Validate unknown-size fallback behavior
- [x] 3.6 Validate manifest behavior

## 4. Attributes And Diagnostics

- [x] 4.1 Add `[AkkaSerializable]`
- [x] 4.2 Add `[AkkaField(index)]`
- [x] 4.3 Add serializer marker/configuration attributes
- [x] 4.4 Add per-serializer explicit registration shape; no assembly scanning
- [ ] 4.5 Add diagnostics for missing field indexes
- [x] 4.6 Add diagnostics for duplicate field indexes
- [x] 4.7 Add diagnostics for unsupported member types
- [ ] 4.8 Add diagnostics for invalid constructors or inaccessible members

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

## 6. Integration Validation

- [x] 6.1 Register generated serializer through explicit programmatic setup
- [x] 6.2 Verify generated helpers expose a discoverable per-serializer registration path
- [x] 6.3 Round-trip generated payload through `Serialization.cs`
- [ ] 6.4 Send generated payload over classic remoting
- [ ] 6.5 Persist and recover generated event payload
- [ ] 6.6 Save and load generated snapshot payload
- [ ] 6.7 Verify V1 and generated V2 serializers coexist
- [ ] 6.8 Verify oversized payload behavior is deterministic
- [ ] 6.9 Validate generated payloads inside Akka.Delivery wrappers
- [ ] 6.10 Validate generated payloads inside DistributedData wrappers

## 7. POC Benchmark

- [x] 7.1 Add a benchmark using real C# types in a protocol family
- [x] 7.2 Compare generated MessagePack serialization against an existing baseline serializer
- [x] 7.3 Report payload size and allocation/throughput signals
- [x] 7.4 Stop after the benchmark POC for human review before completing the full spec

POC benchmark evidence: short BenchmarkDotNet run completed after switching generated payloads to explicit `[AkkaField]` field-id maps. The field-id implementation measured generated MessagePack serialize at ~585 ns and deserialize at ~1.05 us, versus Newtonsoft.Json serialize at ~20.3 us and deserialize at ~24.6 us. Generated allocations were ~904-920 B versus JSON at ~10.8-13.1 KB. Payload size logged at ~128-130 bytes versus JSON at ~411-413 bytes. A later direct `MessagePackReader` / `MessagePackWriter` refactor measured generated serialize at ~362 ns and deserialize at ~612 ns, with generated allocations reduced to ~856-888 B. Evidence log: `BenchmarkDotNet.Artifacts/Akka.Benchmarks.Serialization.GeneratedMessagePackSerializerBenchmarks-20260603-040856.log`.

## 8. Documentation And Validation

- [ ] 8.1 Document generated serializer usage
- [ ] 8.2 Document supported types and versioning rules
- [ ] 8.3 Document migration from V1 serializers
- [x] 8.4 Run focused generated serialization tests
- [ ] 8.5 Run focused Akka.Remote tests using generated serializers
- [ ] 8.6 Run focused Akka.Persistence tests using generated serializers
- [ ] 8.7 Record any V2 API changes required before Artery starts
- [ ] 8.8 Add Akka.Hosting registration extension after Akka.Hosting is inlined into the main Akka.NET repository
- [ ] 8.9 Package runtime and generator assets as one user-facing NuGet package
