## Why

Akka.Remote still spends too much of its hot path materializing byte buffers, routing frames through legacy transport boundaries, and running protocol state transitions through actor mailbox hops. SerializerV2 and the modernized Akka.IO.Tcp / Akka.Streams stack give us the primitives to redesign the C# internals while preserving the existing remote wire format.

## What Changes

- Introduce a stream-oriented Akka.Remote protocol pipeline that uses Akka.Streams / Akka.IO.Tcp as the transport substrate.
- Preserve the existing Akka.Remote TCP framing and protobuf wire format for the first production slice.
- Reshape the PDU codec around `ReadOnlySequence<byte>` input and `IBufferWriter<byte>` output while keeping existing `ByteString` paths as compatibility adapters during migration.
- Move Remote payload serialization toward the `SerializerV2` contract and rely on `SerializerV1Adapter` for legacy serializers instead of adding separate V1 special cases in the Remote pipeline.
- Revisit `AkkaProtocolTransport`, `ProtocolStateActor`, and endpoint listener registration so protocol state transitions can be expressed as stream-friendly state machines without changing remoting semantics.
- Add baseline and regression benchmarks for the current and redesigned Remote hot paths.
- Do not implement a new standalone PipeTransport in this change; Akka.IO.Tcp already owns the pipe and socket mechanics.
- Do not introduce MessagePack PDU envelopes or new built-in serializer IDs in the first slice; those remain future opt-in changes.

## Capabilities

### New Capabilities

- `remote-protocol-pipeline`: Defines a wire-compatible, stream-oriented Akka.Remote protocol pipeline over Akka.Streams / Akka.IO.Tcp, including PDU codec shape, SerializerV2 payload boundaries, protocol state-machine behavior, and benchmark gates.

### Modified Capabilities

No existing OpenSpec capabilities are modified.

## Impact

- Affected code: `src/core/Akka.Remote`, especially `EndpointWriter`, `EndpointReader`, `MessageSerializer`, `AkkaPduCodec`, `AkkaProtocolTransport`, `ProtocolStateActor`, and Remote benchmarks.
- Affected dependencies: no new transport dependency is expected; Akka.Remote should use existing Akka.Streams / Akka.IO.Tcp infrastructure.
- Compatibility: existing Akka.Remote wire format and existing serializer IDs must remain readable; the first implementation slice must be opt-in until performance, compatibility, and rolling-upgrade behavior are proven.
- Performance: expected to reduce allocations and copies in Remote message framing and payload serialization while keeping behavior equivalent to the current remoting stack.
