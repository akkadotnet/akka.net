## 1. Package Setup

- [ ] 1.1 Create `src/core/Akka.Serialization.V2/` project
- [ ] 1.2 Add MessagePack dependency to the new package only
- [ ] 1.3 Add project to solution
- [ ] 1.4 Add test project for generated serialization
- [ ] 1.5 Configure pack/build metadata

## 2. AkkaWriter / AkkaReader

- [ ] 2.1 Port sealed `AkkaWriter` from POC direction
- [ ] 2.2 Port sealed `AkkaReader` from POC direction
- [ ] 2.3 Implement primitive read/write methods
- [ ] 2.4 Implement DateTime, DateTimeOffset, Guid, decimal conventions
- [ ] 2.5 Implement nullable handling
- [ ] 2.6 Implement object/field framing helpers
- [ ] 2.7 Implement unknown-field skip support
- [ ] 2.8 Add round-trip tests for supported built-in types

## 3. MessagePack Serializer Base

- [ ] 3.1 Add `MessagePackSerializer : SerializerV2`
- [ ] 3.2 Add generic protocol-scoped serializer base if needed by generator design
- [ ] 3.3 Bridge V2 buffer API to `AkkaWriter` / `AkkaReader`
- [ ] 3.4 Validate bytes-written/result behavior
- [ ] 3.5 Validate unknown-size fallback behavior
- [ ] 3.6 Validate manifest behavior

## 4. Attributes And Diagnostics

- [ ] 4.1 Add `[AkkaSerializable]`
- [ ] 4.2 Add `[AkkaField(index)]`
- [ ] 4.3 Add serializer marker/configuration attributes
- [ ] 4.4 Add diagnostics for missing field indexes
- [ ] 4.5 Add diagnostics for duplicate field indexes
- [ ] 4.6 Add diagnostics for unsupported member types
- [ ] 4.7 Add diagnostics for invalid constructors or inaccessible members

## 5. Source Generator

- [ ] 5.1 Implement Roslyn incremental source generator
- [ ] 5.2 Generate serializer class for annotated messages
- [ ] 5.3 Generate manifest dispatch
- [ ] 5.4 Generate write methods
- [ ] 5.5 Generate read methods
- [ ] 5.6 Support nested generated types
- [ ] 5.7 Support collection types selected for 1.6 MVP
- [ ] 5.8 Support cross-assembly generated serializers

## 6. Integration Validation

- [ ] 6.1 Register generated serializer through HOCON
- [ ] 6.2 Register generated serializer through programmatic setup if supported
- [ ] 6.3 Round-trip generated payload through `Serialization.cs`
- [ ] 6.4 Send generated payload over classic remoting
- [ ] 6.5 Persist and recover generated event payload
- [ ] 6.6 Save and load generated snapshot payload
- [ ] 6.7 Verify V1 and generated V2 serializers coexist
- [ ] 6.8 Verify oversized payload behavior is deterministic

## 7. Documentation And Validation

- [ ] 7.1 Document generated serializer usage
- [ ] 7.2 Document supported types and versioning rules
- [ ] 7.3 Document migration from V1 serializers
- [ ] 7.4 Run focused generated serialization tests
- [ ] 7.5 Run focused Akka.Remote tests using generated serializers
- [ ] 7.6 Run focused Akka.Persistence tests using generated serializers
- [ ] 7.7 Record any V2 API changes required before Artery starts
