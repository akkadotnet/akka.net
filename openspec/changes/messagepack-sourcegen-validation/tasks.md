## 1. Package Setup

- [x] 1.1 Create `src/core/Akka.Serialization.V2/` project
- [x] 1.2 Add MessagePack dependency to the new package only
- [x] 1.3 Add project to solution
- [ ] 1.4 Add test project for generated serialization
- [x] 1.5 Configure pack/build metadata

## 2. AkkaWriter / AkkaReader

- [x] 2.1 Port sealed `AkkaWriter` from POC direction
- [x] 2.2 Port sealed `AkkaReader` from POC direction
- [x] 2.3 Implement primitive read/write methods
- [x] 2.4 Implement DateTime, DateTimeOffset, Guid, decimal conventions
- [ ] 2.5 Implement nullable handling
- [x] 2.6 Implement object/field framing helpers
- [x] 2.7 Implement unknown-field skip support
- [ ] 2.8 Add round-trip tests for supported built-in types

## 3. MessagePack Serializer Base

- [x] 3.1 Add `MessagePackSerializer : SerializerV2`
- [x] 3.2 Add generic protocol-scoped serializer base if needed by generator design
- [x] 3.3 Bridge V2 buffer API to `AkkaWriter` / `AkkaReader`
- [ ] 3.4 Validate bytes-written/result behavior
- [ ] 3.5 Validate unknown-size fallback behavior
- [ ] 3.6 Validate manifest behavior

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
- [ ] 5.6 Support nested generated types
- [ ] 5.7 Support collection types selected for 1.6 MVP
- [x] 5.8 Support `IActorRef` fields using transport-aware path serialization
- [x] 5.9 Support explicit cross-assembly composition via per-serializer registrations

## 6. Integration Validation

- [x] 6.1 Register generated serializer through explicit programmatic setup
- [x] 6.2 Verify generated helpers expose a discoverable per-serializer setup path
- [ ] 6.3 Round-trip generated payload through `Serialization.cs`
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

POC benchmark evidence: short BenchmarkDotNet run completed with generated MessagePack serialize at ~412 ns, deserialize at ~778 ns, Newtonsoft.Json serialize at ~20.6 us, deserialize at ~22.7 us, generated allocations at ~904-912 B versus JSON at ~10.8-13.1 KB, and payload size logged at ~121-122 bytes versus JSON at ~412-413 bytes.

## 8. Documentation And Validation

- [ ] 8.1 Document generated serializer usage
- [ ] 8.2 Document supported types and versioning rules
- [ ] 8.3 Document migration from V1 serializers
- [ ] 8.4 Run focused generated serialization tests
- [ ] 8.5 Run focused Akka.Remote tests using generated serializers
- [ ] 8.6 Run focused Akka.Persistence tests using generated serializers
- [ ] 8.7 Record any V2 API changes required before Artery starts
