## 1. FrameBufferWriter

- [ ] 1.1 Create `FrameBufferWriter : IBufferWriter<byte>` in `src/core/Akka.Remote/` — wraps pooled `byte[]` with start offset, implements `GetMemory`/`GetSpan`/`Advance`, supports growth via `ArrayPool`
- [ ] 1.2 Add length-header backfill method: writes total frame length into reserved first 4 bytes
- [ ] 1.3 Add `GetFrameMemory()` method that returns `ReadOnlyMemory<byte>` spanning the complete frame (length header + payload)
- [ ] 1.4 Add `Return()` method that returns the pooled `byte[]` to `ArrayPool`
- [ ] 1.5 Unit tests for FrameBufferWriter: basic write, growth, backfill, pool return

## 2. Binary PDU Codec

- [ ] 2.1 Create `BinaryPduCodec` in `src/core/Akka.Remote/` with `WritePdu(IBufferWriter<byte>, ...)` methods for payload, associate, disassociate, heartbeat PDU types
- [ ] 2.2 Create `ReadPdu(ReadOnlySequence<byte>)` methods that parse PDU type and extract fields
- [ ] 2.3 Define PDU type constants (0x01 payload, 0x02 associate, 0x03 disassociate, 0x04 heartbeat)
- [ ] 2.4 Implement payload PDU: serializerId (int32) + manifest (length-prefixed UTF8) + payload bytes
- [ ] 2.5 Implement control PDUs: associate (with handshake info), disassociate (with reason), heartbeat (minimal)
- [ ] 2.6 Unit tests: round-trip encode/decode for all PDU types, edge cases (empty manifest, large payloads, max frame size)

## 3. StreamsTcpTransport Implementation

- [ ] 3.1 Create `StreamsTcpTransport : Transport` in `src/core/Akka.Remote/Transport/Streams/`
- [ ] 3.2 Implement `Listen()`: use `Tcp.Bind()` to create listener, materialize `Source<IncomingConnection>`, return bound address + association event listener
- [ ] 3.3 Implement `Associate(remoteAddress)`: use `Tcp.OutgoingConnection()` to connect, materialize flow, return `StreamsAssociationHandle`
- [ ] 3.4 Implement `Shutdown()`: close listener, close all active associations, complete materialized streams
- [ ] 3.5 Implement `IsResponsibleFor(Address)`: protocol check for "tcp" / "ssl.tcp"
- [ ] 3.6 Create `StreamsAssociationHandle : AssociationHandle` with `Write(ReadOnlyMemory<byte>)` that queues framed data to the materialized flow

## 4. Integrated Write Path

- [ ] 4.1 Modify `EndpointWriter` (or create new equivalent) to use `FrameBufferWriter` for the write path
- [ ] 4.2 Write path: rent buffer → reserve 4 bytes → `BinaryPduCodec.WritePdu(writer, ...)` → `serializer.Serialize(writer, msg)` → backfill length → `AssociationHandle.Write(frame)`
- [ ] 4.3 Buffer lifecycle: return pooled array to `ArrayPool` after `stream.WriteAsync()` completes

## 5. Frame Parser (Read Path)

- [ ] 5.1 Create `FrameParser` that reads length-delimited frames from `ReadOnlySequence<byte>` (from `PipeReader`)
- [ ] 5.2 Handle partial frames: return consumed/examined positions for `PipeReader.AdvanceTo()`
- [ ] 5.3 Maximum frame size enforcement: reject frames exceeding `maximum-frame-size`, close connection
- [ ] 5.4 Parse complete frame: read 4-byte length → slice payload → `BinaryPduCodec.ReadPdu()` → dispatch
- [ ] 5.5 Unit tests: complete frames, partial frames, oversized frames, multiple frames in one read

## 6. Configuration

- [ ] 6.1 Create `StreamsTcpTransportSettings` that parses all `akka.remote.dot-netty.tcp.*` HOCON keys
- [ ] 6.2 Map: `hostname`, `port`, `public-hostname`, `public-port`, `send-buffer-size`, `receive-buffer-size`, `maximum-frame-size`, `backlog`, `tcp-nodelay`, `tcp-keepalive`, `tcp-reuse-addr`, `connection-timeout`
- [ ] 6.3 Map TLS settings: `enable-ssl` + `ssl.*` → `TlsSettings` (Spec 2)
- [ ] 6.4 Update `reference.conf`: change default transport class to `StreamsTcpTransport`
- [ ] 6.5 Preserve `batching.*` settings for Spec 5 (flush batching optimization)

## 7. Remove DotNetty

- [ ] 7.1 Delete `src/core/Akka.Remote/Transport/DotNetty/` directory
- [ ] 7.2 Remove DotNetty NuGet packages from `Akka.Remote.csproj` (`DotNetty.Transport`, `DotNetty.Codecs`, `DotNetty.Handlers`, `DotNetty.Common`, `DotNetty.Buffers`)
- [ ] 7.3 Add `Akka.Streams` project reference to `Akka.Remote.csproj`
- [ ] 7.4 Remove `DotNettyTransportSettings`, `DotNettySslSetup`, and all DotNetty-specific types
- [ ] 7.5 Fix all compilation errors from DotNetty removal

## 8. Testing

- [ ] 8.1 Verify all existing Akka.Remote specs pass (except DotNetty-specific ones)
- [ ] 8.2 Test: two ActorSystems communicate via `StreamsTcpTransport` (basic remoting)
- [ ] 8.3 Test: TLS remoting via `StreamsTcpTransport` + `TlsStreamProvider`
- [ ] 8.4 Test: association handshake, heartbeat, disassociation (AkkaProtocolTransport layer)
- [ ] 8.5 Test: large message handling (near maximum-frame-size)
- [ ] 8.6 Test: oversized frame rejection
- [ ] 8.7 Test: connection failure and recovery
- [ ] 8.8 Test: cluster formation with multiple nodes using new transport
- [ ] 8.9 Remove DotNetty-specific test files
- [ ] 8.10 Run full test suite: `dotnet test -c Release`
