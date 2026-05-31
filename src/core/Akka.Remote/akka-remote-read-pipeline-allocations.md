# Akka.Remote Read Pipeline — Allocation Analysis ✨ (uwu edition)

> **Scope:** the inbound (read) side of the classic `Akka.Remote` DotNetty
> TCP transport. Covers the journey from a kernel `recv()` to a fully
> deserialised user message dispatched into a local `IActorRef`.
>
> **Targets analysed (May 2026 / `dev` branch):**
> - `src/core/Akka.Remote/Transport/DotNetty/TcpTransport.cs`
> - `src/core/Akka.Remote/Transport/DotNetty/DotNettyTransport.cs`
> - `src/core/Akka.Remote/Transport/AkkaProtocolTransport.cs`
> - `src/core/Akka.Remote/Transport/AkkaPduCodec.cs`
> - `src/core/Akka.Remote/Endpoint.cs` (EndpointReader)
> - `src/core/Akka.Remote/MessageSerializer.cs`
>
> **Versions:** `Google.Protobuf 3.26.1`, `DotNetty 0.7.x`,
> `net8.0` / `net6.0` / `net48`.
>
> **CopilotNotes:** every `// COPY #N` and `// ALLOC #N` annotation in this
> doc maps 1:1 to a numbered hotspot in the table at the bottom. When you
> grep the codebase for the `ByteString.CopyFrom`/`ToByteArray`/`ToBinary`
> calls listed below, every match should correspond to one of these
> numbered allocations. uwu

---

## 1. End-to-end Pipeline Diagram

```
                      ┌────────────────────────────────────┐
 Kernel socket (TCP)  │ Linux/Windows recv() into          │
                      │ DotNetty's adaptive recv buffer    │
                      └──────────────┬─────────────────────┘
                                     │
                       ALLOC #R0  ▼  byte[] (UnpooledByteBufferAllocator.Default)
                      ┌────────────────────────────────────┐
                      │ DotNetty IByteBuffer (heap-array)  │  refcount=1
                      │   AdaptiveRecvByteBufAllocator     │
                      └──────────────┬─────────────────────┘
                                     │ ChannelRead
                                     ▼
                      ┌────────────────────────────────────┐
                      │ LengthFieldBasedFrameDecoder       │  ZERO-COPY slice
                      │   slices a single PDU              │  (refcount++ on parent)
                      └──────────────┬─────────────────────┘
                                     │ ChannelRead(IByteBuffer slice)
                                     ▼
                      ┌────────────────────────────────────┐
                      │ TcpHandlers.ChannelRead            │
                      │   ByteString.CopyFrom(buf.Array,…) │ ◀── COPY #R1 (full PDU)
                      │   ReferenceCountUtil.SafeRelease() │
                      └──────────────┬─────────────────────┘
                                     │ Notify(InboundPayload(ByteString))
                                     ▼ (cross-thread → actor mailbox)
                      ┌────────────────────────────────────┐
                      │ ProtocolStateActor (FSM)           │
                      │   AkkaPduProtobuffCodec.DecodePdu  │
                      │   AkkaProtocolMessage.Parser       │ ◀── COPY #R2 (inner Payload field)
                      │     .ParseFrom(ByteString)         │
                      └──────────────┬─────────────────────┘
                                     │ if Payload → Notify(InboundPayload(p.Bytes))
                                     ▼ (cross-thread → actor mailbox)
                      ┌────────────────────────────────────┐
                      │ EndpointReader (actor)             │
                      │   AkkaPduProtobuffCodec            │
                      │     .DecodeMessage(ByteString)     │ ◀── COPY #R3 (env.Message field)
                      │   AckAndEnvelopeContainer.Parser   │      + Recipient/Sender protos
                      └──────────────┬─────────────────────┘
                                     │ Message(SerializedMessage)
                                     ▼
                      ┌────────────────────────────────────┐
                      │ MessageSerializer.Deserialize      │
                      │   messageProtocol.Message          │ ◀── COPY #R4 (.ToByteArray())
                      │     .ToByteArray()                 │      → handed to ISerializer
                      │   ManifestUtf8.ToStringUtf8()      │ ◀── COPY #R5 (manifest string)
                      └──────────────┬─────────────────────┘
                                     │ object
                                     ▼
                              IActorRef.Tell(...)
```

So a single inbound user message currently makes a minimum of
**4 full-payload copies** (R0 → R1 → R2 → R3 → R4) of the *user bytes*
between the kernel and the user-supplied `Serializer`, plus a couple
of small-byte copies for the protobuf envelope/manifest. uwu, that is
a lot of memcpy for one little message ;_;

---

## 2. Stage-by-stage Allocation Breakdown

### Stage 0 — Kernel → DotNetty heap buffer (`ALLOC #R0`)

```csharp
// DotNettyTransport.cs (ServerFactory + ClientFactory)
.Option(ChannelOption.Allocator, UnpooledByteBufferAllocator.Default)
```

