## Context

Akka.Remote's transport layer is pluggable via the abstract `Transport` class (`src/core/Akka.Remote/Transport/Transport.cs`). The current concrete implementation is `DotNettyTransport` which uses DotNetty's channel pipeline for framing, TLS, and socket I/O. Above the transport sits `AkkaProtocolTransport` — an adapter that handles the Akka protocol handshake (Associate/Disassociate), heartbeats, and association state management. `AkkaProtocolTransport` doesn't change.

The transport API:
- `Listen()` → binds a server socket, returns address + listener
- `Associate(remoteAddress)` → connects to remote, returns `AssociationHandle`
- `AssociationHandle.Write(ReadOnlyMemory<byte>)` → send framed data (after Spec 1, payload is `ReadOnlyMemory<byte>`)
- `AssociationHandle.ReadHandlerSource` → listener receives `InboundPayload` events

With Specs 1+2, Akka.IO TCP now uses `Stream` + `Pipe` + `IStreamProvider` (with optional TLS). Akka.Streams TCP (`Tcp.Bind()`, `Tcp.OutgoingConnection()`) wraps Akka.IO TCP actors in a `Flow<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>`. The new transport builds on this.

## Goals / Non-Goals

**Goals:**
- Implement `StreamsTcpTransport : Transport` using Akka.Streams TCP
- Integrated framing + serialization via `FrameBufferWriter : IBufferWriter<byte>` (single buffer, zero intermediate copies)
- Replace Protobuf PDU encoding with simple binary encoding directly to `IBufferWriter<byte>`
- All existing `akka.remote.dot-netty.tcp.*` HOCON configuration works unchanged
- All non-DotNetty-specific Akka.Remote specs pass
- Remove DotNetty dependency entirely

**Non-Goals:**
- Changing the `AkkaProtocolTransport` adapter (handshake, heartbeat, association management)
- Changing the `Endpoint` / `EndpointWriter` / `EndpointReader` actor hierarchy
- UDP transport (separate, later)
- QUIC transport (future, different spec)
- Optimizing flush batching (Spec 5 — Performance)

## Decisions

### 1. FrameBufferWriter for integrated framing + serialization

**Decision:** Create `FrameBufferWriter : IBufferWriter<byte>` that wraps a pooled `byte[]` with a start offset. The write path reserves 4 bytes for the length header, writes PDU metadata + serialized payload via `IBufferWriter<byte>`, then backfills the length. One buffer, one syscall.

```
Write path:
  FrameBufferWriter(pooledArray, startOffset: 4)
    → write PDU header (serializerId, manifest length, manifest bytes)
    → serializer.Serialize(writer, msg)   ← payload directly in same buffer
    → backfill buffer[0..4] with total length
    → ReadOnlyMemory<byte>(array, 0, 4 + writtenCount)
    → stream.WriteAsync()
```

**Rationale:** The length header is always exactly 4 bytes. Reserve them upfront, write the rest, backfill. No need for a separate framing stage (which would require an extra copy). The serializer's `IBufferWriter<byte>` contract flows end-to-end from serialization through framing to the socket.

**Alternative considered:** Separate Akka.Streams framing stage. Rejected for the transport — adds an unnecessary element copy between stages. The general-purpose `Framing.LengthField()` stage remains available for user-facing Streams TCP.

### 2. Binary PDU encoding (replaces Protobuf AkkaPduCodec)

**Decision:** Replace the Protobuf `SerializedMessage` / `AkkaProtocolMessage` envelope with simple binary encoding written directly to `IBufferWriter<byte>`.

```
PDU format (written to IBufferWriter):
  [4 bytes] total frame length (little-endian int32)
  [1 byte]  PDU type (0x01 = payload, 0x02 = associate, 0x03 = disassociate, 0x04 = heartbeat)
  --- for payload PDU type ---
  [4 bytes] serializerId (little-endian int32)
  [2 bytes] manifest length (little-endian uint16)
  [N bytes] manifest (UTF8)
  [M bytes] serialized payload (remaining bytes = frame length - header)
```

**Rationale:** The Protobuf PDU encoding allocates intermediate `byte[]` arrays and Protobuf `ByteString` wrappers. Direct binary encoding to `IBufferWriter<byte>` eliminates these allocations. The PDU format is simple enough that Protobuf's schema evolution isn't needed — it's a fixed internal protocol, not a public API.

**Wire compatibility note:** This changes the PDU encoding format. A v1.6 node CANNOT talk to a v1.5 node. This is acceptable for a major version. The outer framing (4-byte length prefix) is preserved.

