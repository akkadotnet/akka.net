## 1. Baseline And Hot-Path Mapping

- [x] 1.1 Run current `RemotePingPong` baseline on `dev` and record throughput results in this change.
- [x] 1.2 Run current `AkkaPduCodecBenchmark` baseline and record throughput/allocation results in this change.
- [x] 1.3 Map outbound copy/allocation boundaries from `EndpointWriter` through `MessageSerializer`, `AkkaPduCodec`, `AkkaProtocolHandle`, and the transport boundary.
- [x] 1.4 Map inbound copy/allocation boundaries from transport frame receipt through `InboundPayload`, `ProtocolStateActor`, `AkkaPduCodec.DecodeMessage`, and `EndpointReader` dispatch.
- [x] 1.5 Add or update benchmark documentation with baseline commands, environment, and result tables.

## 2. Wire-Compatibility Fixtures

- [x] 2.1 Add golden byte fixtures for `Associate`, `Heartbeat`, and each `DisassociateInfo` control PDU.
- [x] 2.2 Add golden byte fixtures for payload PDU wrapping an `AckAndEnvelopeContainer`.
- [x] 2.3 Add golden byte fixtures for pure ack, ack plus message, and sequenced reliable-delivery message envelopes.
- [x] 2.4 Verify existing `AkkaPduProtobuffCodec` decodes all golden fixtures.
- [x] 2.5 Verify existing `AkkaPduProtobuffCodec` emits byte-compatible output for the supported golden fixture shapes.

## 3. Sequence/Writer PDU Codec Shape

- [x] 3.1 Introduce internal PDU decode APIs that accept `ReadOnlySequence<byte>` without requiring caller-side `ByteString` materialization.
- [x] 3.2 Introduce internal PDU encode APIs that write to caller-owned `IBufferWriter<byte>`.
- [x] 3.3 Keep existing `ByteString` PDU APIs as adapters over the new internal codec path.
- [x] 3.4 Preserve current protobuf wire output for control PDUs and payload PDUs.
- [x] 3.5 Update `AkkaPduCodecBenchmark` to compare legacy adapter calls and sequence/writer calls.

## 4. SerializerV2 Remote Payload Boundary

- [x] 4.1 Update Remote payload serialization design so `MessageSerializer` resolves a `SerializerV2` for outbound payloads.
- [x] 4.2 Route legacy serializers through `SerializerV1Adapter` rather than adding V1-specific branches in Remote code.
- [x] 4.3 Write native V2 payload bytes through `SerializerV2.Serialize` where the current protobuf payload field requires bytes.
- [x] 4.4 Read native V2 payload bytes through `Serialization.Deserialize(ReadOnlySequence<byte>, int, string)` where the payload sequence lifetime is valid.
- [x] 4.5 Preserve serializer id, manifest, and payload byte semantics for old and new payload serializers.
- [x] 4.6 Add tests for V2 payloads and V1-adapted payloads through the Remote message serializer path.

## 5. Protocol State Machine Isolation

- [x] 5.1 Extract protocol transition scenarios from `ProtocolStateActor` into focused tests for handshake, heartbeat, disassociation, quarantine, `refuseUid`, and listener registration.
- [x] 5.2 Identify which `ProtocolStateActor` behavior can be represented as a pure state machine while preserving public remoting behavior.
- [ ] 5.3 Add an internal state-machine model or facade that can be exercised without a live actor mailbox.
- [ ] 5.4 Keep `ProtocolStateActor` authoritative until the extracted model passes equivalence tests.
- [x] 5.5 Document any required listener-registration or pre-listener buffering behavior before replacing actor FSM hot-path handling.

## 6. Stream Protocol Pipeline Spike

- [x] 6.1 Prototype an opt-in Remote protocol pipeline over Akka.Streams / Akka.IO.Tcp without adding a new raw pipe transport.
- [x] 6.2 Use existing Remote protobuf wire format in the opt-in stream pipeline.
- [x] 6.3 Ensure stream framing preserves the same length-framed PDU semantics expected by existing Remote tests.
- [ ] 6.4 Integrate the sequence/writer PDU codec into the stream pipeline.
- [ ] 6.5 Integrate the SerializerV2 payload boundary into the stream pipeline.
- [ ] 6.6 Preserve `EndpointWriter`, `EndpointReader`, and reliable-delivery semantics or document any required internal facade.
- [ ] 6.7 Add a conservative payload-only outer `AkkaProtocolMessage` fast parser over `ReadOnlySequence<byte>`.
- [ ] 6.8 Fall back to generated protobuf parsing for control PDUs, unknown fields, non-canonical field ordering, and malformed fast-path candidates.
- [ ] 6.9 Forward sequence-backed payload PDUs from `ProtocolStateActor` to `EndpointReader` without materializing `InboundPayload(ByteString)` on the established stream inbound path.
- [ ] 6.10 Teach `EndpointReader` to handle `InboundSequencePayload` in both reading and not-reading states while preserving ACK and reliable-delivery behavior.

## 7. Validation And Performance Gates

- [x] 7.1 Run focused `Akka.Remote.Tests` covering `AkkaProtocolSpec`, codec tests, and endpoint send/receive behavior.
- [x] 7.2 Run relevant `Akka.Cluster.Tests` or multi-node smoke tests in legacy mode.
- [ ] 7.3 Run relevant `Akka.Cluster.Tests` or multi-node smoke tests in the opt-in stream pipeline mode when available.
- [x] 7.4 Rerun `RemotePingPong` after each implementation milestone and record result deltas.
- [ ] 7.5 Rerun `AkkaPduCodecBenchmark` after codec milestones and record allocation deltas.
- [x] 7.6 Stop before defaulting the stream pipeline on until benchmarks and compatibility tests justify the change.

## 8. Documentation And Review

- [x] 8.1 Update OpenSpec notes with baseline and final benchmark tables.
- [ ] 8.2 Document rollout behavior, opt-in configuration, and rollback path.
- [ ] 8.3 Document why Akka.Streams / Akka.IO.Tcp is the substrate and why no standalone PipeTransport is added.
- [ ] 8.4 Review public or internal API changes for compatibility before opening a PR.
