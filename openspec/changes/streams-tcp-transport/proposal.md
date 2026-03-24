## Why

DotNetty's `ByteBuf` is incompatible with `System.Memory`, making it a dead end for zero-copy serialization and transport. With Spec 1 delivering `System.Memory` + `Stream` + `Pipe` in Akka.IO and Spec 2 adding TLS via `IStreamProvider`, we can replace the DotNetty transport with one built on Akka.Streams TCP. This eliminates the DotNetty dependency entirely and enables end-to-end zero-copy: `IBufferWriter<byte>` flows from the serializer through framing to the socket in a single buffer with no intermediate copies.

## What Changes

- New `StreamsTcpTransport : Transport` implementation in Akka.Remote using Akka.Streams TCP
- **Wire-compatible**: same `[4-byte little-endian length][payload]` framing as DotNetty
- **Config-compatible**: all `akka.remote.dot-netty.tcp.*` HOCON keys continue to work (mapped to new transport settings)
- **Behaviorally compatible**: `AkkaProtocolTransport` adapter layer (handshake, heartbeats, association management) sits on top unchanged
- New `FrameBufferWriter : IBufferWriter<byte>` — lightweight wrapper over pooled `byte[]` with offset, enabling integrated framing + serialization in a single buffer write (reserve 4 bytes for length header, write PDU + payload, backfill length)
- Replace `AkkaPduProtobuffCodec` (Protobuf-based PDU encoding) with simple binary PDU encoding directly into `IBufferWriter<byte>` (serializerId as int32, manifest as length-prefixed UTF8, payload bytes)
- Remove DotNetty NuGet dependency from Akka.Remote
- **BREAKING**: `DotNettySslSetup` replaced by `TlsSetup` (Spec 2)
- **BREAKING**: DotNetty-specific programmatic APIs removed

### What does NOT change

- `AkkaProtocolTransport` adapter layer (handshake, heartbeats, acking, association state machine)
- The abstract `Transport` base class and `AssociationHandle` contract
- Akka.Remote's `Endpoint` / `EndpointWriter` / `EndpointReader` actor hierarchy (though `EndpointWriter` internals change to use `IBufferWriter<byte>`)
- Wire format on the network (same 4-byte length framing, same SerializerId + Manifest + Payload structure)
- All HOCON configuration keys (names preserved, implementation remapped)

## Capabilities

### New Capabilities

- `streams-tcp-transport`: Akka.Streams-based TCP transport replacing DotNetty. Covers the `Transport` implementation, `AssociationHandle`, integrated framing + serialization via `FrameBufferWriter`, binary PDU encoding, configuration mapping from DotNetty HOCON, and DotNetty dependency removal.

### Modified Capabilities

## Impact

- **Akka.Remote** (`src/core/Akka.Remote/`): New transport implementation, `FrameBufferWriter`, binary PDU codec. Remove `Transport/DotNetty/` directory entirely. Update `MessageSerializer.cs` and `Endpoint.cs` for `IBufferWriter<byte>` write path.
- **Configuration**: All `akka.remote.dot-netty.tcp.*` keys remapped to `StreamsTcpTransportSettings`. Default transport class changes from `TcpTransport` (DotNetty) to `StreamsTcpTransport`.
- **NuGet dependencies**: Remove `DotNetty.Transport`, `DotNetty.Codecs`, `DotNetty.Handlers`, `DotNetty.Common`, `DotNetty.Buffers`. Add dependency on `Akka.Streams` from `Akka.Remote`.
- **Test suites**: All Akka.Remote specs that don't directly reference DotNetty APIs must pass. DotNetty-specific tests removed.
- **Downstream**: Spec 4 (SerializerV2) `Serialize(IBufferWriter<byte>)` writes directly into `FrameBufferWriter`. Spec 5 (Performance) benchmarks this transport.
