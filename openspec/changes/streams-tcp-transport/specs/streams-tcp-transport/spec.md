## ADDED Requirements

### Requirement: StreamsTcpTransport implements Transport abstraction
The system SHALL provide `StreamsTcpTransport : Transport` that implements the pluggable transport API using Akka.Streams TCP.

#### Scenario: Server-side listen
- **WHEN** `StreamsTcpTransport.Listen()` is called
- **THEN** it SHALL bind a TCP listener via `Tcp.Bind()`, return the bound address and a listener that emits `InboundPayload` events for each received frame

#### Scenario: Client-side associate
- **WHEN** `StreamsTcpTransport.Associate(remoteAddress)` is called
- **THEN** it SHALL connect via `Tcp.OutgoingConnection()` (using `IStreamProvider` with optional TLS), materialize the connection, and return a `StreamsAssociationHandle`

#### Scenario: Transport write contract modernized for outbound ownership
- **WHEN** the new transport implementation is integrated into remoting
- **THEN** the write contract below the remoting outbound loop SHALL support transport-owned frame construction and SHALL NOT require prebuilt `ByteString` payloads to be passed across that boundary

#### Scenario: Transport shutdown
- **WHEN** `StreamsTcpTransport.Shutdown()` is called
- **THEN** it SHALL close all active associations and the listener, completing all materialized streams

### Requirement: FrameBufferWriter for integrated framing and serialization
The system SHALL provide `FrameBufferWriter : IBufferWriter<byte>` that writes into a pooled `byte[]` with offset support, enabling the length header to be reserved upfront and backfilled after the payload is written.

#### Scenario: Single-buffer frame construction
- **WHEN** a message is serialized for transport
- **THEN** the write path SHALL reserve 4 bytes for the length header, write PDU metadata and serialized payload via `IBufferWriter<byte>`, backfill the 4-byte length, and produce a single contiguous `ReadOnlyMemory<byte>` frame

#### Scenario: Buffer growth on SizeHint underestimate
- **WHEN** the serializer writes more bytes than the initial buffer capacity
- **THEN** `FrameBufferWriter` SHALL rent a larger array from `ArrayPool<byte>.Shared`, copy existing data, and continue writing

#### Scenario: Buffer returned to pool after send
- **WHEN** the frame has been written to the socket via `stream.WriteAsync()`
- **THEN** the pooled `byte[]` SHALL be returned to `ArrayPool<byte>.Shared`

### Requirement: Outbound transport loop owns buffer lifetime
The outbound remoting transport loop SHALL own pooled write buffers and SHALL build transport frames from send-shaped work items rather than actor messages carrying pooled buffers.

#### Scenario: Send-shaped work enters outbound loop
- **WHEN** remoting has a user message ready to send
- **THEN** the outbound loop SHALL receive a send-shaped work item containing the semantic message metadata, not a prebuilt transport payload

#### Scenario: Transport owns pooled outbound buffer
- **WHEN** the outbound loop rents a buffer to construct a frame
- **THEN** the transport SHALL retain ownership of that buffer until the transport write completes or fails

### Requirement: First production redesign preserves current wire format
The first production remoting redesign SHALL preserve the current remoting wire format even if the internal outbound writer path and C# transport APIs change.

#### Scenario: Payload frames remain wire-compatible
- **WHEN** a user message is sent to a remote actor
- **THEN** the integrated outbound writer SHALL emit payload bytes that are semantically equivalent to the current `AkkaPduProtobuffCodec` plus `AkkaProtocolMessage` wire format

#### Scenario: Control messages remain wire-compatible
- **WHEN** associate, disassociate, or heartbeat messages are sent
- **THEN** the integrated outbound writer SHALL emit the same wire-level protocol fields expected by the current remoting peer

### Requirement: Performance-first API changes allowed
The first production redesign SHALL prioritize the faster outbound regime over source-compatible C# transport APIs.