### 3. StreamsTcpTransport implements Transport abstraction

**Decision:** New `StreamsTcpTransport : Transport` that uses Akka.Streams TCP for both server-side listening and client-side association.

Server path (`Listen`):
- `Tcp.Bind(listenAddress)` → `Source<IncomingConnection>` → materialize
- Each `IncomingConnection` produces a `StreamsAssociationHandle`
- `InboundPayload` events delivered to registered listener

Client path (`Associate`):
- `Tcp.OutgoingConnection(remoteAddress)` → materialize → `StreamsAssociationHandle`
- `Write(ReadOnlyMemory<byte>)` queues data for the materialized flow

**Rationale:** Reuses the Akka.Streams TCP infrastructure (which, after Spec 1, uses `Stream` + `Pipe` + `IStreamProvider`). Backpressure propagates naturally through Streams demand signaling.

### 4. Configuration key preservation

**Decision:** Parse all existing `akka.remote.dot-netty.tcp.*` HOCON keys into `StreamsTcpTransportSettings`. The config section name stays the same. Only the transport class reference changes.

**Rationale:** Zero user config changes for the most common case. Users who have no DotNetty-specific code or config changes see a transparent upgrade. The default transport class in `reference.conf` changes from DotNetty's `TcpTransport` to `StreamsTcpTransport`.

Keys preserved:
- `hostname`, `port`, `public-hostname`, `public-port`
- `send-buffer-size`, `receive-buffer-size`
- `maximum-frame-size`
- `backlog`
- `tcp-nodelay`, `tcp-keepalive`, `tcp-reuse-addr`
- `enable-ssl` + all `ssl.*` sub-keys (→ Spec 2 TLS)
- `connection-timeout`
- `batching.*` (flush batching settings — Spec 5 optimization)

### 5. Remove DotNetty dependency

**Decision:** Delete `src/core/Akka.Remote/Transport/DotNetty/` entirely. Remove all DotNetty NuGet packages from `Akka.Remote.csproj`.

**Rationale:** Clean break. No adapter layer or backward compat shim for DotNetty. The new transport is the only transport. DotNetty-specific programmatic APIs (`DotNettyTransportSettings`, `DotNettySslSetup`) are replaced by their equivalents.

### 6. Read path: Pipe → length-delimited frame parsing → deserialize

**Decision:** The read side uses the Pipe from Spec 1. `ReadOnlySequence<byte>` from `PipeReader` is parsed for length-delimited frames. Each complete frame's payload `ReadOnlySequence<byte>` is sliced and passed directly to `serializer.Deserialize()`.

```
Read path:
  stream.ReadAsync() → Pipe.Writer
  Pipe.Reader.ReadAsync() → ReadOnlySequence<byte>
    → read 4-byte length → check if enough bytes for full frame
    → if yes: slice payload → parse PDU header → serializer.Deserialize(payloadSlice)
    → if no: AdvanceTo(consumed, examined) → wait for more data
```

**Rationale:** `ReadOnlySequence<byte>` is what `PipeReader` returns and what `serializer.Deserialize()` takes. The frame parser just reads the length, checks bounds, and slices. Zero copy from Pipe buffer to serializer. The only copy is the one in the Akka.IO TCP actor (required for unbounded message lifetime — decided in Spec 1).

## Risks / Trade-offs

**[Breaking wire compatibility with v1.5]** → The binary PDU encoding is incompatible with the Protobuf PDU used in v1.5. This is a major version break. Mixed-version clusters are not supported during upgrade. All nodes must be upgraded together. Document clearly in release notes.

**[Akka.Remote now depends on Akka.Streams]** → Currently `Akka.Remote` depends on `Akka` core only (plus DotNetty). The new transport adds a dependency on `Akka.Streams`. This is a new transitive dependency for all remoting users. Acceptable since Streams is a core module, not an external package.

**[Flush batching needs tuning]** → DotNetty's `FlushConsolidationHandler` batches flushes for throughput. The new transport needs equivalent batching (consolidate multiple writes before calling `stream.FlushAsync()`). Deferred to Spec 5 (Performance) for benchmarking-driven optimization.

**[FrameBufferWriter growth on SizeHint underestimate]** → If `serializer.SizeHint()` underestimates, `FrameBufferWriter` rents a larger array from `ArrayPool` and copies. This is a fallback path — `SizeHint` should be accurate for most messages. Benchmark to ensure the growth path doesn't regress.
