## Context

Akka.Remote currently routes outbound user messages through `EndpointWriter`, `MessageSerializer`, `AkkaPduCodec`, `AkkaProtocolHandle`, `AssociationHandle.Write(ByteString)`, and the transport. The inbound path mirrors this through `InboundPayload(ByteString)`, `ProtocolStateActor`, `AkkaPduCodec.DecodeMessage`, and `EndpointReader` dispatch. This design was built around older byte-buffer transport boundaries and makes `ByteString` the handoff format between major layers.

SerializerV2 now provides a buffer-writer and sequence-reader serialization contract. Akka.IO.Tcp was also modernized around stream and pipe primitives, and Akka.Streams already exposes the transport composition layer. Therefore the next Remote performance work should not create another raw pipe transport. It should use Akka.Streams / Akka.IO.Tcp as the transport substrate and focus on the PDU codec, protocol state machine, and serialization boundaries.

The first production slice must preserve the current Remote wire format: length-framed Akka protocol PDUs, protobuf `AkkaProtocolMessage`, protobuf `AckAndEnvelopeContainer`, existing serializer ids, manifests, and payload bytes. The implementation can change C# internals aggressively, but mixed-version and persisted compatibility must not be compromised by this change.

## Goals / Non-Goals

**Goals:**

- Use Akka.Streams / Akka.IO.Tcp as the Remote transport substrate.
- Preserve the existing Akka.Remote wire format in the first slice.
- Redesign `AkkaPduCodec` around `ReadOnlySequence<byte>` input and `IBufferWriter<byte>` output.
- Keep existing `ByteString` PDU methods as adapters during migration and tests.
- Move Remote payload serialization toward a uniform `SerializerV2` model.
- Use `SerializerV1Adapter` for legacy serializers instead of adding V1-specific branches throughout Remote.
- Identify and then reduce hot-path mailbox hops in `AkkaProtocolTransport` / `ProtocolStateActor` without changing handshake, heartbeat, disassociation, quarantine, or reliable-delivery semantics.
- Add baselines and regression benchmarks before changing the protocol pipeline.

**Non-Goals:**

- No new standalone `PipeTransport` implementation.
- No MessagePack PDU envelope in the first slice.
- No Artery implementation in this change.
- No built-in serializer ID migration for Remote, Persistence, Delivery, or DistributedData in this change.
- No removal of existing wire-compatible serializers or existing remoting behavior.
- No public transport API redesign unless a compatibility adapter and migration plan are defined.

## Decisions

### 1. Use Akka.Streams / Akka.IO.Tcp, Not A New Pipe Transport

Akka.IO.Tcp owns socket, stream, and pipe mechanics. Akka.Streams is the composition layer we should use for transport flow. A separate `PipeTransport` duplicates machinery, creates another lifecycle surface, and distracts from the actual bottlenecks in the PDU codec and protocol state machine.

Alternative considered: port To11mtm's `TcpPipeTransport` directly. It is useful as a reference and showed promising throughput, but it still preserved the `AssociationHandle.Write(ByteString)` / `InboundPayload(ByteString)` boundary. That makes it an incremental transport replacement, not the deeper Remote protocol pipeline we need.

### 2. Preserve Wire Format First

The first slice must produce and consume the same bytes as the current Remote implementation for protobuf Akka protocol PDUs and `AckAndEnvelopeContainer`. This lets us isolate C# runtime improvements from wire-compatibility changes and keeps rolling upgrades feasible.

Alternative considered: switch Remote PDU envelopes to MessagePack immediately. That would require coordinated cluster-wide opt-in, new compatibility rules, and new failure modes. It should be evaluated later after the wire-compatible pipeline is measurable.

### 3. Make `AkkaPduCodec` Sequence/Writer-Oriented

The current codec accepts and returns `ByteString`, which forces materialization at the codec boundary. The new primary codec shape should read from `ReadOnlySequence<byte>` and write to `IBufferWriter<byte>`. Existing `ByteString` methods can remain as adapters until callers are migrated.

The wire format can remain protobuf while the implementation becomes allocation-conscious. Hand-written protobuf encode/decode for the small Remote PDU schema is acceptable if it preserves byte compatibility and materially reduces allocations.

### 4. Treat Payload Serializers As `SerializerV2`

Remote should use the V2 serializer abstraction as its payload boundary. Native V2 serializers can write into caller-owned buffers and read from `ReadOnlySequence<byte>`. Legacy serializers should flow through `SerializerV1Adapter`, where unavoidable byte-array copies remain localized.

This keeps Remote code focused on frame ownership, serializer id, manifest, and payload boundaries rather than branching repeatedly on V1 versus V2.

### 5. Redesign Protocol State Independently From Transport

`AkkaProtocolTransport` and `ProtocolStateActor` own handshake, heartbeat, disassociation, quarantine, and listener registration. The initial implementation should keep behavior equivalent while making state transitions testable outside actor mailboxes. A later slice can replace the FSM actor with a stream-friendly state machine or stream stage once equivalence tests are in place.

This sequencing avoids rewriting transport, codec, serializer, and protocol state all at once.

### 6. Benchmarks Are Entry Criteria, Not Exit Notes

Before implementation, record current baselines for RemotePingPong, `AkkaPduCodecBenchmark`, allocation counts, and known copy boundaries. Each code slice must update or add a benchmark that proves whether the change helped.

## Risks / Trade-offs

**Wire compatibility regressions** -> Add byte-for-byte golden tests for current PDU shapes before replacing codec internals.

**Protocol semantic regressions** -> Extract state-machine tests for handshake, heartbeat timeout, disassociation reasons, quarantine, `refuseUid`, and listener registration before removing actor hops.

**Pipe / stream buffer lifetime bugs** -> Do not pass borrowed pipe memory across actor boundaries without ownership. Decode synchronously inside the stream stage or copy into owned memory at explicit compatibility boundaries.

**Over-scoping into Artery** -> Keep this change focused on classic Remote protocol internals and wire compatibility. Artery remains a future transport/protocol design.

**Legacy serializer complexity** -> Route legacy serializers through `SerializerV1Adapter` and keep copies isolated there.

**Performance ambiguity** -> Require benchmarks at each milestone and preserve baseline numbers in the change notes.

## Migration Plan

1. Add baselines and wire-format tests on the current implementation.
2. Introduce sequence/writer PDU codec APIs with `ByteString` adapters and no behavior change.
3. Integrate SerializerV2 payload serialization/deserialization in Remote behind existing wire-compatible payload fields.
4. Add stream-oriented protocol state-machine tests while the actor FSM remains authoritative.
5. Introduce an opt-in stream protocol pipeline that uses Akka.Streams / Akka.IO.Tcp and preserves the wire format.
6. Run existing Remote, Cluster, and multi-node tests in both legacy and opt-in modes before considering a default switch.

Rollback is config-based while the new pipeline is opt-in. The existing Remote path and serializers remain available throughout the change.

## Open Questions

- Should the first stream pipeline reuse `EndpointWriter` / `EndpointReader` directly, or should it introduce a smaller internal endpoint protocol facade first?
- Which PDU shapes need golden byte fixtures before the codec refactor begins?
- Can protocol state be extracted into a pure state machine while keeping `ProtocolStateActor` as a wrapper during migration?
- What is the minimum benchmark improvement required before shipping the opt-in pipeline?