#### Scenario: Slower compatibility layer rejected on hot path
- **WHEN** an existing C# transport or remoting write API blocks the integrated outbound path
- **THEN** that API SHALL be changed instead of preserving a slower compatibility layer in the hot path

### Requirement: Length-delimited frame parsing on read path
The system SHALL parse incoming data from `PipeReader` as length-delimited frames while keeping read-side pooled buffer lifetime internal to the transport.

#### Scenario: Complete frame available
- **WHEN** `PipeReader.ReadAsync()` returns a buffer containing at least `4 + frameLength` bytes
- **THEN** the parser SHALL read the 4-byte length, parse the frame while bytes are live, and copy before data crosses actor-visible lifetime boundaries

#### Scenario: Partial frame buffering
- **WHEN** `PipeReader.ReadAsync()` returns a buffer with fewer bytes than the frame length indicates
- **THEN** the parser SHALL call `AdvanceTo(consumed, examined)` to signal that more data is needed, and wait for the next `ReadAsync()` call

#### Scenario: Maximum frame size enforcement
- **WHEN** the 4-byte length header indicates a frame larger than `maximum-frame-size`
- **THEN** the parser SHALL close the connection with an error (prevents memory exhaustion from malformed/malicious data)

#### Scenario: No pooled inbound buffers across actor boundaries
- **WHEN** inbound bytes are turned into remoting protocol events or actor messages
- **THEN** they SHALL no longer depend on transport-owned pooled buffer lifetime

### Requirement: DotNetty HOCON configuration compatibility
All existing `akka.remote.dot-netty.tcp.*` HOCON configuration keys SHALL continue to work without modification, mapped to the new `StreamsTcpTransportSettings`.

#### Scenario: Standard connection settings preserved
- **WHEN** `hostname`, `port`, `public-hostname`, `public-port`, `send-buffer-size`, `receive-buffer-size`, `maximum-frame-size`, `backlog`, `tcp-nodelay`, `tcp-keepalive`, `tcp-reuse-addr`, `connection-timeout` are configured
- **THEN** the new transport SHALL apply these settings identically to the DotNetty transport

#### Scenario: TLS settings preserved
- **WHEN** `enable-ssl = true` with `ssl.*` sub-keys are configured
- **THEN** the transport SHALL use `TlsStreamProvider` (Spec 2) with settings parsed from the same HOCON keys

#### Scenario: Default transport class updated
- **WHEN** no explicit transport class is configured
- **THEN** the `reference.conf` default SHALL specify `StreamsTcpTransport` instead of `DotNettyTransport`

### Requirement: DotNetty dependency removed
The `Akka.Remote` project SHALL NOT reference any DotNetty NuGet packages. The `Transport/DotNetty/` directory SHALL be deleted.

#### Scenario: Clean build without DotNetty
- **WHEN** the solution is built
- **THEN** no DotNetty assemblies SHALL be referenced or loaded

#### Scenario: Akka.Remote depends on Akka.Streams
- **WHEN** the `Akka.Remote` project is built
- **THEN** it SHALL reference `Akka.Streams` as a project/package dependency

### Requirement: Behavioral compatibility with existing transport
All Akka.Remote specs that do not directly reference DotNetty library APIs SHALL pass with the new transport without modification.

#### Scenario: AkkaProtocolTransport semantics preserved
- **WHEN** the remoting protocol layer is run on top of `StreamsTcpTransport`
- **THEN** association handshake, heartbeats, sequence numbers, ACKs, and disassociation SHALL behave identically on the wire to the DotNetty-based transport

#### Scenario: EndpointWriter sends messages
- **WHEN** `EndpointWriter` serializes a message and writes it to the transport
- **THEN** it SHALL lower serialization and protocol framing into the transport-owned outbound writer path (`Send`-shaped work → single buffer construction → transport write)

#### Scenario: Two ActorSystems communicate
- **WHEN** two ActorSystems are configured with `StreamsTcpTransport` and remoting enabled
- **THEN** they SHALL be able to send messages, resolve remote actor refs, and maintain cluster membership identically to the DotNetty transport
