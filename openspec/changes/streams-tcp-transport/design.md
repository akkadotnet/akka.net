## Context

Akka.Remote's transport layer is pluggable via the abstract `Transport` class (`src/core/Akka.Remote/Transport/Transport.cs`). The current concrete implementation is `DotNettyTransport`, which uses DotNetty's channel pipeline for framing, TLS, and socket I/O. Above the transport sits `AkkaProtocolTransport`, an adapter that handles the Akka protocol handshake (Associate/Disassociate), heartbeats, and association state management.

The current outbound hot path is split across multiple stages:

- `EndpointWriter.WriteSend()` serializes the user message into a `SerializedMessage`
- `AkkaPduCodec.ConstructMessage()` builds the remote envelope bytes
- `AkkaProtocolHandle.Write()` wraps that again in `AkkaProtocolMessage`
- the concrete transport turns the resulting `ByteString` back into transport bytes

That split is the main architectural bottleneck exposed by PR #8203. The abstraction overhead itself is small; the real cost is that serialization, protocol framing, and transport framing march along separate allocation-heavy paths.

With Specs 1+2, Akka.IO TCP now uses `Stream` + `Pipe` + `IStreamProvider` (with optional TLS). That gives the transport a place to own a pooled outbound writer and build the entire frame in one loop. The new transport should exploit that directly.

## Goals / Non-Goals

**Goals:**
- Implement `StreamsTcpTransport : Transport` using Akka.Streams TCP
- Integrated outbound framing + serialization via `FrameBufferWriter : IBufferWriter<byte>` (single buffer, no intermediate payload copies on the write side)
- Collapse the outbound path so the transport-owned write loop receives a send-shaped work item, writes protocol metadata, serializes the payload into the same writer, and emits one contiguous frame
- Preserve the current remoting wire format in the first production redesign
- All existing `akka.remote.dot-netty.tcp.*` HOCON configuration works unchanged
- All non-DotNetty-specific Akka.Remote specs pass
- Remove DotNetty dependency entirely

**Non-Goals:**
- Preserving `AssociationHandle.Write(ByteString)` purely for compatibility if it blocks the integrated write path
- Preserving source-compatible C# transport or setup APIs if they block the faster design
- Read-side pooled buffer leasing across actor boundaries
- Changing the actor-visible `Endpoint` / `EndpointWriter` / `EndpointReader` behavior more than necessary to lower serialization into the write loop
- UDP transport (separate, later)
- QUIC transport (future, different spec)
- Optimizing flush batching (Spec 5 — Performance)

## Decisions

### 1. Transport-owned outbound writer loop

**Decision:** The new transport lowers serialization and framing into a single outbound write loop owned by the remoting transport. The queue item crossing into that loop is send-shaped work, not prebuilt bytes.

```
Write path:
  EndpointWriter / transport state dequeues Send-shaped work
    → rent transport-owned frame buffer
    → reserve outer framing bytes
    → write protocol / envelope metadata
    → serializer writes payload directly into same writer
    → backfill lengths
    → flush completed frame to transport
    → return buffer to pool
```

**Rationale:** This is the smallest design that actually removes the current split between serialization and transport framing. It also keeps buffer lifetime simple: the outbound transport loop owns the pool and returns buffers after the write completes.

### 2. FrameBufferWriter for integrated outbound framing + serialization

**Decision:** Create `FrameBufferWriter : IBufferWriter<byte>` that wraps a pooled `byte[]` with a start offset. The write path reserves space for the outer frame header, writes protocol metadata + serialized payload via `IBufferWriter<byte>`, then backfills the length.

```
Write path:
  FrameBufferWriter(pooledArray, startOffset: 4)
    → write PDU header (serializerId, manifest length, manifest bytes)
    → serializer.Serialize(writer, msg)   ← payload directly in same buffer
    → backfill buffer[0..4] with total length
    → ReadOnlyMemory<byte>(array, 0, 4 + writtenCount)
    → stream.WriteAsync()
```

**Rationale:** The length header is always exactly 4 bytes. Reserve it upfront, write the rest, backfill. No separate framing stage is needed. The buffer exists only inside the outbound loop, which avoids having to pass `IMemoryOwner<byte>` or other ownership wrappers through the actor protocol.

**Alternative considered:** Separate Akka.Streams framing stage. Rejected for the transport — adds an unnecessary element copy between stages. The general-purpose `Framing.LengthField()` stage remains available for user-facing Streams TCP.

### 3. First production redesign preserves the current wire format

**Decision:** The first production redesign keeps the current remoting wire format end-to-end, including the existing protocol wrapper and envelope semantics, while rewriting the outbound path to construct that wire format in one pass.

**Rationale:** PR #8203 showed that the primary problem is the split pipeline, not just Protobuf. Keeping the wire format stable isolates the performance value of the new outbound regime and gets us to a production redesign faster.

