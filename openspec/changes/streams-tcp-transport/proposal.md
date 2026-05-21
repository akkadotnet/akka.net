## Why

DotNetty's `ByteBuf` is incompatible with `System.Memory`, making it a dead end for integrated serialization and transport. Spec 1 delivered `System.Memory` + `Stream` + `Pipe` in Akka.IO and Spec 2 added TLS via `IStreamProvider`, which makes an Akka.Streams-based TCP transport viable. PR #8203 also showed that serializer improvements alone are not enough: Akka.Remote's outbound path still splits serialization, envelope construction, protocol wrapping, and transport framing into separate allocation-heavy stages.

The next transport milestone therefore needs to do more than swap DotNetty for Akka.Streams. It needs to collapse the outbound write path into one transport-owned loop:

- dequeue a send-shaped work item
- reserve framing space
- write envelope metadata
- serialize the payload directly into the same writer
- write the protocol wrapper and outer frame
- flush the completed bytes to the transport

That is where buffer pooling is practical. Read-side pooling is intentionally out of scope for this milestone because inbound payload bytes can outlive the transport and become actor state indefinitely.

## What Changes

- New `StreamsTcpTransport : Transport` implementation in Akka.Remote using Akka.Streams TCP
- **Wire-compatible first production slice**: preserve the current remoting wire format end-to-end while redesigning the outbound path
- **Config-compatible**: all `akka.remote.dot-netty.tcp.*` HOCON keys continue to work (mapped to new transport settings)
- `AkkaProtocolTransport` remains the handshake / heartbeat / association-management layer, but the outbound write contract below it is allowed to change for performance
- New transport-owned outbound writer loop that lowers serialization and protocol framing into a single buffer construction path
- New pooled `FrameBufferWriter : IBufferWriter<byte>` for outbound writes only
- Replace the current multi-stage `MessageSerializer` -> `AkkaPduCodec.ConstructMessage` -> `AkkaProtocolHandle.Write` write path with one integrated path that writes the current wire format directly to the transport-owned writer
- Remove DotNetty NuGet dependency from Akka.Remote
- **BREAKING**: the current `AssociationHandle.Write(ByteString)` / `InboundPayload(ByteString)` assumptions are no longer design constraints for the new transport implementation
- **BREAKING**: source-compatible C# transport/setup APIs are not a goal if they block the faster outbound regime
- **BREAKING**: `DotNettySslSetup` replaced by `TlsSetup` (Spec 2)
- **BREAKING**: DotNetty-specific programmatic APIs removed

### What does NOT change

- `AkkaProtocolTransport` remains the layer responsible for handshake, heartbeats, acking, and association state management
- Akka.Remote's actor-level semantics stay the same: sends are still driven by send-shaped work items and inbound payloads still become ordinary actor messages after deserialization
- Read-side transport behavior remains copy-based before actor-visible lifetime begins
- The remoting wire format stays compatible in the first production redesign
- All HOCON configuration keys (names preserved, implementation remapped)

## Capabilities

### New Capabilities

- `streams-tcp-transport`: Akka.Streams-based TCP transport replacing DotNetty. Covers the transport implementation, integrated outbound framing + serialization via a transport-owned `FrameBufferWriter`, configuration mapping from DotNetty HOCON, and DotNetty dependency removal.

### Modified Capabilities

## Impact

- **Akka.Remote** (`src/core/Akka.Remote/`): New transport implementation and outbound writer pipeline. Remove `Transport/DotNetty/` directory entirely. Update `EndpointWriter`, `MessageSerializer`, and transport abstractions to support transport-owned outbound buffer construction while preserving the current wire format.
- **Configuration**: All `akka.remote.dot-netty.tcp.*` keys remapped to `StreamsTcpTransportSettings`. Default transport class changes from `TcpTransport` (DotNetty) to `StreamsTcpTransport`.
- **NuGet dependencies**: Remove `DotNetty.Transport`, `DotNetty.Codecs`, `DotNetty.Handlers`, `DotNetty.Common`, `DotNetty.Buffers`. Add dependency on `Akka.Streams` from `Akka.Remote`.
- **Benchmarks**: Add a bounded spike that compares the current split outbound pipeline against an integrated transport-owned write loop before the full transport rewrite is attempted, then benchmark the first wire-compatible redesigned transport before any compatibility follow-up work.
- **Test suites**: All Akka.Remote specs that don't directly reference DotNetty APIs must pass. DotNetty-specific tests removed.
- **Downstream**: Spec 4 (SerializerV2) is now explicitly a foundation for the integrated outbound path, not a standalone end-state. Spec 5 benchmarks both the spike and the final transport.
