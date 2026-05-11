## 1. FrameBufferWriter

- [ ] 1.1 Create `FrameBufferWriter : IBufferWriter<byte>` in `src/core/Akka.Remote/` — wraps pooled `byte[]` with start offset, implements `GetMemory`/`GetSpan`/`Advance`, supports growth via `ArrayPool`
- [ ] 1.2 Add length-header backfill method: writes total frame length into reserved first 4 bytes
- [ ] 1.3 Add `GetFrameMemory()` method that returns `ReadOnlyMemory<byte>` spanning the complete frame (length header + payload)
- [ ] 1.4 Add `Return()` method that returns the pooled `byte[]` to `ArrayPool`
- [ ] 1.5 Unit tests for FrameBufferWriter: basic write, growth, backfill, pool return

## 2. Outbound write-loop spike

- [ ] 2.1 Add a benchmark-local spike that compares the current split outbound pipeline against an integrated transport-owned writer loop
- [ ] 2.2 Use a send-shaped work item in the spike rather than prebuilt payload bytes
- [ ] 2.3 Keep buffer ownership internal to the outbound loop in the spike; do not pass owned buffers through actor messages
- [ ] 2.4 Benchmark common payload shapes (`string`, `byte[]`, small and large payloads)
- [ ] 2.5 Record whether the integrated loop is enough to justify changing the transport write contract

## 3. Production PDU strategy

- [ ] 3.1 Decide whether production transport keeps the current Protobuf wire semantics initially or replaces `AkkaPduProtobuffCodec` with a binary codec after the spike lands
- [ ] 3.2 If binary PDU replacement is still justified, create `BinaryPduCodec` in `src/core/Akka.Remote/` with `WritePdu(IBufferWriter<byte>, ...)` methods for payload, associate, disassociate, heartbeat PDU types
- [ ] 3.3 If binary PDU replacement is chosen, create `ReadPdu(ReadOnlySequence<byte>)` methods that parse PDU type and extract fields
- [ ] 3.4 If binary PDU replacement is chosen, define PDU type constants and round-trip tests for all PDU types

## 4. StreamsTcpTransport Implementation

- [ ] 4.1 Create `StreamsTcpTransport : Transport` in `src/core/Akka.Remote/Transport/Streams/`
- [ ] 4.2 Implement `Listen()`: use `Tcp.Bind()` to create listener, materialize `Source<IncomingConnection>`, return bound address + association event listener
- [ ] 4.3 Implement `Associate(remoteAddress)`: use `Tcp.OutgoingConnection()` to connect, materialize flow, return `StreamsAssociationHandle`
- [ ] 4.4 Implement `Shutdown()`: close listener, close all active associations, complete materialized streams
- [ ] 4.5 Implement `IsResponsibleFor(Address)`: protocol check for "tcp" / "ssl.tcp"
- [ ] 4.6 Replace the current write boundary with a contract that supports transport-owned outbound frame construction; do not preserve `Write(ByteString)` if it blocks the integrated path

## 5. Integrated Write Path

- [ ] 5.1 Modify `EndpointWriter` (or create new equivalent) so the outbound loop operates on send-shaped work, not prebuilt payload bytes
- [ ] 5.2 Write path: rent buffer → reserve outer frame bytes → write protocol metadata → serialize payload directly into the same writer → backfill lengths → flush to transport
- [ ] 5.3 Buffer lifecycle: return pooled array to `ArrayPool` after the transport write completes
- [ ] 5.4 Keep read-side copy semantics intact; do not introduce pooled inbound buffers into actor-visible lifetime

## 6. Frame Parser (Read Path)

- [ ] 6.1 Create `FrameParser` that reads length-delimited frames from `ReadOnlySequence<byte>` (from `PipeReader`)
- [ ] 6.2 Handle partial frames: return consumed/examined positions for `PipeReader.AdvanceTo()`
- [ ] 6.3 Maximum frame size enforcement: reject frames exceeding `maximum-frame-size`, close connection
- [ ] 6.4 Parse complete frame while bytes are live, then copy before data crosses actor-visible lifetime boundaries
- [ ] 6.5 Unit tests: complete frames, partial frames, oversized frames, multiple frames in one read

## 7. Configuration

- [ ] 7.1 Create `StreamsTcpTransportSettings` that parses all `akka.remote.dot-netty.tcp.*` HOCON keys
- [ ] 7.2 Map: `hostname`, `port`, `public-hostname`, `public-port`, `send-buffer-size`, `receive-buffer-size`, `maximum-frame-size`, `backlog`, `tcp-nodelay`, `tcp-keepalive`, `tcp-reuse-addr`, `connection-timeout`
- [ ] 7.3 Map TLS settings: `enable-ssl` + `ssl.*` → `TlsSettings` (Spec 2)
- [ ] 7.4 Update `reference.conf`: change default transport class to `StreamsTcpTransport`
- [ ] 7.5 Preserve `batching.*` settings for Spec 5 (flush batching optimization)

## 8. Remove DotNetty

- [ ] 8.1 Delete `src/core/Akka.Remote/Transport/DotNetty/` directory
- [ ] 8.2 Remove DotNetty NuGet packages from `Akka.Remote.csproj` (`DotNetty.Transport`, `DotNetty.Codecs`, `DotNetty.Handlers`, `DotNetty.Common`, `DotNetty.Buffers`)
- [ ] 8.3 Add `Akka.Streams` project reference to `Akka.Remote.csproj`
- [ ] 8.4 Remove `DotNettyTransportSettings`, `DotNettySslSetup`, and all DotNetty-specific types
- [ ] 8.5 Fix all compilation errors from DotNetty removal

## 9. Testing

- [ ] 9.1 Verify all existing Akka.Remote specs pass (except DotNetty-specific ones)
- [ ] 9.2 Test: two ActorSystems communicate via `StreamsTcpTransport` (basic remoting)
- [ ] 9.3 Test: TLS remoting via `StreamsTcpTransport` + `TlsStreamProvider`
- [ ] 9.4 Test: association handshake, heartbeat, disassociation (AkkaProtocolTransport layer)
- [ ] 9.5 Test: large message handling (near maximum-frame-size)
- [ ] 9.6 Test: oversized frame rejection
- [ ] 9.7 Test: connection failure and recovery
- [ ] 9.8 Test: cluster formation with multiple nodes using new transport
- [ ] 9.9 Remove DotNetty-specific test files
- [ ] 9.10 Run full test suite: `dotnet test -c Release`