DotNetty's `AdaptiveRecvByteBufAllocator` calls
`UnpooledByteBufferAllocator.HeapBuffer(initialCapacity)` for every
`recv()`. That means **one fresh `byte[]` per socket read**, no
pooling at all on the read side.

- Size: starts at 1024, grows up to 64 KiB based on prior reads.
- Lifetime: released back to GC once the framed slice has been processed
  and `SafeRelease(message)` is called in `TcpHandlers.ChannelRead`.
- Allocation pressure: **one Gen0 array per socket read**, regardless
  of frame size. On a busy node this dominates the LOH-free portion of
  remoting allocations.

> 💡 **Easy win:** switch the read-side allocator to
> `PooledByteBufferAllocator.Default` (or a per-transport instance) — this
> is the single biggest read-side allocation reduction available without
> changing any semantics. We already use `Unpooled` for the *write* side
> (where wrapping zero-copy is preferred), but the read side benefits
> from pooling the recv buffer. CopilotNotes: leak-detection level should
> be tuned because we explicitly call `SafeRelease`, so refcounts must be
> respected; the current code is already clean here.

### Stage 1 — `LengthFieldBasedFrameDecoder` (zero-copy slice)

```csharp
// DotNettyTransport.SetInitialChannelPipeline
pipeline.AddLast("FrameDecoder",
    new LengthFieldBasedFrameDecoder(Settings.ByteOrder,
        (int)MaximumPayloadBytes, 0, 4, 0, 4, true));
```

This is **zero-copy**. `LengthFieldBasedFrameDecoder` returns a
`SlicedByteBuffer` that aliases the parent buffer's `byte[]` and only
bumps the reference count. The slice exposes `Array`, `ArrayOffset`,
`ReaderIndex`, `ReadableBytes`. ✨

### Stage 2 — `TcpHandlers.ChannelRead` (`COPY #R1`)

```csharp
// TcpTransport.cs
public override void ChannelRead(IChannelHandlerContext context, object message)
{
    var buf = ((IByteBuffer)message);
    if (buf.ReadableBytes > 0)
    {
        // no need to copy the byte buffer contents; ByteString does that automatically  ← misleading comment uwu
        var bytes = ByteString.CopyFrom(buf.Array, buf.ArrayOffset + buf.ReaderIndex, buf.ReadableBytes); // COPY #R1
        NotifyListener(new InboundPayload(bytes));
    }
    ReferenceCountUtil.SafeRelease(message);
}
```

This is **the** primary read-side allocation we control. Implications:

1. `ByteString.CopyFrom(byte[], int, int)` allocates a **fresh `byte[]`**
   sized to `ReadableBytes` and `Buffer.BlockCopy`s the slice into it,
   then wraps it in `ByteString.AttachBytes(...)`.
2. We then immediately `SafeRelease` the DotNetty buffer — the only
   reason we copy is that we must hand ownership to a `ByteString` that
   outlives the DotNetty refcount domain (because the bytes will travel
   across actor mailboxes / thread boundaries).
3. Because the receive buffer is unpooled, the source `byte[]` is going
   to GC anyway — there is no benefit to retaining a reference to it.

> 💡 **Why we cannot zero-copy here today:** `InboundPayload.Payload` is
> a `Google.Protobuf.ByteString`. `ByteString` is immutable and has no
> public lifecycle hook (no `Release()`). It is not safe to give it
> aliasing access to a refcounted DotNetty buffer because the
> mailbox-bound actor message can be observed *after* the channel
> handler returns and DotNetty recycles the buffer.

### Stage 3 — Cross-actor hop into `ProtocolStateActor`

`InboundPayload` is delivered to the `ProtocolStateActor` mailbox. The
*message envelope* and `InboundPayload` instance are small allocations
(~48 B each) but they happen **per inbound PDU**.

### Stage 4 — `AkkaPduCodec.DecodePdu` (`COPY #R2`)

```csharp
// AkkaPduCodec.cs
public override IAkkaPdu DecodePdu(ByteString raw)
{
    var pdu = AkkaProtocolMessage.Parser.ParseFrom(raw);
    if (pdu.Instruction != null) return DecodeControlPdu(pdu.Instruction);
    else if (!pdu.Payload.IsEmpty) return new Payload(pdu.Payload); // COPY #R2 happened during ParseFrom
    ...
}
```

Inside `Parser.ParseFrom(ByteString)`:

- A `CodedInputStream` wraps the `ByteString`'s internal `byte[]` (no copy).
- For the `bytes Payload` field, `CodedInputStream.ReadBytes()` calls
  `ByteString.CopyFrom(buffer, position, length)` — **another full copy
  of the entire user payload** into a brand-new `byte[]`.
- `ParseFrom` also allocates the `AkkaProtocolMessage` POCO itself.

