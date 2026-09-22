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

- [x] 3.1 Add `AkkaSerializer : SerializerV2` (non-generic base class; renamed from the original generic `MessagePackSerializer<TProtocol>` -- protocol identity moved entirely onto `[AkkaSerializer<TProtocol>]`; `src/core/Akka.Serialization.V2/AkkaSerializer.cs`)
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
- [x] 4.5 Add diagnostics for missing field indexes (a type with no `[AkkaField]` properties at all: AKKASG004 `MissingFields`, unless `AllowEmpty = true`; a constructor parameter left uncovered by any `[AkkaField]`: AKKASG027 `ConstructorParameterNotCovered`)
- [x] 4.6 Add diagnostics for duplicate field indexes
- [x] 4.7 Add diagnostics for unsupported member types
- [x] 4.8 Add diagnostics for invalid constructors or inaccessible members (AKKASG026 `NoMatchingConstructor` -- no accessible constructor maps every required parameter to an `[AkkaField]` property by name, or an uncovered property has no accessible setter, or a constructor parameter matches multiple fields ambiguously case-insensitively; AKKASG028 `FieldPropertyNotAccessible` -- a static `[AkkaField]` property or one with no accessible getter; `AkkaSerializerGeneratorDiagnosticsSpec.cs`)
- [x] 4.9 Add `[AkkaEnvelopePayload]` marker for serializer-boundary fields

## 5. Source Generator