**Follow-up:** If the integrated writer path is successful and further gains are still available, a later change can revisit the wire format separately.

### 4. Performance-first API breaks are acceptable

**Decision:** If the current C# transport, association, or setup APIs block the integrated outbound path, they should be changed instead of wrapped in compatibility shims on the hot path.

**Rationale:** The priority for Akka.NET 1.6 is to prove the new regime is faster. Preserving source compatibility in the performance-critical path would obscure that result and slow down the redesign.

### 5. StreamsTcpTransport implements Transport abstraction

**Decision:** New `StreamsTcpTransport : Transport` that uses Akka.Streams TCP for both server-side listening and client-side association.

Server path (`Listen`):
- `Tcp.Bind(listenAddress)` → `Source<IncomingConnection>` → materialize
- Each `IncomingConnection` produces a `StreamsAssociationHandle`
- `InboundPayload` events delivered to registered listener

Client path (`Associate`):
- `Tcp.OutgoingConnection(remoteAddress)` → materialize → `StreamsAssociationHandle`
- the association write path accepts already-framed transport bytes only as an implementation detail; the preferred production path is that framing happens before this boundary inside the outbound loop

**Rationale:** Reuses the Akka.Streams TCP infrastructure (which, after Spec 1, uses `Stream` + `Pipe` + `IStreamProvider`). Backpressure propagates naturally through Streams demand signaling.

### 6. Configuration key preservation

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

### 7. Remove DotNetty dependency

**Decision:** Delete `src/core/Akka.Remote/Transport/DotNetty/` entirely. Remove all DotNetty NuGet packages from `Akka.Remote.csproj`.

**Rationale:** Clean break. No adapter layer or backward compat shim for DotNetty. The new transport is the only transport. DotNetty-specific programmatic APIs (`DotNettyTransportSettings`, `DotNettySslSetup`) are replaced by their equivalents.

### 8. Read path remains copy-based above the transport boundary

**Decision:** The read side uses the Pipe from Spec 1, but pooled buffer ownership stops before actor-visible lifetime begins. Transport and framing layers may parse from `ReadOnlySequence<byte>` while data is still in scope, but payload bytes are copied before they become inbound actor messages or stateful protocol queues.

```
Read path:
  stream.ReadAsync() → Pipe.Writer
  Pipe.Reader.ReadAsync() → ReadOnlySequence<byte>
    → read 4-byte length → check if enough bytes for full frame
    → if yes: parse frame while bytes are live
    → copy before crossing actor-visible lifetime boundaries
    → deserialize / dispatch as ordinary inbound remoting data
    → if no: AdvanceTo(consumed, examined) → wait for more data
```

**Rationale:** Read-side zero-copy across actor boundaries would require explicit lifetime / release semantics and effectively a mini-GC for inbound messages. That is not a good trade-off for this milestone. Outbound pooling captures most of the practical gain with much less risk.

### Phased delivery path

- Phase 1: keep the benchmark-only spike as the proof that the integrated outbound regime is directionally better.
- Phase 2: land a production outbound writer path that emits the current remoting wire format from send-shaped work items and takes any required C# API breaks.
- Phase 3: swap the DotNetty transport backend for `StreamsTcpTransport` on the same wire format.
- Phase 4: run `RemotePingPong`, tune batching and buffer thresholds, and only then decide which compatibility cleanups or additional protocol changes are worth doing.

## Risks / Trade-offs

**[Transport abstraction changes]** → The existing `AssociationHandle.Write(ByteString)` boundary is not a good fit for a transport-owned pooled write path. This is an intentional 1.6 breaking change. The spike already gives directional evidence that the break is worth taking.

**[Wire format stays the same, so some protocol allocations may remain]** → Acceptable for the first production redesign. The goal is to remove the largest structural costs first and benchmark the result before taking on a separate wire-format rewrite.

**[Akka.Remote now depends on Akka.Streams]** → Currently `Akka.Remote` depends on `Akka` core only (plus DotNetty). The new transport adds a dependency on `Akka.Streams`. This is a new transitive dependency for all remoting users. Acceptable since Streams is a core module, not an external package.

**[Flush batching needs tuning]** → DotNetty's `FlushConsolidationHandler` batches flushes for throughput. The new transport needs equivalent batching (consolidate multiple writes before calling `stream.FlushAsync()`). Deferred to Spec 5 (Performance) for benchmarking-driven optimization.

**[FrameBufferWriter growth on SizeHint underestimate]** → If `serializer.SizeHint()` underestimates, `FrameBufferWriter` rents a larger array from `ArrayPool` and copies. This is a fallback path — `SizeHint` should be accurate for most messages. Benchmark to ensure the growth path doesn't regress.

**[Read-side pooling intentionally excluded]** → Some peak read-side gains are left on the table. This is acceptable because the actor-lifetime semantics make pooled inbound buffers disproportionately risky.