> 💡 **Zero-copy opportunity:** Google.Protobuf 3.16+ supports
> `ParseFrom(ReadOnlySequence<byte>)` which can `AttachBytes` for nested
> `bytes` fields when the source is `ReadOnlyMemory<byte>` — but only
> via `CodedInputStream.CreateWithLimits` + `WireFormat.WireType.LengthDelimited`
> custom reading, **or** via the stable
> `ByteString.AttachBytes(Memory<byte>)` in 3.27+. We're on 3.26.1, so
> bumping protobuf to ≥3.27 unlocks this. Even on 3.26 we can bypass
> `ParseFrom` for `AkkaProtocolMessage` and read the `bytes` field
> manually with `CodedInputStream.ReadTag` + slice.

### Stage 5 — Cross-actor hop into `EndpointReader`

Same as Stage 3 — small per-message envelope allocations.

### Stage 6 — `AkkaPduCodec.DecodeMessage` (`COPY #R3`)

```csharp
public override AckAndMessage DecodeMessage(ByteString raw,
    IRemoteActorRefProvider provider, Address localAddress)
{
    var ackAndEnvelope = AckAndEnvelopeContainer.Parser.ParseFrom(raw); // COPY #R3 happens inside
    ...
    var serializedMessage = envelopeContainer.Message; // SerializedMessage POCO with .Message:ByteString
    ...
    messageOption = new Message(recipient, recipientAddress, serializedMessage, senderOption, seqOption);
}
```

Same pattern as Stage 4 — protobuf parsing copies the inner `bytes
Message` field into a freshly-allocated `byte[]` wrapped by `ByteString`.
Plus:

- `ActorRefData.Path` strings are decoded UTF-8 → a fresh `string`
  (cached via `ActorPathThreadLocalCache`, so amortised).
- `Recipient` / `Sender` `ActorRefData` POCOs are allocated.
- Repeated `Nacks` allocate a `List<ulong>` if any are present.

### Stage 7 — `MessageSerializer.Deserialize` (`COPY #R4` + `COPY #R5`)

```csharp
public static object Deserialize(ExtendedActorSystem system, SerializedMessage messageProtocol)
{
    return system.Serialization.Deserialize(
        messageProtocol.Message.ToByteArray(),                         // COPY #R4
        messageProtocol.SerializerId,
        !messageProtocol.MessageManifest.IsEmpty
            ? messageProtocol.MessageManifest.ToStringUtf8()           // COPY #R5
            : null);
}
```

- `ToByteArray()` allocates a new `byte[]` and `Buffer.BlockCopy`s the
  payload out of the `ByteString`. This is forced because the
  `Akka.Serialization.Serializer.FromBinary` API takes `byte[]`.
- `ToStringUtf8()` allocates a `string` for the manifest.

> 💡 **API-level fix:** `Akka.Serialization.Serializer` only exposes
> `FromBinary(byte[], …)`. We could add (non-breaking) a
> `FromBinary(ReadOnlySpan<byte>, …)` / `FromBinary(ReadOnlyMemory<byte>, …)`
> overload, plus a Google.Protobuf-specific `FromBinary(ByteString, …)`
> path that uses `ByteString.CodedInputStream` directly. Then we can
> hand the *aliased* `ByteString` from R3 straight into the serializer
> and skip R4 entirely. Manifests are short and string-interning could
> remove R5 in steady-state.

---

## 3. Where We Need a Buffer vs. Where We Can Pass

| Stage | Bytes flow            | Buffer required?                              | Why |
|------:|-----------------------|-----------------------------------------------|-----|
| 0     | kernel → byte[]       | **YES (pooled is fine)**                      | recv() needs a writable buffer; pool it. |
| 1     | byte[] → slice        | NO (zero-copy slice)                          | already zero-copy. |
| 2     | slice → ByteString    | **TODAY: yes**, future: no                    | needed only because `ByteString` cannot alias a refcounted buffer. Could use an `IMemoryOwner<byte>`-backed payload (see §4). |
| 3     | ProtocolStateActor    | NO                                            | pure pass-through of `ByteString`. |
| 4     | `DecodePdu`           | TODAY: yes (protobuf nested `bytes`)          | could be `ReadOnlyMemory<byte>` slice if we hand-roll the protobuf reader for the outer envelope, or upgrade to protobuf ≥3.27 + `AttachBytes`. |
| 5     | EndpointReader        | NO                                            | pass-through. |
| 6     | `DecodeMessage`       | TODAY: yes (protobuf nested `bytes`)          | same as #4 — outer envelope is small; the big payload is the inner `bytes Message`. |
| 7     | Serializer.FromBinary | **TODAY: yes** (`byte[]` API), future: span   | requires API extension. |

**Bottom line:** of the 4 user-payload copies (R1, R2, R3, R4), we can
realistically eliminate **R2 and R3** with a protobuf upgrade or a small
hand-rolled reader; we can eliminate **R4** with a serializer API
addition; **R1** is the hardest because it crosses the
DotNetty-refcount-domain → managed-message boundary.

---