- [x] 5.1 Implement Roslyn incremental source generator
- [x] 5.2 Generate serializer class for annotated messages
- [x] 5.3 Generate manifest dispatch
- [x] 5.4 Generate write methods
- [x] 5.5 Generate read methods
- [x] 5.6 Support nested generated types with their own explicit field IDs
- [x] 5.7 Support immutable and read-only collection types selected for 1.6 MVP -- the generator now natively supports ten collection shapes: the original four (`T[]`, `List<T>`, `IReadOnlyList<T>`, `Dictionary<TKey,TValue>`) plus the six from design.md Decision 7.2's original scope (`ImmutableArray<T>`, `ImmutableList<T>`, `ImmutableHashSet<T>`, `ImmutableDictionary<TKey,TValue>`, `IReadOnlyCollection<T>`, `IReadOnlyDictionary<TKey,TValue>`). All ten share identical MessagePack wire framing (`AkkaSerializerGenerator.TryMapCollection`, `TryMatchSingleArgumentKind`, `TryMatchKeyValueKind`; wire-identity assertions in `ImmutableCollectionFieldSpec.cs`). Deserialization materializes: `ImmutableArray<T>` via `ImmutableArray.CreateBuilder<T>(capacity).MoveToImmutable()` (zero-copy, pre-sized from the wire's element count); `ImmutableList<T>`/`ImmutableHashSet<T>`/`ImmutableDictionary<TKey,TValue>` via their own `Builder` + `.ToImmutable()` (no capacity parameter -- tree/trie-backed); `IReadOnlyCollection<T>` via `List<T>`; `IReadOnlyDictionary<TKey,TValue>` via `Dictionary<TKey,TValue>` (same as the existing `IReadOnlyList<T>`/`Dictionary<TKey,TValue>` shapes). `ImmutableArray<T>` is the one VALUE-typed (struct) collection kind: `default(ImmutableArray<T>).IsDefault` is treated as the null-ish wire state (encodes as MessagePack nil, decodes back to `default`), distinct from `ImmutableArray<T>.Empty` (Length 0, IsDefault false, encodes as a zero-length array header) -- see the design note above `EmitWriteCollectionBody` in `AkkaSerializerGenerator.cs` and Decision 7.2 below. Nesting composes (`ImmutableList<Nested>`, `ImmutableDictionary<string, List<int>>`, `ImmutableList<ImmutableArray<int>>`) and an unsupported element/value type still collapses to `FieldKind.Unsupported` (AKKASG003) for every one of the six new shapes. Test coverage: `ImmutableCollectionFieldSpec.cs` (round-trip, null-vs-empty, default-vs-empty for `ImmutableArray<T>`, wire-identity vs the equivalent `List`/`Dictionary`/array shape, exact SizeHint, nested composition) and three new cases in `AkkaSerializerGeneratorDiagnosticsSpec.cs` (compiles cleanly for all six shapes, AKKASG003 for each shape given an unsupported element, AKKASG003 still fires for an out-of-scope immutable type like `ImmutableSortedSet<T>`)
- [x] 5.8 Support `IActorRef` fields using transport-aware path serialization
- [x] 5.9 Support explicit cross-assembly composition via per-serializer registrations (`SerializerRegistration.CreateSetup(params SerializerRegistration[])` composes registrations from any number of assemblies into one `SerializationSetup`; `src/core/Akka.Serialization.V2/SerializerRegistration.cs`)
- [x] 5.10 Support init-only property or field assignment for immutable message shapes (`GeneratedMessagePackSerializerSpec`: "Generated serializer should round-trip an init-only POCO reconstructed via a parameterless constructor and object initializer" / "...a mix of constructor arguments and object-initializer assignments"; hybrid construction plan tracks `InitializerFieldNames` for properties assigned after the constructor call)
- [x] 5.11 Reject unsupported mutable, factory-only, or arbitrary polymorphic message shapes with diagnostics (AKKASG026 no matching constructor, AKKASG027 uncovered defaulted parameter, AKKASG028 inaccessible field property, AKKASG029 protocol message not `[AkkaSerializable]`, AKKASG032 malformed `[AkkaSerializer]` class shape, AKKASG033 non-interface protocol type, AKKASG034 closed generic registration with no effect, plus union member strictness AKKASG015/016/017/018/019 and exact-runtime-type union write dispatch; `AkkaSerializerGeneratorDiagnosticsSpec.cs`, `GeneratedUnionSpec.cs`)
- [x] 5.12 Implement exact generated size calculators for schemas whose full encoded size can be proven
- [x] 5.13 Support `[AkkaEnvelopePayload]` fields through runtime Akka serializer lookup
- [x] 5.14 Support foreign-type formatters via [AkkaSerializerFormatter] escape hatch (AddressFormatter/ActorPathFormatter built-ins, byte-compatible with Artery control-message wire format)
- [x] 5.15 Honor declared accessibility of serializer partial classes (internal serializers)
- [x] 5.16 Remove `[AkkaEnvelopePayload]`; an `object`-typed field is the serializer boundary on its own, AKKASG035 retired, AKKASG038 added (design.md Decision 20) — PR #8518
- [x] 5.17 Extend Decision 20 to collection elements: a `List<object>`/`object[]`/any other natively-supported collection whose element type is `object` (or `object?`) treats each element as its own envelope-payload boundary, nullable-aware the same way a field is (`MapCollectionElement`, `EmitWriteElement`/`EmitReadElement`/`EmitSizeElement` `FieldKind.EnvelopePayload` cases). A dictionary KEY typed `object` is rejected with AKKASG003 instead (unstable round-tripped identity for hash/equality lookups, plus a null key crashing `Dictionary<TKey,TValue>` at runtime); dictionary VALUES typed `object` are supported. `ObjectElementSpec.cs`

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
- [x] 8.9 Package runtime and generator assets as one user-facing NuGet package (`src/core/Akka.Serialization.V2/Akka.Serialization.V2.csproj`: added a `ReferenceOutputAssembly="false"` `OutputItemType="Analyzer"` `ProjectReference` to `Akka.Serialization.V2.Generators` plus a `_AkkaSerializationV2PackGeneratorAnalyzer` pack target that embeds the generator dll from `@(Analyzer)` into `analyzers/dotnet/cs` via `None Pack="true"`, matching the standard IsRoslynComponent-style analyzer-packing convention; `Akka.Serialization.V2.Generators.csproj` stays `IsPackable=false` so it never appears in the nuspec `<dependencies>`. Verified `dotnet pack src/core/Akka.Serialization.V2 -c Release`: nupkg contains `lib/net10.0/Akka.Serialization.V2.dll` and `analyzers/dotnet/cs/Akka.Serialization.V2.Generators.dll`; nuspec dependencies are only `Akka` and `MessagePack` (no generator entry). End-to-end proof in a throwaway net10.0 console app outside the repo, referencing only the packed nupkg through a local feed: the generator ran from the package (not a `ProjectReference` -- confirmed via `EmitCompilerGeneratedFiles`, generated source attributed to `Akka.Serialization.V2.Generators.AkkaSerializerGenerator`), compiled, and round-tripped an `[AkkaSerializable]` message through a real `ActorSystem`'s `Serialization.Serialize`/`Deserialize`.)

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

## 10. Manifest-Discriminated Closed Unions

Implemented after the initial spec slice (commit range `5308542ac..278e9a4a0`); not previously
tracked in this file.

- [x] 10.1 Add `[AkkaUnion(Type first, params Type[] rest)]`, declarable on a union base
      interface/abstract class (type-level, inherited by every field of that static type) or on a
      single `[AkkaField]` property (narrowing override); the constructor shape makes an empty
      member set a compile error rather than a diagnostic (`src/core/Akka.Serialization.V2/Attributes.cs`
      `AkkaUnionAttribute`)
- [x] 10.2 Encode the union wire format as a manifest-discriminated 2-entry map,
      `{1: member manifest, 2: inline member field map}`, distinct from the
      `[AkkaEnvelopePayload]` frame's `{1: serializerId, 2: manifest, 3: opaque bytes}`
      (`GeneratedUnionSpec`: "Union wire format should be a manifest-discriminated 2-entry map with
      inline member fields")
- [x] 10.3 Dispatch union writes by exact runtime type; fail serialization for a runtime value whose
      exact type is not a declared member, including an undeclared subtype of a declared member
      (`GeneratedUnionSpec`: "Union write should fail serialization for an undeclared runtime type" /
      "...for an undeclared subtype of a declared member")
- [x] 10.4 Validate union member types are `[AkkaSerializable]`, declare a manifest unique within the
      union, and are assignable to the field's static type; reject an invalid member set (AKKASG015
      `UnionMemberNotSerializable`, AKKASG016 `UnionMemberMissingManifest`, AKKASG017
      `UnionMemberManifestCollision`, AKKASG018 `UnionMemberNotAssignable`, AKKASG019
      `InvalidUnionMemberSet`)
- [x] 10.5 Emit an advisory (Info, not Error) when a union member type is not sealed, since write
      dispatch matches exact runtime type and an unsealed member is exactly where an undeclared
      subtype can appear (AKKASG025 `UnionMemberNotSealed`)
- [x] 10.6 Deduplicate generated union dispatch helpers by (field static type, member set) identity so
      the common case -- one type-level `[AkkaUnion]` declaration used by many fields -- emits one
      helper instead of one per field
- [x] 10.7 Support nullable union fields, field-level overrides narrowing a type-level member set,
      struct members via boxing, and a union member that is also independently a top-level protocol
      message sharing one manifest across both roles (`GeneratedUnionSpec`)

## 11. Closed Generic Registrations

Implemented after the initial spec slice (commit `5308542ac`); not previously tracked in this file.
A Roslyn source generator cannot reify an open generic type, so a generic `[AkkaSerializable]`
definition is never itself serialized -- only explicitly registered closed constructions are, the
same model System.Text.Json's source generator uses for `[JsonSerializable]`.

- [x] 11.1 Add `[AkkaSerializable<TMessage>(Manifest = ...)]`, applied to the `[AkkaSerializer]`
      class, to register one closed generic construction of a generic `[AkkaSerializable]` type
      (`src/core/Akka.Serialization.V2/Attributes.cs` `AkkaSerializableAttribute<TMessage>`)
- [x] 11.2 Each registered closed construction dispatches as its own top-level message with its own
      manifest and its own generated `Manifest`/`Serialize`/`Deserialize` arm, with generic fields
      resolved against the concrete type arguments, and is usable as a nested field of an ordinary
      message (`GeneratedClosedGenericSpec`: "Distinct closed constructions of the same generic
      should dispatch by distinct manifests", "...should be usable as a nested field of an ordinary
      message")
- [x] 11.3 Require a closed generic registration when a generic `[AkkaSerializable]` definition
      implements the serializer's protocol interface (AKKASG022
      `GenericSerializableRequiresRegistration`); the open definition itself is never serialized
- [x] 11.4 Reject a field typed as a closed generic construction that is not registered on the owning
      serializer (AKKASG023 `UnregisteredClosedGenericField`)
- [x] 11.5 Reject an invalid closed generic registration target (AKKASG020
      `InvalidClosedGenericRegistration`), a duplicate registration of the same construction
      (AKKASG021 `DuplicateClosedGenericRegistration`), and a registration that neither implements
      the protocol nor is reachable from any `[AkkaField]` property, so it would otherwise have no
      effect (AKKASG034 `ClosedGenericRegistrationNotInProtocol`)

## 12. Attribute Surface Finalization / Illegal-States-Unrepresentable Pass

Design review on issue #8384 / PR #8385 reshaped the attribute surface so several states the
generator used to reject with a diagnostic became states the C# compiler rejects outright, and
added generator-level validation of the `[AkkaSerializer]` class shape itself. Commit range
`e9bfbef43..5ea975e30`; not previously tracked in this file.

- [x] 12.1 Rename the base class from the generic `MessagePackSerializer<TProtocol>` to the
      non-generic `AkkaSerializer`, moving protocol identity entirely onto
      `[AkkaSerializer<TProtocol>]` (`src/core/Akka.Serialization.V2/AkkaSerializer.cs`)
- [x] 12.2 Make `[AkkaSerializer<TProtocol>(string name, int serializerId)]` constructor arguments
      required with get-only `Name`/`SerializerId` properties -- no auto-assigned id or alias, by
      design (source-generator registration order across a compilation is nondeterministic, and a
      collision with an id/alias defined elsewhere, e.g. in HOCON, is invisible to the generator);
      AKKASG001/002 narrow to argument-validity checks only (non-null/empty/whitespace name,
      positive id)
- [x] 12.3 Require the `[AkkaSerializer<TProtocol>]` protocol type argument to be an interface
      (AKKASG033 `ProtocolTypeMustBeInterface`)
- [x] 12.4 Validate the `[AkkaSerializer]` class itself is `partial`, non-generic, and derives from
      `AkkaSerializer` (AKKASG032 `InvalidSerializerShape`)
- [x] 12.5 Reject two `[AkkaSerializer]` classes binding the same protocol interface (AKKASG031
      `DuplicateProtocolBinding`)
- [x] 12.6 Reject a type that implements a bound protocol interface but is not `[AkkaSerializable]`,
      closing a gap where such a type was previously invisible to generated dispatch and failed only
      at runtime on first send (AKKASG029 `ProtocolMessageNotSerializable`)
- [x] 12.7 Make `[AkkaSerializerFormatter<TTarget, TFormatter>]` generic with a
      `where TFormatter : IAkkaMessagePackFormatter<TTarget>` constraint so interface conformance is
      a compiler error at the attribute usage site instead of a generator diagnostic; AKKASG008
      narrows to the one thing the constraint cannot express (`TFormatter` must not be abstract) and
      AKKASG011 narrows to arrays and closed generics (an open generic can no longer reach this check
      either, since C# does not allow an unbound generic type as an attribute type argument)
- [x] 12.8 Remove `global::`-qualified type names from hand-written runtime code and generator
      diagnostic message text in favor of human-readable names (`ToDisplayName`; commit `278e9a4a0`)

## 13. Schemas From Referenced-Assembly Metadata (Decision 16)

Stacks on S1-S6 (`feature/serialization-v2-generator-locations`, PRs #8525/#8526/#8527/#8528/#8530/#8532/#8533).
F1 of the follow-on cross-assembly work: builds a nested field's or a union member's schema from a
referenced assembly's compiled metadata, closing the gap `CrossAssemblyBaselineSpec` pinned. Does
not implement Decisions 17-21 (later PRs).

- [x] 13.1 Add a per-compilation metadata-schema stage (`ComputeMetadataSchemas`,
      `AkkaSerializerGenerator.MetadataSchemas.cs`): resolves every non-generic, foreign-assembly
      type a local message's nested field or union member names, checks it is `[AkkaSerializable]`
      and accessible, and extracts its schema through the same `ExtractMessageCore` a local type
      uses. Walks nested foreign references breadth-first, so a type nested arbitrarily deep in a
      referenced assembly still resolves. New model: `MetadataSchemaTable`
      (`AkkaSerializerGenerator.Models.cs`), symbol-free and value-equatable, wired into
      `ResolveSerializerMessages` beneath local/closed-generic messages (local declarations keep
      priority) so every downstream stage (reachability, union planning, emission) treats a metadata
      schema exactly like a local one
- [x] 13.2 Add AKKASG039 (`NestedFieldNotAccessibleCrossAssembly` /
      `UnionMemberNotAccessibleCrossAssembly`, Error): a referenced type carries `[AkkaSerializable]`
      but this compilation cannot see it, or one of its own `[AkkaField]` properties, or a type it
      itself nests. Type-level accessibility uses `Compilation.IsSymbolAccessibleWithin`; member-level
      accessibility is read back from `ExtractMessageCore`'s own `InvalidFields`/`ConstructionPlan.Errors`
      output (a wholly inaccessible member is invisible to `GetMembers()` and cannot be diagnosed at
      all -- see design.md Decision 16's implementation addendum). A nested failure propagates to a
      fixed point, so a problem one level down is attributed to the actual broken type while still
      reporting at the local reference site
- [x] 13.3 Confirm the existing AKKASG023/AKKASG007/AKKASG015 mislabel fix (a prior branch) still
      reports correctly once metadata schemas exist for the residual "not `[AkkaSerializable]`
      anywhere" failure path; no code change needed, covered by `CrossAssemblyBaselineSpec`
- [x] 13.4 Flip `CrossAssemblyBaselineSpec`'s nested-field and union-member cases from a pinned
      failure to a pinned success, rewriting their ASCII diagrams; add three new AKKASG039 cases
      (non-public property, an internal union member, and one level down). The other four baseline
      cases (generic definition, unreachable closed-generic registration, envelope payload on a
      generic property, protocol implementor only in a referenced assembly) stay pinned, unaffected
      by this decision
- [x] 13.5 Add cross-assembly golden-output coverage (`CrossAssemblyGoldenOutputSpec.cs`): a nested
      field, a union member, and a closed generic from a referenced assembly, pinned against a
      checked-in baseline and proven byte-identical to the same declarations made locally except for
      the namespace
- [x] 13.6 Add the `MetadataSchemas` tracking name and two caching-proof scenarios to
      `GeneratorIncrementalScenariosSpec.cs`: an unrelated edit with a metadata schema in play
      re-emits nothing, and editing the local message that references it re-emits only the owning
      serializer
- [x] 13.7 Document cross-assembly support in the user guide (new "Cross-Assembly Types" section,
      AKKASG039 in the diagnostics table, updated Limitations section) and record the implementation
      choices the design text did not cover as an addendum under Decision 16 in design.md

## 14. Closed-Set Expansion And Adoption On The Serializer (Decisions 17 And 18)

Stacks on F1 (`feature/serialization-v2-metadata-schemas`, PR #8534). F2 of the follow-on
cross-assembly work. Decision 17 ("Unknown Union Members: The Generator Throws, The Caller
Decides") needed no code change: the generated union write/read dispatch already throws
`SerializationException` on an unmatched runtime type or an unrecognized manifest, confirmed by
inspection of `GenerateUnionWrite`/`GenerateUnionRead` and unchanged by this phase. Decision 18
("Closed-Set Expansion And Adoption On The Serializer") is the substance of this phase. Does not
implement Decisions 19 or 21 (the referenced-assembly implementor walk and the union-without-a-
member-list form; later PRs) -- the closed set `ManifestPrefix` expands over, and the walk a
one-owner conflict compares against, are both scoped to the current compilation only.

- [x] 14.1 Add `ManifestPrefix` to `AkkaSerializableAttribute<TMessage>` (extend-only; `Attributes.cs`),
      alongside the existing `Manifest`. `ClosedGenericRegistrationInfo` gains `ManifestPrefix`,
      `ExpansionGroup` (empty for a directly-written registration; the base target's display name for
      a synthesized expansion member), and `ExpansionError` (non-empty when a `ManifestPrefix`
      registration could not expand)
- [x] 14.2 Relax `ExtractClosedGenericRegistrations`'s target validity check to accept a non-generic
      concrete type (Decision 18's adoption rule), not only a closed generic construction; AKKASG020
      stops rejecting a non-generic type argument
- [x] 14.3 Implement `ManifestPrefix` expansion (`ExpandClosedGenericRegistration`,
      `AkkaSerializerGenerator.Extraction.cs`): a type argument's closed set is either the
      serializer's own protocol interface (`TryGetClosedSetMembers`, walking this compilation's own
      non-generic `[AkkaSerializable]` implementors, `ComputeLocalMarkedProtocolImplementors`) or a
      type-level `[AkkaUnion]`'s explicit member list. Every type argument is tested independently, so
      a multi-argument registration expands to the product of its arguments' sets
      (`CartesianProduct`); an argument with no closed set must instead resolve to exactly one
      already-known manifest (`TryResolveFixedArgumentManifest`) -- its own, for an ordinary concrete
      type, or a sibling literal registration's, for a nested generic construction. Each combination
      is `Construct()`-ed off the open generic definition and run through the same `ExtractMessageCore`
      every other schema uses, with a manifest derived by the formula
      `prefix + "/" + string.Join("/", memberManifests)`. An explicit registration for one specific
      construction is skipped during expansion and keeps its own manifest, overriding the derived one
- [x] 14.4 Add AKKASG040 (`ClosedSetExpansionRequiresClosedSet`, Error): a `ManifestPrefix`
      registration whose target has no closed set to expand (not generic, a concrete class, or one
      of its arguments resolves to neither a closed set nor a registered sibling manifest). Gate-level,
      alongside AKKASG020/AKKASG021 in `ValidateClosedGenericRegistrations`
- [x] 14.5 Add AKKASG042 (`ClosedSetExpansionCount`, Info): reported once per successfully-expanding
      `ManifestPrefix` registration, naming the resulting construction count. Unconditional -- the
      design specifies no size threshold
- [x] 14.6 Implement the adoption rule in `ResolveSerializerMessages`
      (`AkkaSerializerGenerator.Emission.cs`): every registered/expanded closed-generic-schema target
      is unconditionally top-level, whether or not it implements the protocol. Retire AKKASG034 (its
      descriptor, `DiagnosticKey` entry, and registry mapping removed; the id stays a permanent gap
      like AKKASG030/AKKASG035) -- the "registration has no effect" condition it guarded can no
      longer occur. `GenerateRegistration` binds the concrete type of every entry in
      `SerializerInfo.ClosedGenericSchemas`, not only the protocol interface, sorted by fully-qualified
      name for deterministic output
- [x] 14.7 Implement the protocol-interface-field-as-union rule in `ResolveMessages`
      (`AkkaSerializerGenerator.Emission.cs`): a field whose static type is exactly the serializer's
      own protocol interface is reclassified from `FieldKind.Unsupported` to a union over the
      protocol closed set, with no `[AkkaUnion]` attribute needed. `MessageInfo` gains `IsSealed`/
      `IsAbstract`/`IsValueType`/`ForeignAssemblyName` (captured once at extraction) so the implicit
      union's `UnionMemberInfo` list needs no symbol access at resolve time; `FieldInfo.WithUnion`
      performs the reclassification. AKKASG003's polymorphic hint text gains the protocol-interface
      option
- [x] 14.8 Add AKKASG041 (`AdoptedMessageOwnedByMultipleSerializers`, Error): the one-owner rule.
      `ComputeMultiOwnedMessages` (`AkkaSerializerGenerator.Emission.cs`) maps every message type to
      its owning serializer(s) -- by protocol membership or by `ClosedGenericSchemas` adoption --
      across the whole compilation; a type with more than one owner is reported at every owning
      serializer's own attribute, from `ReportCrossSerializerDiagnostics`
- [x] 14.9 Fix a duplicate-key crash `ResolveSerializerMessages` would otherwise hit when a
      non-generic type is both ordinarily declared (`declaredMessages`) and separately adopted
      (`ClosedGenericSchemas`): the adopted entry wins and the plain declaration is excluded from the
      concat, preserving declaration order and the registration's own manifest override
- [x] 14.10 Rewrite `CrossAssemblyBaselineSpec`'s case 4 (`Unreachable_closed_generic_registration_of_customer_envelope`)
      from a pinned AKKASG034 failure to a pinned adoption success: the customer's own motivating
      shape now emits, dispatches by its derived/explicit manifest, and gets its own concrete
      `typeof()` binding. Rewrite the AKKASG020/AKKASG034 cases in `GeneratorValidatorSpec.cs` and
      `AkkaSerializerGeneratorDiagnosticsSpec.cs` the same way; add a `Manifest` to the golden
      corpus's nested-only `Pair<int, string>` registration, now that every registration is
      unconditionally top-level too
- [x] 14.11 Add `GeneratedClosedGenericExpansionSpec.cs` (round-trip specs: derived manifest,
      explicit-override manifest, the literal construction's implicit protocol union, a multi-argument
      expansion with a nested fixed-argument manifest lookup, a non-generic non-protocol adoption with
      a manifest override, and reflection-built constructions both inside and outside the registered
      set) and `ClosedGenericExpansionDiagnosticsSpec.cs` (AKKASG040, AKKASG041, AKKASG042, an
      AKKASG012 collision from a derived manifest, and AKKASG003's non-firing for a protocol-interface
      field)
- [x] 14.12 Add `ClosedGenericExpansionGoldenOutputSpec.cs`: golden-output coverage for a derived
      manifest, an explicit override, the implicit protocol union, a nested nested-fixed-argument
      construction, and a concrete type adopted from a referenced assembly, each pinned against a
      checked-in baseline
- [x] 14.13 Document `ManifestPrefix`, the manifest formula (with worked examples), the adoption
      rule, the protocol-interface-field-as-union rule, and the one-owner rule in the user guide (new
      subsections under "Closed Generic Registrations"); update the diagnostics table (AKKASG034
      removed, AKKASG040/041/042 added, AKKASG020/003 descriptions updated); remove the delivered
      `ManifestPrefix` bullet from "Limitations Today and Planned Changes"; add an addendum under
      Decision 18 in design.md for choices the design text left to the implementation

## 15. Referenced-Assembly Implementors, Serializer Placement, And Marked Union Bases (Decisions 19 And 21)

Stacks on F2 (`feature/serialization-v2-expansion-adoption`, commit `ac3376ffa`). F3 of the
follow-on cross-assembly work: the generator finds top-level protocol messages, and a marked
union base's members, in every referenced assembly that itself references
`Akka.Serialization.V2`, not only in the current compilation. A misplaced serializer becomes a
compile-time error or warning instead of a silent gap. Does not migrate `ManifestPrefix`
expansion's own closed-set walk to a shared per-compilation stage; it is cross-assembly-correct
but still runs per-registration, a documented caching-granularity follow-up (see design.md's
Decision 19 addendum).

- [x] 15.1 Move closed-set discovery into `CompilationFacts` (`AkkaSerializerGenerator.Facts.cs`):
      `LocalMarkedImplementorsByClosedSetKey` and a REAL `ReferencedAssemblyImplementorsByProtocol`
      (the F2 skeleton always returned empty), both keyed by every protocol key a collected
      serializer declares and every discovered-mode union field's own static-type key.
      `LocalMarkedImplementorsByClosedSetKey` is a pure filter over the already-collected `messages`
      array (no symbol walk: see 15.13); the referenced-assembly half, and the two `ManifestPrefix`-
      expansion walks in `AkkaSerializerGenerator.Extraction.cs` that still run per registration,
      share one membership test, `ImplementsOrDerivesFromClosedSetKey` (interfaces via
      `AllInterfaces`, a marked abstract class via the base-type chain)
- [x] 15.2 Extend `ComputeMetadataSchemas` (Decision 16's stage, `AkkaSerializerGenerator.MetadataSchemas.cs`)
      to also seed its resolution walk from `CompilationFacts`' referenced-assembly implementor
      keys, so a referenced-assembly top-level implementor gets a full extracted schema the same way
      a locally-named nested field's foreign type already does
- [x] 15.3 Wire `CompilationFacts` and the extended `MetadataSchemas` directly into
      `ResolvedSerializers`' own inputs (previously deferred, per F2's own `TrackingNames.CompilationFacts`
      doc comment); `ResolveSerializerMessages` (`AkkaSerializerGenerator.Emission.cs`) adds every
      referenced-assembly implementor's resolved schema to the same compilation-wide candidate pool
      `declaredMessages` already populates, so the existing top-level filter and the existing
      protocol-interface-implicit-union rule (Decision 18) both widen with no further change
- [x] 15.4 Widen AKKASG029 (protocol coverage) to a new `ReferencedAssemblyUnmarkedImplementorsByProtocol`
      bucket, reported the same way a local unmarked implementor is, at the serializer's own
      attribute. AKKASG012 (manifest uniqueness) widens for free, since it already runs over
      `topLevelMessages`, which now includes the referenced-assembly members
- [x] 15.5 Add `[AkkaUnion]`'s parameterless constructor (extend-only; `Attributes.cs`) and its
      generator support: `ExtractUnionMembers` recognizes zero constructor arguments as "discovered,
      resolve later" (an empty `UnionMembers` array on a `FieldKind.Union` mapping is the unambiguous
      signal, since the explicit form can never produce it); `MessageInfo` gains `BaseTypeNames` (a
      marked abstract-class base needs a class-hierarchy fact `Protocols`, built from
      `AllInterfaces`, cannot carry); `ResolveMessages` generalizes Decision 18's protocol-interface
      reclassification into one shared, per-closed-set-key member cache serving both rules
- [x] 15.6 Add AKKASG043 (`ProtocolOwnedUpstream`, Error) and AKKASG044 (`SerializerHasNoMessages`,
      Warning); extend AKKASG031 (`DuplicateProtocolBindingCrossAssembly`, a second message-text
      variant sharing the existing id, matching the decision page's own "AKKASG031 across assemblies
      (extended)" labeling) across assemblies. All three reported from a new diagnostics-only output,
      `ReportPlacementDiagnostics` (`AkkaSerializerGenerator.Placement.cs`), consuming the collected
      serializers and messages plus `CompilationFacts` (including a new
      `UpstreamSerializerBindingsByProtocol` walk for `[AkkaSerializer<TProtocol>]` declarations found
      in a referenced assembly). The fourth placement rule (the runtime "unsupported generated
      serializer type" exception) carries no id; its text now names both the failing value's own
      runtime assembly and the assembly the serializer was generated in
      (`SerializerInfo.CompilationAssemblyName`, new)
- [x] 15.7 Add the startup one-owner check to `SerializerRegistration.CreateSetup` (`src/core/Akka.Serialization.V2/SerializerRegistration.cs`):
      one dictionary pass over every composed registration's own `UseFor` set, throwing naming both
      colliding registrations' aliases, run once when registrations are composed
- [x] 15.8 Flip `CrossAssemblyBaselineSpec`'s case 6 (`Protocol_implementor_declared_only_in_referenced_assembly`)
      from a pinned "invisible, no dispatch arm" failure to a pinned success: the referenced-assembly
      implementor now gets a Manifest dispatch arm, a `typeof()` binding, and its own generated helpers
- [x] 15.9 Add `MarkedUnionBaseSpec.cs` (local discovery, cross-assembly discovery, the empty-set
      exemption, and the explicit list's regression coverage), `PlacementDiagnosticsSpec.cs`
      (AKKASG043, AKKASG044, the extended AKKASG031, the widened AKKASG029, and the improved runtime
      exception text), and `SerializerRegistrationOneOwnerSpec.cs` (the startup check, disjoint and
      colliding registrations, three-or-more registrations)
- [x] 15.10 Add `ReferencedAssemblyImplementorGoldenOutputSpec.cs`: three golden cases pinned against
      checked-in baselines -- a referenced-assembly top-level implementor, a marked union base with
      implementors on both sides of the boundary, and a `ManifestPrefix` expansion whose closed set
      spans the boundary
- [x] 15.11 Add a caching-proof scenario to `GeneratorIncrementalScenariosSpec.cs`: adding a new
      local marked implementor of a protocol marks `CompilationFacts` Modified and re-emits only the
      serializer whose closed set changed, leaving the other serializer's own resolved model
      `Unchanged`
- [x] 15.12 Update the user guide: a new "Marked Union Bases" subsection, the "Cross-Assembly Types"
      section rewritten for the real walk, a new "Serializer Placement" section (the four rules) and
      "The Startup One-Owner Check" subsection, the diagnostics table gains AKKASG043/044 and
      AKKASG031's extended wording, and the delivered "discovery of protocol implementors" bullet is
      removed from "Limitations Today and Planned Changes"; add addenda under Decisions 19 and 21 in
      design.md for choices the design text and the maintainer's decision-record page left to the
      implementation, including the one place the page and this design text disagree (Rule 1's fix
      list naming an unimplemented serializer part)
- [x] 15.13 Fix an allocation regression an initial pass at 15.1 left in place (up to ~20% over
      baseline on the `SourceGeneratorBenchmarks` corpus, whose reference set includes
      `Akka.Remote`): `ComputeLocalMarkedImplementorsFromMessages` replaces a redundant local symbol
      walk with a pure filter over the collected `messages` array, and `ComputeReferencedAssemblyFacts`
      (the genuinely-needed referenced-assembly symbol walk) is memoized in a process-lifetime
      `ConditionalWeakTable`, so it runs once per reference rather than once per edit. Re-measured
      against `ac3376ffa`: all four benchmark rows' allocations landed within ~1% of baseline
      (previously 7-20% over)
- [x] 15.14 Fix a real (not flaky-only-in-appearance) cache miss 15.13's cache left in place: keying
      on `IAssemblySymbol` relies on Roslyn reusing the same symbol instance across compilations,
      which only happens while an earlier bound symbol for the reference is still reachable through
      Roslyn's own weak symbol cache -- observed to fail under memory pressure, re-walking on every
      edit in exactly the conditions an IDE creates constantly. Rekeyed on
      `Compilation.GetMetadataReference(assemblySymbol)`, falling back to the assembly symbol only
      when a compilation has no separate reference for it. Proven by a counter-based test asserting a
      delta across a three-run sequence (`GeneratorCompilationFactsSpec.ReferencedAssemblyWalk_is_cached_per_metadata_reference_not_per_assembly_symbol`):
      zero additional walks across an edit reusing the same reference, exactly one more against a
      freshly compiled, distinct reference over identical source -- and neutralizes an incidental
      confound in the test harness itself (its own base reference set independently qualifies the
      executing test assembly for this same walk) by warming that entry before measuring

## 16. ManifestPrefix Expansion Hoisted To A Per-Compilation Stage (S7)

Stacks on F3 (`feature/serialization-v2-implementor-walk`, commit `ab82136da`). Closes the
caching-granularity gap 15's own intro text and design.md's F3 addendum both flagged and
deliberately deferred: `ManifestPrefix` expansion (Decision 18) still ran its own closed-set walk
and construction/schema extraction once per registration, inside the per-node serializer
extraction transform, instead of sharing `CompilationFacts`' own cached buckets the way top-level
dispatch and the implicit-union rules already do. Behavior-neutral: no new diagnostic, no changed
diagnostic text, no changed generated output for any existing fixture.

- [x] 16.1 Replace `ExpandClosedGenericRegistration`/`TryGetClosedSetMembers`/`AddDiscoveredClosedSetMembers`/
      `ComputeLocalMarkedProtocolImplementors`/`EnumerateReferencedAssemblyMarkedImplementors`
      (`AkkaSerializerGenerator.Extraction.cs`) with `BuildPrefixExpansionSpec`/`TryClassifyPrefixArgumentPosition`:
      classifies each `ManifestPrefix` target's own type-argument position from the argument's own
      symbol alone (no walk of the compilation's declared types), recording a light
      `PrefixExpansionSpec` (`SerializerInfo.PrefixExpansions`) for a registration with at least one
      protocol- or `[AkkaUnion]`-flavored position. An invalid target, an unresolvable fixed
      argument, or no closed-set-flavored position at all is still resolved as an AKKASG040 error
      entirely in the transform, exactly as before -- none of those depend on a whole-compilation
      walk. `ImplementsOrDerivesFromClosedSetKey` (`AkkaSerializerGenerator.Facts.cs`), whose only
      callers were the two removed walks, is deleted alongside them
- [x] 16.2 Add the S7 expansion stage, `ComputeClosedGenericExpansions` (`AkkaSerializerGenerator.Expansion.cs`),
      a per-compilation stage next to `CompilationFacts`/`ComputeMetadataSchemas`: consumes the
      collected serializers' own `PrefixExpansions` plus `CompilationFacts`' `LocalMarkedImplementorsByClosedSetKey`/
      `ReferencedAssemblyImplementorsByProtocol` buckets, re-resolves each candidate `TypeKey` back
      to a symbol (`ResolveTypeSymbol`, recursing through `TypeKey.TypeArguments` for a candidate
      that is itself a closed generic construction -- `Compilation.GetTypeByMetadataName` alone only
      ever resolves a type's own open definition), runs the same cartesian product and manifest
      formula F2 ran inline, and extracts each construction's schema through the same
      `ExtractMessageCore`. `ComputeDistinctClosedSetKeys` (`AkkaSerializerGenerator.Facts.cs`) is
      extended to also request every `PrefixExpansionSpec`'s own discovered-mode keys, so
      `CompilationFacts`' shared buckets already cover them. Tracking name
      `TrackingNames.ClosedGenericExpansions`, added to `TrackingNames.All`
      (`GeneratorIncrementalScenariosSpec`'s stage-count pin updated 9 -> 10)
- [x] 16.3 Merge the expansion stage's own table back into each `SerializerInfo` with ONE function,
      `SerializerInfo.WithClosedGenericExpansion` (called from `MergeSerializerClosedGenericExpansions`,
      producing `effectiveSerializers`, which `ComputeMetadataSchemas`, the per-serializer resolve
      stage, and every diagnostics output now combine instead of the raw collected array), splicing
      each `PrefixExpansionGroup` back into `ClosedGenericRegistrations`/`ClosedGenericSchemas` at
      the exact insertion point (`PrefixExpansionSpec.RegistrationInsertionIndex`/`SchemaInsertionIndex`,
      `SpliceExpansionGroups<T>`) F2/F3's own inline, per-attribute expansion left them at -- an
      append-only first version of this merge reordered the golden corpus's own dispatch switch and
      failed the byte-identical gate
- [x] 16.4 Redirect `MessageTypeLocationKey` (`AkkaSerializerGenerator.Locations.cs`) to resolve an
      expansion member's own type-level location through `ClosedGenericRegistrationInfo.ExpansionGroup`
      (the base attribute's own location entry `BuildSerializerLocationBag` already records) instead
      of a per-member entry, so the expansion stage needs no attribute-location plumbing of its own;
      field-level locations for an expanded member's own `[AkkaField]` properties still resolve
      through `ExtractMessageCore`'s existing inlined capture, carried in the expansion stage's own
      raw `LocationBag` half (`ExpandedPrefixRegistrations`) and merged into `allLocations`
- [x] 16.5 Add three caching-proof scenarios to `GeneratorIncrementalScenariosSpec.cs` over a new
      fixture with a `ManifestPrefix` registration (scenarios (i)/(j)/(k)): an unrelated edit and a
      whitespace edit inside a message both leave `ResolvedSerializers` `Cached` and re-emit nothing
      (the expansion stage's own tracked, table-only projection reports `Cached` when its raw input
      compares equal -- the same "Unchanged upstream lets a trivial downstream projection go
      Cached" shape `SerializerSchemas`/`MessageSchemas` already document -- or `Unchanged` when a
      location-only shift forces the raw stage to rerun but its own table half still compares
      equal); adding a new local marked implementor of the expanded protocol marks `CompilationFacts`
      and the expansion stage `Modified` and re-emits only the serializer whose closed set changed
- [x] 16.6 Add a `PrefixVariant` corpus variant to `SourceGeneratorBenchmarks`/`SourceGeneratorBenchmarkCorpus`
      (`src/benchmark/Akka.Benchmarks/Serialization/SourceGeneratorBenchmarks.cs`): one serializer
      additionally registers `Envelope0<IProtocol0>` with `ManifestPrefix`, expanding over all 100 of
      its own protocol messages. Measured against `ab82136da` (ShortRun, same machine, back to
      back): the existing (non-prefix) corpus lands within ~1% on allocations for every row; the
      prefix variant's rows that do not force a re-emission (trailing whitespace/comment edit,
      unrelated file edited) drop ~8-9% on allocations, and the fresh/cold run drops ~2% (one fewer
      redundant walk over the same closed set `CompilationFacts` already covers for top-level
      dispatch); the field-rename row drops a smaller ~3% because it still forces a genuine
      re-emission of the owning serializer's file (the renamed message's own field is part of the
      emitted text), independent of this hoist. Wall-clock (`Mean`) numbers were noisy under
      `ShortRun`'s three iterations on a shared machine; `Allocated` (deterministic per run) is the
      metric actually compared
- [x] 16.7 Add design.md's own S7 addendum under Decision 18: what moved, where it lives now, the
      merge point, why an append-only first version of the merge failed the byte-identical gate and
      needed a splice instead, the `ResolveTypeSymbol` fix for a nested closed-generic candidate, and
      the run-reason shape for the three new caching-proof scenarios
