## Wire Shape To Preserve

Classic remoting currently sends length-framed TCP payloads containing an outer protobuf `AkkaProtocolMessage`. User messages are carried as `AkkaProtocolMessage.Payload`, which contains a protobuf `AckAndEnvelopeContainer`, which contains a `RemoteEnvelope`, which contains a protobuf serializer `Payload` with serializer id, manifest bytes, and user payload bytes.

The first stream pipeline slice must preserve that wire shape exactly.

## Outbound Hot Path

| Step | Current boundary | Copy / allocation point | Compatibility note |
|------|------------------|-------------------------|--------------------|
| 1 | `EndpointWriter.WriteSend` receives `EndpointManager.Send` and builds the inner Remote PDU. See `src/core/Akka.Remote/Endpoint.cs:1477` and `src/core/Akka.Remote/Endpoint.cs:1494`. | Allocates remoting envelope state before handoff to the codec. | Required by current endpoint actor flow. |
| 2 | `EndpointWriter.SerializeMessage` delegates to `MessageSerializer.Serialize`. See `src/core/Akka.Remote/Endpoint.cs:1339` and `src/core/Akka.Remote/MessageSerializer.cs:46`. | `serializer.ToBinary(message)` creates a `byte[]`; `ByteString.CopyFrom(...)` copies payload bytes into protobuf `ByteString`. See `src/core/Akka.Remote/MessageSerializer.cs:58`. | The `byte[]` is required for legacy `Serializer`; native `SerializerV2` can target `IBufferWriter<byte>`. |
| 3 | `AkkaPduProtobuffCodec.ConstructMessage` builds `AckAndEnvelopeContainer`. See `src/core/Akka.Remote/Transport/AkkaPduCodec.cs:477`. | Generated protobuf objects are allocated; `ackAndEnvelope.ToByteString()` materializes the complete inner envelope. See `src/core/Akka.Remote/Transport/AkkaPduCodec.cs:488`. | Current `ConstructMessage` returns `ByteString`; a writer-oriented API can preserve bytes without this intermediate container. |
| 4 | `AkkaProtocolHandle.Write` wraps the inner envelope in outer `AkkaProtocolMessage`. See `src/core/Akka.Remote/Transport/AkkaProtocolTransport.cs:414`. | `ConstructPayload` creates `new AkkaProtocolMessage { Payload = payload }.ToByteString()`. See `src/core/Akka.Remote/Transport/AkkaPduCodec.cs:344`. | Required by current `AssociationHandle.Write(ByteString)` contract. |
| 5 | DotNetty TCP handle writes the final payload. See `src/core/Akka.Remote/Transport/DotNetty/TcpTransport.cs:254`. | `payload.ToByteArray()` copies the outer protobuf `ByteString` before `Unpooled.WrappedBuffer(...)`. See `src/core/Akka.Remote/Transport/DotNetty/TcpTransport.cs:268`. | Transport SPI refactor target; not required by wire format. |
| 6 | DotNetty pipeline prepends the TCP length field. See `src/core/Akka.Remote/Transport/DotNetty/DotNettyTransport.cs:340` and `src/core/Akka.Remote/Transport/DotNetty/DotNettyTransport.cs:344`. | Helios compatibility framing has an explicit combined-buffer copy path. See `src/core/Akka.Remote/Transport/DotNetty/DotNettyTransport.cs:651`. | Length framing must remain; copy behavior can change if bytes are equivalent. |

## Inbound Hot Path

| Step | Current boundary | Copy / allocation point | Compatibility note |
|------|------------------|-------------------------|--------------------|
| 1 | DotNetty strips the 4-byte length prefix and passes an `IByteBuffer` frame. See `src/core/Akka.Remote/Transport/DotNetty/DotNettyTransport.cs:337`. | `ByteString.CopyFrom(buf.Array, ...)` copies the full frame into immutable protobuf bytes, then wraps it in `InboundPayload`. See `src/core/Akka.Remote/Transport/DotNetty/TcpTransport.cs:73` and `src/core/Akka.Remote/Transport/DotNetty/TcpTransport.cs:74`. | Required by current `InboundPayload(ByteString)` contract. |
| 2 | `ProtocolStateActor` decodes the outer `AkkaProtocolMessage`. See `src/core/Akka.Remote/Transport/AkkaProtocolTransport.cs:1300` and `src/core/Akka.Remote/Transport/AkkaPduCodec.cs:322`. | `AkkaProtocolMessage.Parser.ParseFrom(raw)` materializes the outer protobuf object; ordinary payloads allocate `new Payload(pdu.Payload)`. See `src/core/Akka.Remote/Transport/AkkaPduCodec.cs:326` and `src/core/Akka.Remote/Transport/AkkaPduCodec.cs:328`. | Refactor target: parse outer tag and expose payload as a slice/sequence. |
| 3 | Open protocol state forwards payloads to the endpoint listener. See `src/core/Akka.Remote/Transport/AkkaProtocolTransport.cs:1002`. | Payload `ByteString`s may be queued before listener registration and replayed later. | Behavior must be preserved before replacing actor FSM hot-path handling. |
| 4 | `EndpointReader` decodes `AckAndEnvelopeContainer`. See `src/core/Akka.Remote/Endpoint.cs:1838`, `src/core/Akka.Remote/Endpoint.cs:1969`, and `src/core/Akka.Remote/Transport/AkkaPduCodec.cs:413`. | `AckAndEnvelopeContainer.Parser.ParseFrom(raw)` materializes inner protobuf objects, ACK collections, actor refs, and remoting `Message` / `AckAndMessage` wrappers. See `src/core/Akka.Remote/Transport/AkkaPduCodec.cs:415` through `src/core/Akka.Remote/Transport/AkkaPduCodec.cs:455`. | Refactor target: decode headers and expose serialized user payload bytes as a sequence. |
| 5 | `MessageSerializer.Deserialize` recovers the user message. See `src/core/Akka.Remote/MessageSerializer.cs:30`. | `messageProtocol.Message.ToByteArray()` copies protobuf payload bytes for serializer input; manifest bytes become a string. See `src/core/Akka.Remote/MessageSerializer.cs:34`. | Required for legacy serializers; native V2 can use `Serialization.Deserialize(ReadOnlySequence<byte>, ...)`. |
| 6 | `EndpointReader` dispatches decoded user messages. See `src/core/Akka.Remote/Endpoint.cs:1843` through `src/core/Akka.Remote/Endpoint.cs:1855`. | Reliable-delivery buffering allocates around decoded `Message` wrappers before dispatch and ACK. | Protocol semantics must remain unchanged. |

## Primary Refactor Targets

- Add `AkkaPduCodec` APIs that read `ReadOnlySequence<byte>` and write to `IBufferWriter<byte>` while keeping `ByteString` adapters.
- Keep legacy serializer copies inside `SerializerV1Adapter`; route native serializers through `SerializerV2.Serialize` and sequence-based deserialize where lifetime is safe.
- Avoid re-wrapping inner and outer protobuf PDUs as separate `ByteString` instances when a single writer-owned frame buffer can preserve the exact bytes.
- Avoid adapting the future stream pipeline back through `AssociationHandle.Write(ByteString)` and `InboundPayload(ByteString)` as the primary hot-path boundary, or most existing copies remain.