## 4. Removing R1 with a Custom `IMemoryOwner<byte>` Payload

### 4.1 Design sketch

Introduce a new `IInboundPayload` abstraction that can carry **either**
a `ByteString` (today) **or** an `IMemoryOwner<byte>` slice that the
recipient explicitly disposes:

```csharp
// proposed: src/core/Akka.Remote/Transport/IInboundPayload.cs
public interface IInboundPayload : IDisposable
{
    ReadOnlyMemory<byte> Bytes { get; }
    int Length { get; }
}

internal sealed class PooledInboundPayload : IInboundPayload
{
    private IMemoryOwner<byte>? _owner;
    private readonly int _length;

    public PooledInboundPayload(IMemoryOwner<byte> owner, int length)
    {
        _owner = owner;
        _length = length;
    }

    public ReadOnlyMemory<byte> Bytes =>
        _owner is null
            ? throw new ObjectDisposedException(nameof(PooledInboundPayload))
            : _owner.Memory.Slice(0, _length);

    public int Length => _length;

    public void Dispose()
    {
        var o = Interlocked.Exchange(ref _owner, null);
        o?.Dispose();
    }
}
```

The `TcpHandlers.ChannelRead` rewrite:

```csharp
public override void ChannelRead(IChannelHandlerContext context, object message)
{
    var buf = (IByteBuffer)message;
    var len = buf.ReadableBytes;
    if (len > 0)
    {
        // Rent from a *transport-instance-owned* MemoryPool<byte> so we don't fight
        // with the rest of the application for ArrayPool<byte>.Shared slots.
        var owner = _pduPool.Rent(len);
        buf.GetBytes(buf.ReaderIndex, owner.Memory.Span.Slice(0, len)); // single memcpy
        NotifyListener(new PooledInboundPayloadEvent(new PooledInboundPayload(owner, len)));
    }
    ReferenceCountUtil.SafeRelease(message);
}
```

> Notice: **this is still one copy.** We did not eliminate the copy —
> we eliminated the *allocation* of the destination `byte[]`. R1 becomes
> "rent from pool + memcpy" instead of "allocate + memcpy". For
> sustained throughput that turns Gen0 pressure into amortised memory.

### 4.2 Choosing the right pool

| Pool                                | Pros                                         | Cons / when to avoid |
|-------------------------------------|----------------------------------------------|----------------------|
| `ArrayPool<byte>.Shared`            | zero setup, process-wide reuse               | shared with **all** other library code (Kestrel, ASP.NET, Newtonsoft, etc.). Burst behaviour from one subsystem can starve another; large rented arrays are silently thrown away (size > 1 MiB). PDUs up to `MaximumPayloadBytes` (default 128 KiB, tunable up to multi-MiB) can blow past `Shared`'s per-bucket caps. |
| `ArrayPool<byte>.Create(maxArrayLength, maxArraysPerBucket)` (instanced) | bounded by us, predictable behaviour, no cross-subsystem contention | one more thing to size; lifetime tied to `Akka.Remote` extension. |
| `MemoryPool<byte>.Shared`           | `IMemoryOwner<byte>` ergonomics              | wraps `ArrayPool<byte>.Shared` internally — **same downsides** as above. |
| Custom `MemoryPool<byte>` (per-transport) | full control over slab size & lifetime; can integrate with DotNetty `PooledByteBufferAllocator` later | implementation cost. |

**Recommendation:** instanced `ArrayPool<byte>` (or a thin
`MemoryPool<byte>` wrapper around it) **owned by the
`DotNettyTransport` instance**. Sizing:

- `maxArrayLength` = `Settings.MaxFrameSize` (default 128 KiB, but
  cluster users frequently set this to several MiB).
- `maxArraysPerBucket` ~ `WorkerPoolSize × 4`.

This keeps remoting's allocations isolated from the rest of the
process, makes the pool shut down with the transport, and avoids the
"silent drop on > 1 MiB" footgun in `ArrayPool<byte>.Shared`.

> **CopilotNotes:** if/when the codec is rewritten to walk
> `ReadOnlySequence<byte>` directly (Stages 4 & 6 above), we can rent
> *segments* and chain them via `ReadOnlySequenceSegment<byte>`,
> avoiding the contiguous-array constraint entirely. That is the right
> long-term shape, but is a much bigger refactor.

### 4.3 Disposal strategy / lifetime

The hard part is making sure `Dispose()` is actually called. The
classic remoting pipeline crosses **at least two actor mailboxes** (the
`ProtocolStateActor` and the `EndpointReader`) and one or two more if
there's a reliable-delivery buffer hop. Three options, in order of
preference:

1. **Pass the `IMemoryOwner<byte>` only as far as `EndpointReader`,
   then copy → `byte[]` for the serializer call (R4 collapsed into R1's
   memcpy).** Net effect: one rented-buffer memcpy + one serializer
   `byte[]` allocation, instead of today's R1 + R2 + R3 + R4.
2. **Pass it all the way into the serializer**, requires a span-friendly
   serializer API (see §3 R4). Best perf, but biggest API change.
3. **Reference-counted wrapper** (`Interlocked` retain/release) — works
   if a single PDU fans out to multiple consumers (e.g. heartbeat that
   also seeds the failure detector). Probably overkill for the read
   path, where the PDU is consumed exactly once.

---

## 5. Allocation Hotspot Cheat-Sheet

| ID  | Location | Call | Approx size | Eliminable? | Notes |
|-----|----------|------|-------------|------------|-------|
| R0  | `UnpooledByteBufferAllocator` (DotNetty) | `HeapBuffer(...)` | 1 KiB → 64 KiB | **YES** — switch to `PooledByteBufferAllocator` for read side | Per-recv `byte[]`. |
| R1  | `TcpHandlers.ChannelRead` | `ByteString.CopyFrom(buf.Array,…)` | full PDU | **Allocation YES, copy NO** — replace with pooled `IMemoryOwner<byte>` | Crosses refcount → mailbox boundary. |
| R2  | `AkkaPduCodec.DecodePdu` | `AkkaProtocolMessage.Parser.ParseFrom(raw)` → inner `ReadBytes()` | full PDU | **YES** with protobuf ≥ 3.27 `AttachBytes` *or* hand-rolled outer reader | Outer envelope is tiny; inner `bytes` field is the whole user message. |
| R3  | `AkkaPduCodec.DecodeMessage` | `AckAndEnvelopeContainer.Parser.ParseFrom(raw)` → inner `ReadBytes()` | full message | **YES** same fix as R2 | Plus small POCO allocs for `ActorRefData`, `Nacks` list. |
| R4  | `MessageSerializer.Deserialize` | `messageProtocol.Message.ToByteArray()` | full message | **YES** with span-aware `Serializer.FromBinary(ReadOnlySpan<byte>)` | Currently mandated by the public `Serializer` API. |
| R5  | `MessageSerializer.Deserialize` | `messageProtocol.MessageManifest.ToStringUtf8()` | manifest len (tens of bytes) | partial (intern manifests per-`SerializerId`) | Per-message string. |
| R6  | `EndpointReader.Reading` | `new InboundPayload(...)` envelope alloc | ~24 B | **NO** — actor mailbox primitive | Per-PDU. |
| R7  | `ActorPathCache.GetOrCompute(path)` | mostly amortised | — | already cached | n/a. |

---

## 6. Suggested Implementation Order (smallest blast radius first)

1. **Switch read-side `ChannelOption.Allocator` to a pooled allocator**
   (`PooledByteBufferAllocator(preferDirect: false)` instance owned by
   `DotNettyTransport`). Kills **R0** at the cost of one config change.
   No public API impact. ✨
2. **Introduce `IInboundPayload` + `PooledInboundPayload`** backed by
   an instanced `ArrayPool<byte>`; route through `TcpHandlers` →
   `EndpointReader` (kept internal, `InboundPayload(ByteString)` stays
   for back-compat). Eliminates **R1**'s allocation. ✨
3. **Bump `Google.Protobuf` to ≥ 3.27** and migrate the codec to
   `ParseFrom(ReadOnlyMemory<byte>)` with `AttachBytes`. Eliminates
   **R2 and R3** with no API change. ✨
4. **Add `Serializer.FromBinary(ReadOnlySpan<byte>, …)`** virtual with
   default impl = `FromBinary(span.ToArray(), …)`. Override in built-in
   serializers (`NewtonSoftJson`, `Protobuf`, `Hyperion`,
   `ByteArraySerializer`). Eliminates **R4** for serializers that
   opt in. ✨
5. (Optional) **Manifest interning** keyed on `(SerializerId, manifest
   ReadOnlySpan<byte>)` via a `ConcurrentDictionary<…>` keyed by hash.
   Eliminates **R5** in steady state. ✨

Each step is independently shippable and incrementally measurable with
`BenchmarkDotNet` against the existing benchmarks in
`src/benchmark/RemotePingPong`. uwu let's gooo~ 🌸

---

# 7. `TcpPipeTransport` (Pipelines) — Read Pipeline 🌟

> **Targets analysed:**
> - `src/core/Akka.Remote/Transport/Pipelines/TcpPipeTransport.cs`
> - `src/core/Akka.Remote/Transport/Pipelines/PipeConnection.cs` (read loop)
> - `src/core/Akka.Remote/Transport/Pipelines/AkkaPduMessagePackCodec.cs`
>   (when `envelope = messagepack`, otherwise the same
>   `AkkaPduProtobuffCodec` analysed in §1–§5 is used)
>
> **Activation:** `akka.remote.enabled-transports = ["akka.remote.pipe.tcp"]`
>
> **Verdict (TL;DR):** the new pipelines transport **already eliminates
> R0** (kernel-recv buffer is now pooled by `PipeReader`), turns frame
> slicing into a true zero-copy `ReadOnlySequence<byte>` walk, but
> **still pays R1** (one `ByteString.CopyFrom` per frame) and inherits
> R2–R5 from the upper layer. The MessagePack codec changes the shape
> of R2/R3 but does **not** make the pipeline overall cheaper without
> the same `IInboundPayload` work proposed in §4. uwu it's already
> 80% of the way there~ ✨

## 7.1 End-to-end Pipeline Diagram (Pipe variant)

```
                      ┌────────────────────────────────────┐
 Kernel socket (TCP)  │ recv() into a buffer rented from   │
                      │ MemoryPool<byte>.Shared by         │
                      │ PipeReader.Create(stream)          │  ← pooled (R0 GONE ✨)
                      └──────────────┬─────────────────────┘
                                     │ ReadAsync()
                                     ▼
                      ┌────────────────────────────────────┐
                      │ PipeConnection.ReadLoopAsync       │
                      │   ReadResult.Buffer is a           │
                      │   ReadOnlySequence<byte> aliasing  │  ZERO-COPY
                      │   the pooled segments              │
                      └──────────────┬─────────────────────┘
                                     │ TryParseFrame(ref buffer, out frame)
                                     │   ├─ stackalloc byte[4] header  (no heap alloc)
                                     │   └─ buffer.Slice(...) zero-copy
                                     ▼
                      ┌────────────────────────────────────┐
                      │ frame: ReadOnlySequence<byte>      │
                      │   bytes = frame.IsSingleSegment    │
                      │      ? ByteString.CopyFrom(span)   │ ◀── COPY #PR1 (full PDU)
                      │      : ByteString.CopyFrom(arr)    │      slow path: ToArray() then CopyFrom
                      │   listener.Notify(InboundPayload)  │
                      └──────────────┬─────────────────────┘
                                     │ AdvanceTo(buffer.Start, buffer.End)
                                     │ → returns segments to pool ✨
                                     ▼ (cross-thread → actor mailbox)
                      ┌────────────────────────────────────┐
                      │ ProtocolStateActor (FSM)           │  same as §1
                      │   AkkaPduCodec.DecodePdu(raw)      │ ◀── COPY #PR2
                      └──────────────┬─────────────────────┘
                                     ▼
                              EndpointReader
                                     │
                                     ▼ (same as §1, R3 + R4 + R5)
                              MessageSerializer.Deserialize
```

So the pipelines transport's read side is **structurally identical to
DotNetty's from `InboundPayload` onwards** — that's the whole point of
the Phase 1 design (the `Transport` SPI is unchanged). The wins live
strictly in the bottom two boxes. ✨

## 7.2 Stage-by-stage allocation deltas vs. DotNetty

| ID  | DotNetty equivalent | Pipe variant | Δ |
|-----|---------------------|--------------|----|
| **PR0** | R0 (`UnpooledByteBufferAllocator` per-recv `byte[]`) | `PipeReader.Create(stream)` rents from `MemoryPool<byte>.Shared` (= `ArrayPool<byte>.Shared` under the hood) | ✅ **eliminated** as a per-recv allocation; reuses pool slots. |
| **PR-frame** | `LengthFieldBasedFrameDecoder` slice (zero-copy) | `TryParseFrame` slices a `ReadOnlySequence<byte>` (zero-copy) + `stackalloc byte[4]` for the header read | ✅ **same or better** — the stackalloc avoids even the small `IByteBuffer` envelope. |
| **PR1** | R1 (`ByteString.CopyFrom(buf.Array,…)`) | `ByteString.CopyFrom(frame.FirstSpan)` (fast path) **or** `ByteString.CopyFrom(frame.ToArray())` (slow, multi-segment path) | ⚠️ **same alloc & copy count, but slow path is worse** — `frame.ToArray()` allocates **twice**: once for the contiguous staging array, once for the `ByteString`. See §7.3. |
| **PR-mailbox** | new `InboundPayload(...)` envelope + actor mailbox slot | identical | ☑️ unchanged. |
| **PR2 / PR3** | R2/R3 (`Parser.ParseFrom(ByteString)` + nested `ReadBytes()`) | **Protobuf codec:** identical to R2/R3. **MessagePack codec:** `MessagePackSerializer.Deserialize<MpProtocolFrame>(raw.ToByteArray())` in `DecodePdu` — see §7.4. | ⚠️ MessagePack `DecodePdu` is **strictly worse** today; `DecodeMessage` is roughly equivalent. |
| **PR4** | R4 (`messageProtocol.Message.ToByteArray()`) | identical (same `MessageSerializer.Deserialize` is used) | ☑️ unchanged. |
| **PR5** | R5 manifest UTF-8 decode | identical | ☑️ unchanged. |

### What the pipelines transport gives you for free

1. **No per-recv `byte[]` allocation.** `PipeReader` rents segments
   from `MemoryPool<byte>.Shared` and returns them on `AdvanceTo`. On
   a steady-state busy connection this is ~zero Gen0 pressure for
   the recv buffer.
2. **Multi-frame batching.** A single `ReadAsync` can yield several
   frames; the inner `while (TryParseFrame(...))` loop drains them
   all before another `ReadAsync`. This amortises the `await`
   state-machine cost over many PDUs.
3. **No DotNetty refcount domain.** There's no `IByteBuffer`,
   `ReferenceCountUtil.SafeRelease`, or per-pipeline allocator object
   bookkeeping. `PipeReader.AdvanceTo` does it all.

### What it does **not** give you

1. **`ByteString.CopyFrom` is still mandatory at the `InboundPayload`
   boundary** — the `Transport` SPI hands `ByteString` (not
   `ReadOnlyMemory<byte>`) to `IHandleEventListener`. That's the same
   public contract the DotNetty transport must honour.
2. **The codec layer (`AkkaPduCodec.DecodePdu` /
   `DecodeMessage`) is unchanged.** All R2–R5 wins from §3 carry
   over identically here when implemented.

## 7.3 Hotspot deep-dive — `PipeConnection.ReadLoopAsync`

```csharp
// PipeConnection.cs, inside the read loop
while (TryParseFrame(ref buffer, out var frame))
{
    // CopilotNotes: ByteString.CopyFrom allocates per frame (the frame bytes are
    // already in a pooled PipeReader buffer). Phase 2 can avoid the copy by
    // teaching IHandleEventListener about ReadOnlySequence<byte> directly.
    var bytes = frame.IsSingleSegment
        ? ByteString.CopyFrom(frame.FirstSpan)        // FAST PATH — 1 alloc + 1 memcpy
        : ByteString.CopyFrom(frame.ToArray());       // SLOW PATH — 2 allocs + 2 memcpys

    listener.Notify(new InboundPayload(bytes));
}

_reader.AdvanceTo(buffer.Start, buffer.End);
```

- **Fast path (single-segment frame):** `ByteString.CopyFrom(span)`
  allocates one `byte[span.Length]`, copies, wraps via `AttachBytes`.
  Same cost as DotNetty's `R1`.
- **Slow path (multi-segment frame):** `frame.ToArray()` allocates
  a **temporary** contiguous `byte[]` and copies all segments into
  it. Then `ByteString.CopyFrom(byte[])` copies it **again** into a
  fresh array. **Two full PDU copies and two allocations** — strictly
  worse than the fast path.

  **Easy fix (≤ 5 lines):**

  ```csharp
  // PipeConnection.cs — eliminate the slow path's double copy
  ByteString bytes;
  if (frame.IsSingleSegment)
  {
      bytes = ByteString.CopyFrom(frame.FirstSpan);
  }
  else
  {
      // Allocate once, copy segments into it, AttachBytes to take ownership.
      // Same cost as the fast path: 1 alloc + 1 memcpy total.
      var arr = new byte[frame.Length];
      frame.CopyTo(arr);
      bytes = ByteString.AttachBytes(arr); // protobuf >= 3.x supports this
  }
  ```

  > **CopilotNotes:** Multi-segment frames are common when (a) a frame
  > straddles a `PipeReader` segment boundary (≈ 4 KiB on .NET 8), or
  > (b) TLS is enabled (`SslStream` decryption tends to chop output).
  > So this is not a corner case.

- **Pool sizing:** `PipeReader.Create(stream)` uses
  `StreamPipeReaderOptions.Default`, which uses
  `MemoryPool<byte>.Shared` → `ArrayPool<byte>.Shared` with default
  segment size **4 KiB** and pause/resume thresholds at **64 KiB /
  32 KiB**. For Akka.Remote workloads where `MaxFrameSize` is often
  tens or hundreds of KiB, **the default thresholds throttle reads**
  before a single large frame can arrive in one go, forcing the
  multi-segment slow path more often than necessary.

  > 💡 **Easy win:** construct the `PipeReader` with explicit
  > options:
  >
  > ```csharp
  > _reader = PipeReader.Create(stream, new StreamPipeReaderOptions(
  >     pool:                   _settings.PipeMemoryPool ?? MemoryPool<byte>.Shared,
  >     bufferSize:             Math.Min(64 * 1024, (int)_settings.MaxFrameSize),
  >     minimumReadSize:        4096,
  >     leaveOpen:              true));
  > ```
  >
  > Then expose `PipeMemoryPool` on `PipeTransportSettings` so
  > operators can plug in an instanced pool (see §4.2 above) when they
  > don't want to share `ArrayPool<byte>.Shared` with the rest of the
  > process. Same pool argument as the DotNetty side.

## 7.4 MessagePack codec (`envelope = messagepack`) read side

The MessagePack codec is opt-in (`akka.remote.pipe.tcp.envelope =
messagepack`) and is **codec-equivalent** to the protobuf codec, but
with a few interesting allocation differences.

### `DecodePdu` — currently regressed vs. protobuf

```csharp
// AkkaPduMessagePackCodec.cs
public override IAkkaPdu DecodePdu(ByteString raw)
{
    var frame = MP.MessagePackSerializer.Deserialize<MpProtocolFrame>(raw.ToByteArray()); // COPY #PR2-mp
    return frame.Tag switch
    {
        ProtocolTag.Payload =>
            new Payload(frame.Payload is { Length: > 0 }
                ? ByteString.CopyFrom(frame.Payload)                                       // COPY #PR2b-mp
                : ByteString.Empty),
        ...
    };
}
```

Two avoidable copies per inbound PDU:

1. `raw.ToByteArray()` — a full copy of the inbound PDU's bytes
   into a fresh array, just so MessagePack can read it.
   `MessagePackSerializer.Deserialize<T>` has a
   `ReadOnlyMemory<byte>` overload (already used in `DecodeMessage`
   below). Switch to `raw.Memory` and the copy disappears.
2. `ByteString.CopyFrom(frame.Payload)` — `frame.Payload` is the
   MessagePack-deserialised inner `byte[]`, which is freshly
   allocated and otherwise unreferenced. Use
   `ByteString.AttachBytes(frame.Payload)` to skip the second copy.

> **Easy win:** ~4-line patch eliminates **both** copies in
> `DecodePdu`. After this fix, the MessagePack `DecodePdu` matches
> protobuf's `DecodePdu` semantically and is **cheaper** per PDU
> (MessagePack has lower per-byte parsing overhead than protobuf for
> this schema).

### `DecodeMessage` — already optimal-ish

```csharp
var msg = MP.MessagePackSerializer.Deserialize<MpAckAndEnvelope>(raw.Memory); // ✅ no PDU copy
...
var serializedMessage = new SerializedMessage
{
    Message = env.Message.Message is { Length: > 0 }
        ? ByteString.CopyFrom(env.Message.Message.Value.Span)                  // COPY #PR3-mp
        : ByteString.Empty,
    SerializerId    = env.Message.SerializerId,
    MessageManifest = env.Message.Manifest is { Length: > 0 }
        ? ByteString.CopyFrom(env.Message.Manifest)                            // COPY #PR3b-mp (small)
        : ByteString.Empty
};
```

- `raw.Memory` keeps `DecodeMessage` zero-copy at the outer envelope
  level (✨ better than the protobuf codec, which copies the whole PDU
  into `CodedInputStream` in some paths).
- `ByteString.CopyFrom(env.Message.Message.Value.Span)` copies the
  user payload again. Same `AttachBytes` opportunity:
  `env.Message.Message` is a `byte[]?` produced by MessagePack's
  formatter and is otherwise unreferenced — switching to
  `ByteString.AttachBytes(env.Message.Message)` kills `COPY #PR3-mp`.

> **Easy win:** ~2-line patch eliminates the user-payload copy in
> `DecodeMessage` for the MessagePack codec.

## 7.5 Suggested implementation order (Pipe-specific)

Stack these on top of the cross-cutting steps from §6:

1. **Fix the multi-segment slow path in `PipeConnection.ReadLoopAsync`**
   (use `frame.CopyTo(arr)` + `ByteString.AttachBytes(arr)`). Kills
   the second alloc + memcpy in the slow path. ✨
2. **Configure `PipeReader` with explicit
   `StreamPipeReaderOptions`** sized to `MaxFrameSize` (and let the
   user inject a `MemoryPool<byte>`). Reduces the multi-segment
   probability for large frames. ✨
3. **Switch `AkkaPduMessagePackCodec.DecodePdu` to read from
   `raw.Memory`** and use `ByteString.AttachBytes(frame.Payload)`.
   Kills two copies. ✨
4. **Switch `AkkaPduMessagePackCodec.DecodeMessage` to use
   `ByteString.AttachBytes(env.Message.Message)`** for the inner
   payload. Kills one full PDU copy. ✨
5. **Phase 2 (cross-cutting):** the `IInboundPayload` /
   `IMemoryOwner<byte>` design from §4 lights up beautifully on this
   transport because the read loop already has an `AdvanceTo`
   refcount-style protocol — we can rent a buffer from
   `_settings.PipeMemoryPool`, `frame.CopyTo` into it once, then
   release on dispose. The mailbox-crossing problem (§4.3) is the
   same as DotNetty's; the upside is that the read-loop side is
   already span-friendly. ✨

> **Closing CopilotNotes:** the pipelines transport is the right
> place to land the eventual span-aware `IHandleEventListener`
> overload first, because it already speaks `ReadOnlySequence<byte>`
> end-to-end on the read side. The DotNetty transport can adopt the
> same overload by wrapping its `IByteBuffer` slice as a single-
> segment `ReadOnlySequence<byte>` — **but only after copying it out
> of the refcounted DotNetty buffer**, which puts us right back at
> R1. So: pipelines transport → leads, DotNetty → follows. uwu
> kawaii ordering~ 🌸
