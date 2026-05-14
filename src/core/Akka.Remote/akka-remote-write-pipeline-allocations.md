# Akka.Remote Write Pipeline — Allocation Analysis ✨ (uwu edition)

> **Scope:** the outbound (write) side of the classic `Akka.Remote`
> DotNetty TCP transport. Covers the journey from
> `IActorRef.Tell(message)` → `EndpointWriter` → DotNetty pipeline →
> kernel `send()`.
>
> **Targets analysed (May 2026 / `dev` branch):**
> - `src/core/Akka.Remote/Endpoint.cs` (EndpointWriter, `WriteSend`,
>   `TrySendPureAck`, `SerializeMessage`)
> - `src/core/Akka.Remote/MessageSerializer.cs`
> - `src/core/Akka.Remote/Transport/AkkaPduCodec.cs`
> - `src/core/Akka.Remote/Transport/AkkaProtocolTransport.cs`
> - `src/core/Akka.Remote/Transport/DotNetty/TcpTransport.cs`
>   (`TcpAssociationHandle.Write`)
> - `src/core/Akka.Remote/Transport/DotNetty/DotNettyTransport.cs`
>   (`LengthFieldPrepender`, `HeliosBackwardsCompatabilityLengthFramePrepender`)
> - `src/core/Akka.Remote/Transport/DotNetty/BatchWriter.cs`
>
> **Versions:** `Google.Protobuf 3.26.1`, `DotNetty 0.7.x`,
> `net8.0` / `net6.0` / `net48`.

---

## 1. End-to-end Pipeline Diagram

```
                    user code (any thread)
                           │ Tell(msg)
                           ▼
              ┌───────────────────────────────┐
              │ RemoteActorRef → outbound     │
              │ EndpointManager.Send envelope │  ALLOC #W0a (envelope)
              └──────────────┬────────────────┘
                             │ mailbox hop
                             ▼
              ┌───────────────────────────────┐
              │ EndpointWriter.WriteSend      │
              │   SerializeMessage(msg)       │
              │     ISerializer.ToBinary(msg) │ ◀── ALLOC #W1 (byte[] from serializer)
              │     ByteString.CopyFrom(...)  │ ◀── COPY  #W2 (full payload)
              │     ByteString.CopyFromUtf8() │ ◀── COPY  #W3 (manifest)
              │     SerializedMessage POCO    │     ALLOC #W0b
              └──────────────┬────────────────┘
                             │
                             ▼
              ┌───────────────────────────────┐
              │ AkkaPduCodec.ConstructMessage │
              │   AckAndEnvelopeContainer     │  ALLOC #W0c (proto POCOs)
              │     RemoteEnvelope            │
              │     ActorRefData (×1 or ×2)   │
              │   .ToByteString()             │ ◀── ALLOC #W4 (full PDU byte[])
              └──────────────┬────────────────┘
                             │
                             ▼
              ┌───────────────────────────────┐
              │ AssociationHandle.Write(pdu)  │
              │ (AkkaProtocolHandle wraps     │
              │  with another                  │
              │  AkkaProtocolMessage proto)    │ ◀── ALLOC #W5 (outer envelope byte[])
              │   ConstructPayload(pdu)        │
              │     .ToByteString()            │
              └──────────────┬────────────────┘
                             │
                             ▼
              ┌───────────────────────────────┐
              │ TcpAssociationHandle.Write    │
              │   payload.ToByteArray()       │ ◀── COPY #W6 (full PDU)
              │   Unpooled.WrappedBuffer(arr) │      ZERO-COPY wrap
              │   _channel.WriteAndFlushAsync │
              └──────────────┬────────────────┘
                             │
                             ▼
              ┌───────────────────────────────┐
              │ LengthFieldPrepender          │
              │   allocates 4-byte len buffer │ ◀── ALLOC #W7 (4 B header buffer)
              │   (or HeliosCompat: copies    │      Helios path: COPY #W7b (full PDU)
              │    msg into combined buffer)  │
              └──────────────┬────────────────┘
                             │
                             ▼
              ┌───────────────────────────────┐
              │ FlushConsolidationHandler     │  (no alloc, pure flow control)
              └──────────────┬────────────────┘
                             │
                             ▼
              ┌───────────────────────────────┐
              │ TLS handler (optional)        │
              │   SslStream encrypts in-place │  +1 copy if SSL enabled
              │   into a fresh write buffer   │
              └──────────────┬────────────────┘
                             │
                             ▼
                       socket send()
```

A single outbound user message therefore performs, in the **non-TLS,
non-Helios** default path:

- **3 full-payload copies** of the user bytes (W2, W6, plus protobuf's
  internal copy when `ToByteString()` walks `WriteRawBytes` for the
  `bytes` field inside W4).
- **2 full-payload `byte[]` allocations** beyond the user's own
  serializer output (W4, W6 destination).
- **1 outer-envelope `byte[]` allocation** (W5).
- A handful of small POCO allocations for protobuf wrappers (W0b, W0c).
- **1 short-lived 4-byte length-prefix buffer** (W7) per write, also
  unpooled.

---

## 2. Stage-by-stage Allocation Breakdown

### Stage 0 — `EndpointManager.Send` envelope (`ALLOC #W0a`)

The `Send` envelope is a small reference type carrying recipient,
sender, message, and seq. **Per-Tell, per-association** allocation.
This is part of the actor model and not a transport concern.

### Stage 1 — `MessageSerializer.Serialize` (`ALLOC #W1`, `COPY #W2`, `COPY #W3`)

```csharp
// MessageSerializer.cs
var serializedMsg = new SerializedMessage
{
    Message = ByteString.CopyFrom(serializer.ToBinary(message)), // ALLOC #W1 + COPY #W2
    SerializerId = serializer.Identifier
};
if (serializer is SerializerWithStringManifest s2)
{
    var manifest = s2.Manifest(message);
    if (!string.IsNullOrEmpty(manifest))
        serializedMsg.MessageManifest = ByteString.CopyFromUtf8(manifest); // COPY #W3
}
else if (serializer.IncludeManifest)
{
    serializedMsg.MessageManifest = ByteString.CopyFromUtf8(message.GetType().TypeQualifiedName()); // COPY #W3
}
```

- `serializer.ToBinary(message)` returns a fresh `byte[]` — the
  serializer cannot reuse a pooled buffer because the API contract
  returns ownership. (`ALLOC #W1`)
- `ByteString.CopyFrom(byte[])` then **copies the entire array again**
  into another `byte[]` and wraps it. (`COPY #W2`)
  - This second copy is gratuitous given the array is freshly allocated
    and otherwise unreferenced. `ByteString.AttachBytes(byte[])` would
    take ownership without copying — it exists in Google.Protobuf and
    is exactly the right tool.
- `CopyFromUtf8(manifest)` allocates a UTF-8 byte array sized via
  `Encoding.UTF8.GetByteCount` and encodes into it. (`COPY #W3`)

> 💡 **Easy win #1:** swap `ByteString.CopyFrom(serializer.ToBinary(...))`
> for `ByteString.AttachBytes(serializer.ToBinary(...))`. This kills
> `COPY #W2` outright with **zero** behavioural change — the
> `byte[]` returned by `ToBinary` is owned by us and is not mutated
> after the call. This is a one-line patch.
>
> **CopilotNotes:** double-check that no built-in `Serializer` returns
> a buffer it intends to reuse — historically all Akka.NET serializers
> (`NewtonSoftJson`, `Hyperion`, `Protobuf`, `ByteArraySerializer`)
> allocate fresh, but if any third-party serializer leaked a pooled
> buffer this would become observable.

### Stage 2 — `AkkaPduCodec.ConstructMessage` (`ALLOC #W4` + small POCO allocs)

```csharp
// AkkaPduCodec.cs
public override ByteString ConstructMessage(Address localAddress, IActorRef recipient,
    SerializedMessage serializedMessage, IActorRef senderOption = null,
    SeqNo? seqOption = null, Ack ackOption = null)
{
    var ackAndEnvelope = new AckAndEnvelopeContainer();        // ALLOC #W0c
    var envelope = new RemoteEnvelope { Recipient = SerializeActorRef(...) }; // + ActorRefData alloc
    if (senderOption?.Path != null) envelope.Sender = SerializeActorRef(...); // + ActorRefData alloc
    if (seqOption is { } seq) envelope.Seq = (ulong)seq.RawValue; else envelope.Seq = SeqUndefined;
    if (ackOption != null) ackAndEnvelope.Ack = AckBuilder(ackOption);
    envelope.Message = serializedMessage;
    ackAndEnvelope.Envelope = envelope;
    return ackAndEnvelope.ToByteString();                      // ALLOC #W4 (one byte[] sized to total proto length)
}
```

- `ToByteString()` calls `IMessage.CalculateSize()`, allocates a
  `byte[size]`, runs `WriteTo(CodedOutputStream)` over it, then wraps
  with `ByteString.AttachBytes`. So **one full PDU-sized `byte[]`** is
  allocated per outbound message (`ALLOC #W4`).
- Internally the inner `bytes Message` field is written via
  `CodedOutputStream.WriteRawBytes(byteString.Span)` — that step does
  **one memcpy** of the user payload into the destination array.
- Path strings: `actorRef.Path.ToSerializationFormat()` allocates a
  `string` (it's cached in some hot paths via `ActorPath.ToString`
  internals, but the serialization-format string still requires
  formatting per call).

> 💡 **Optimisation:** `RemoteEnvelope.Recipient.Path` strings are
> highly repetitive (the same recipient and sender appear millions of
> times). A small per-`EndpointWriter` LRU cache keyed on
> `IActorRef` → cached `ActorRefData` (or even cached
> pre-serialised `ByteString` chunks for the `Recipient`/`Sender`
> field) would cut both string and POCO allocations in steady state.
> This was historically prototyped under "remote envelope caching"
> and proved measurable.

### Stage 3 — `AkkaProtocolHandle.Write` → `ConstructPayload` (`ALLOC #W5`)

The Akka protocol layer wraps the message PDU once more in an
`AkkaProtocolMessage`:

```csharp
public override ByteString ConstructPayload(ByteString payload)
{
    return new AkkaProtocolMessage { Payload = payload }.ToByteString(); // ALLOC #W5
}
```

- Same `ToByteString()` cost as Stage 2.
- This wrapping is what makes a control PDU (heartbeat, associate,
  disassociate) distinguishable from a data PDU. The cost is
  fundamental to the wire format **but** is wasted size: the outer
  envelope adds ~4–6 bytes of protobuf framing per message, but
  forces a *full* re-serialisation copy of the payload.

> 💡 **Optimisation:** because the outer envelope is just
> `oneof { instruction, payload }`, we could synthesise the bytes by
> hand:
>
> ```csharp
> // Pseudocode: write [tag for Payload][varint(payload.Length)][payload bytes]
> ```
>
> i.e. allocate `4 + Varint(len) + payload.Length` and `Buffer.BlockCopy`
> the payload into it. This collapses the cost of W5 from
> "alloc + memcpy whole payload via CodedOutputStream" to
> "alloc + memcpy whole payload" with much less protobuf machinery
> overhead. Or, with a span-aware `IBufferWriter<byte>`-style codec,
> we can write directly into a pooled buffer (see §4).

### Stage 4 — `TcpAssociationHandle.Write` (`COPY #W6`)

```csharp
// TcpTransport.cs
public override bool Write(ByteString payload)
{
    if (_channel.Open)
    {
        var data = ToByteBuffer(_channel, payload);
        _channel.WriteAndFlushAsync(data);
        return true;
    }
    return false;
}

private static IByteBuffer ToByteBuffer(IChannel channel, ByteString payload)
{
    var buffer = Unpooled.WrappedBuffer(payload.ToByteArray()); // COPY #W6 + ALLOC for byte[]
    return buffer;
}
```

- `payload.ToByteArray()` allocates a brand-new `byte[]` and
  `Buffer.BlockCopy`s the whole `ByteString` into it. **A complete
  copy of the entire PDU** for no semantic reason (`Unpooled.WrappedBuffer`
  is happy to alias).
- Then `Unpooled.WrappedBuffer(byte[])` aliases that fresh array as
  an `IByteBuffer` (zero copy).

> 💡 **Easy win #2:** `ByteString` exposes `Memory` and `Span` (in
> Google.Protobuf 3.x via `ByteString.Memory` getter). We can do:
>
> ```csharp
> // CopilotNotes: ByteString.Memory returns a ReadOnlyMemory<byte> aliasing the
> // underlying byte[]. Try MemoryMarshal.TryGetArray to recover the array
> // segment, then Unpooled.WrappedBuffer(segment.Array, segment.Offset, segment.Count).
> if (MemoryMarshal.TryGetArray<byte>(payload.Memory, out var seg))
>     return Unpooled.WrappedBuffer(seg.Array!, seg.Offset, seg.Count);
> // fallback for the unlikely non-array-backed ByteString
> return Unpooled.WrappedBuffer(payload.ToByteArray());
> ```
>
> All `ByteString`s constructed in the entire write pipeline above are
> array-backed (they came from `ToByteString()` which uses
> `AttachBytes(byte[])`), so the fast-path always wins. **This kills
> `COPY #W6` with a ~6-line patch and no API change.** uwu

### Stage 5 — `LengthFieldPrepender` (`ALLOC #W7`)

DotNetty's stock `LengthFieldPrepender`:

```text
encode(ctx, msg):
  out.add(ctx.alloc().buffer(4).writeInt(msg.readableBytes())) // ALLOC #W7 (4-byte header)
  out.add(msg.retain())                                         // zero-copy
```

- Allocates a tiny 4-byte `IByteBuffer` from the channel allocator.
  Because `DotNettyTransport` configures
  `Allocator = UnpooledByteBufferAllocator.Default`, this is **a fresh
  4-byte `byte[]` per write**. That's surprisingly large per-write
  overhead at high message rates.

#### `HeliosBackwardsCompatabilityLengthFramePrepender` (`COPY #W7b`)

If `BackwardsCompatibilityModeEnabled` is on:

```csharp
protected override void Encode(IChannelHandlerContext context, IByteBuffer message, List<object> output)
{
    base.Encode(context, message, output);
    var lengthFrame = (IByteBuffer)_temporaryOutput[0];
    var combined = lengthFrame.WriteBytes(message);              // COPY #W7b — full PDU into combined buffer
    ReferenceCountUtil.SafeRelease(message, 1);
    output.Add(combined.Retain());
    _temporaryOutput.Clear();
}
```

This explicitly **memcpys the entire PDU** into a combined buffer (so
that Helios-compatible peers see the length-prefix and payload as one
contiguous frame). Adds **another full copy** when in Helios mode.

> 💡 **Optimisation:** for the non-Helios path, switching the channel
> allocator to a pooled allocator amortises `ALLOC #W7`. For the
> Helios path, this is intrinsic to the wire format — we *must*
> produce one contiguous buffer, so the copy is unavoidable. The only
> mitigation is to rent the combined buffer from a pool sized to
> `4 + payload.Length`.

### Stage 6 — `FlushConsolidationHandler` and TLS

- `FlushConsolidationHandler` is allocation-free per write (it just
  bumps an int counter).
- `TlsHandler` (when SSL is enabled) calls `SslStream.Write(buffer)`
  which **always copies into a fresh encryption buffer** internally.
  This is unavoidable at the BCL level but is worth knowing — TLS
  implies +1 full-payload copy and +1 GCM/CBC working-set buffer.

---

## 3. Where We Need a Buffer vs. Where We Can Pass

| Stage | Bytes flow                | Buffer required?                      | Why |
|------:|---------------------------|---------------------------------------|-----|
| 1     | serializer → `byte[]`     | **YES** (serializer owns it)          | external API contract. |
| 1→2   | `byte[]` → `ByteString`   | **NO** (use `AttachBytes`)            | currently uses `CopyFrom` — gratuitous copy. |
| 2     | proto envelope → `byte[]` | **YES** (`ToByteString` builds it)    | could be replaced by writing directly into a pooled `IBufferWriter<byte>`. |
| 3     | proto outer → `byte[]`    | **YES** (or hand-rolled)              | small overhead, big copy — could be a tag+varint+memcpy. |
| 4     | `ByteString` → `IByteBuffer` | **NO** (use `MemoryMarshal.TryGetArray`) | currently `ToByteArray()` — gratuitous copy. |
| 5     | length-prefix             | **YES** (4 bytes)                     | pool the channel allocator. |
| 6     | TLS encryption            | **YES** (BCL internal)                | not in our control. |

**Bottom line:** of the 3+ user-payload copies on the write side, **W2
and W6 are pure waste** and can be removed with surgical patches today.
W4 and W5 are protobuf-shaped and need a more involved codec rewrite
(see §4) but together they account for the majority of LOH pressure
in big-message scenarios.

---

## 4. Pooled Output via `IBufferWriter<byte>` and `IMemoryOwner<byte>`

### 4.1 Design sketch

The desired endgame on the write side is:

1. Rent a single contiguous buffer from a pool, sized to
   `len(envelope) + len(payload)`.
2. Write the protobuf envelope **directly into** that buffer using a
   `CodedOutputStream` wrapping a `Span<byte>` (Google.Protobuf 3.15+
   supports `WriteTo(IBufferWriter<byte>)` and
   `WriteTo(Span<byte>)`).
3. Wrap the rented buffer as a DotNetty `IByteBuffer` whose `Release`
   returns it to the pool.

```csharp
// proposed shape
internal interface IPduWriter
{
    /// <summary>Rent a buffer big enough to hold a fully framed PDU and write into it.</summary>
    IByteBuffer Encode(SerializedMessage message, RemoteEnvelopeContext ctx);
}

internal sealed class PooledPduWriter : IPduWriter
{
    private readonly ArrayPool<byte> _pool;
    public PooledPduWriter(ArrayPool<byte> pool) => _pool = pool;

    public IByteBuffer Encode(SerializedMessage message, RemoteEnvelopeContext ctx)
    {
        var envelopeSize = ComputeOuterEnvelopeSize(ctx, message);
        var buffer = _pool.Rent(envelopeSize);
        try
        {
            var written = WriteEnvelope(buffer, ctx, message); // direct Span<byte> writer, no protobuf POCO allocs
            // Hand ownership to a custom IByteBuffer that returns to _pool on Release().
            return new PooledArrayByteBuffer(_pool, buffer, written);
        }
        catch
        {
            _pool.Return(buffer);
            throw;
        }
    }
}
```

`PooledArrayByteBuffer` is a thin `AbstractReferenceCountedByteBuf`
subclass that wraps `byte[]` and on `Deallocate()` returns the array
to the pool. DotNetty already has `AbstractByteBufferAllocator` and
`UnpooledHeapByteBuffer` patterns to copy from.

### 4.2 Choosing the right pool (write side)

Same considerations as the read side, with two extras:

1. **Outbound writes are bursty** — `EndpointWriter` may flush dozens
   of PDUs in one batch. The pool needs to tolerate "rent N before
   any are returned" without falling back to allocations.
2. **Sizes are skewed** — most PDUs are small (acks, heartbeats,
   small commands), but a minority can be very large (cluster gossip,
   sharding rebalance, user blobs near `MaxFrameSize`). A
   well-bucketed `ArrayPool<byte>.Create(maxArrayLength,
   maxArraysPerBucket)` outperforms `ArrayPool<byte>.Shared` here
   because:
   - `Shared` silently drops returns of arrays > 1 MiB (so the largest
     PDUs are effectively un-pooled — exactly the worst case for GC).
   - `Shared` has a per-thread cache + per-core bucket; bursty writes
     from a single `EndpointWriter` thread saturate one core's cache
     and start hitting the slow path quickly.

| Pool                               | Verdict for write side |
|------------------------------------|------------------------|
| `ArrayPool<byte>.Shared`           | ⚠️ acceptable for *small* PDUs only; fails for large frames. |
| `ArrayPool<byte>.Create(maxArrayLength = MaxFrameSize, maxArraysPerBucket = workers × 8)` per-transport | ✅ recommended. |
| Per-`EndpointWriter` thread-local pool (custom) | 🔬 best for raw throughput, but lifetime + cleanup complexity is real. Defer until benchmarks demand it. |
| DotNetty `PooledByteBufferAllocator` (write side) | ✅ also viable — gives us `IByteBuffer.Release()` semantics natively, but mixes the pool with kernel-bound buffers. |

**Recommendation:** start with an instanced
`ArrayPool<byte>.Create(maxArrayLength = Settings.MaxFrameSize,
maxArraysPerBucket = Settings.ClientSocketWorkerPoolSize * 8)` owned
by the `DotNettyTransport`. Same instance can serve the read side
(see the read-side analysis doc).

### 4.3 What about a `PooledByteString`?

We *could* implement `IMemoryOwner<byte>` + a `ByteString` adapter via
`ByteString.AttachBytes` followed by a "release on PDU sent" hook —
but `ByteString` has **no disposal API**, so the pool would never get
the buffer back. Therefore the right shape is:

- Replace the `ByteString pdu` flowing through
  `AssociationHandle.Write` with a typed `IPooledPdu` (or
  `IMemoryOwner<byte>`) that the *transport* understands and disposes
  exactly once after `WriteAndFlushAsync` completes (DotNetty handles
  this via `IByteBuffer` reference counting — perfect fit).
- Keep `ByteString` only for the `SerializedMessage.Message` field,
  where its immutability and shareability are valuable.

### 4.4 Lifetime / disposal

Unlike the read side, the write side has a **clear single owner**:
the buffer is rented inside `TcpAssociationHandle.Write`, handed to
`channel.WriteAndFlushAsync`, and DotNetty guarantees `Release()` is
called exactly once after the kernel has consumed it (or on error).
This makes pooling on the write side **substantially easier** than on
the read side. ✨

---

## 5. Allocation Hotspot Cheat-Sheet

| ID  | Location | Call | Approx size | Eliminable? | Notes |
|-----|----------|------|-------------|------------|-------|
| W0a | `EndpointManager.Send` | envelope ctor | ~64 B | NO | actor-model primitive. |
| W0b | `MessageSerializer` | `new SerializedMessage(...)` | ~32 B | NO | proto POCO. |
| W0c | `AkkaPduCodec.ConstructMessage` | `new AckAndEnvelopeContainer/RemoteEnvelope/ActorRefData` | ~64–128 B | partial via per-recipient cache | repeated POCOs per send. |
| W1  | `MessageSerializer.Serialize` | `serializer.ToBinary(message)` | full payload | NO (serializer owns) | external API contract. |
| W2  | `MessageSerializer.Serialize` | `ByteString.CopyFrom(byte[])` | full payload | **YES** — switch to `ByteString.AttachBytes` | one-line patch. |
| W3  | `MessageSerializer.Serialize` | `ByteString.CopyFromUtf8(manifest)` | manifest length | partial (cache common manifests) | per-message UTF-8 encode. |
| W4  | `AkkaPduCodec.ConstructMessage` | `ackAndEnvelope.ToByteString()` | full PDU | YES, with codec rewrite to `IBufferWriter<byte>` | full proto serialise of envelope+payload. |
| W5  | `AkkaPduCodec.ConstructPayload` | `new AkkaProtocolMessage { Payload = payload }.ToByteString()` | full PDU + small header | YES, hand-rolled tag+varint+memcpy or codec rewrite | redundant outer wrap. |
| W6  | `TcpAssociationHandle.Write` | `payload.ToByteArray()` | full PDU | **YES** — `MemoryMarshal.TryGetArray(payload.Memory)` + `Unpooled.WrappedBuffer` | ~6-line patch. |
| W7  | `LengthFieldPrepender.Encode` | `ctx.alloc().buffer(4)` | 4 B | partial (pool channel allocator) | per-write tiny alloc. |
| W7b | `HeliosBackwardsCompatabilityLengthFramePrepender.Encode` | `lengthFrame.WriteBytes(message)` | full PDU | NO (Helios wire format demands contiguity) | only when `BackwardsCompatibilityModeEnabled = true`. |
| W8  | `FlushConsolidationHandler.ScheduleFlush` | `new CancellationTokenSource()` | ~80 B | partial — only when batching | one per scheduled flush, **not per write**. |

---

## 6. Suggested Implementation Order (smallest blast radius first)

1. **`MessageSerializer.Serialize`: `CopyFrom` → `AttachBytes`.**
   One line. Kills `COPY #W2`. Low risk because every built-in Akka
   serializer returns a freshly-allocated array. ✨
2. **`TcpAssociationHandle.Write`: avoid `ToByteArray()` via
   `MemoryMarshal.TryGetArray(payload.Memory)`.** 6 lines. Kills
   `COPY #W6`. Zero behavioural change — `Unpooled.WrappedBuffer`
   already handled both sliced and full arrays. ✨
3. **Switch the channel allocator to
   `PooledByteBufferAllocator`** (instance owned by `DotNettyTransport`,
   shared with read-side fix). Amortises `ALLOC #W7` (and reduces
   short-lived `ByteBuffer` pressure for the SSL/Flush handlers too). ✨
4. **Per-`EndpointWriter` `RemoteEnvelope` cache.** Cache
   `Recipient` → `ActorRefData` (or even pre-serialised partial
   protobuf bytes for the recipient field). Targets `ALLOC #W0c`
   and the path-string allocations. ✨
5. **Codec rewrite to write into a pooled `IBufferWriter<byte>` /
   `IMemoryOwner<byte>` directly.** This is the Big One — collapses
   `ALLOC #W4` and `ALLOC #W5` into one rented buffer. Best done
   together with the read-side `IInboundPayload` work so that the
   full transport layer speaks `Memory<byte>` end-to-end. ✨
6. **Manifest `ByteString` cache** keyed on `(SerializerId, Type)` →
   pre-encoded `ByteString`. Eliminates `COPY #W3` in steady state. ✨
7. **(Cluster-only)** SerializeWithTransport-aware caching of common
   gossip messages — orthogonal to the transport but stacks well with
   #4. ✨

Each of steps 1–3 is a same-day patch with measurable benefit on
the existing `RemotePingPong` benchmark and zero public API surface
change. Steps 4–7 escalate in scope and are well-suited to an OpenSpec
proposal. uwu let's optimise this baby~ 🌸

---

# 7. `TcpPipeTransport` (Pipelines) — Write Pipeline 🌟

> **Targets analysed:**
> - `src/core/Akka.Remote/Transport/Pipelines/TcpPipeTransport.cs`
>   (`Associate`, accept loop)
> - `src/core/Akka.Remote/Transport/Pipelines/PipeAssociationHandle.cs`
>   (`Write` → `_connection.TryEnqueueWrite`)
> - `src/core/Akka.Remote/Transport/Pipelines/PipeConnection.cs`
>   (`TryEnqueueWrite`, `WriteLoopAsync` — the **double-buffer ping-pong** 🏓)
> - `src/core/Akka.Remote/Transport/Pipelines/AkkaPduMessagePackCodec.cs`
>   (when `envelope = messagepack`)
>
> **Verdict (TL;DR):** the pipelines transport's write path is
> **architecturally cleaner** than DotNetty's — there is no
> `LengthFieldPrepender`, no Helios compatibility shim, no
> `Unpooled.WrappedBuffer` round-trip. But it currently performs **one
> additional payload copy at enqueue time** (`payload.ToByteArray()` in
> `TryEnqueueWrite`) and the write loop's `ArrayBufferWriter<byte>`
> instances are **never returned to a pool**. The MessagePack codec
> adds **two more avoidable payload copies** in `ConstructMessage`.
> Net: today the pipe write side is *roughly comparable* to DotNetty;
> with the easy fixes below it becomes **strictly cheaper**. uwu ✨

## 7.1 End-to-end Pipeline Diagram (Pipe variant)

```
                    user code (any thread)
                           │ Tell(msg)
                           ▼
              ┌───────────────────────────────┐
              │ EndpointWriter (same as §1)   │
              │   SerializeMessage(msg)       │ ◀── ALLOC #W1, COPY #W2, COPY #W3
              │   ConstructMessage(...)       │ ◀── ALLOC #W4 (proto OR MessagePack)
              │   ConstructPayload(pdu)       │ ◀── ALLOC #W5
              │   _handle.Write(byteString)   │
              └──────────────┬────────────────┘
                             │ PipeAssociationHandle.Write
                             ▼
              ┌───────────────────────────────┐
              │ PipeConnection.TryEnqueueWrite│
              │   payload.ToByteArray()       │ ◀── COPY #PW6 (full PDU; pre-channel copy)
              │   _writeChannel.TryWrite(arr) │
              └──────────────┬────────────────┘
                             │ Channel<byte[]> (bounded, DropWrite)
                             ▼
              ┌───────────────────────────────┐
              │ WriteLoopAsync — ping-pong 🏓 │
              │  ┌──────────┐    ┌──────────┐ │
              │  │ buffer A │ ⇄  │ buffer B │ │  ArrayBufferWriter<byte>(8192)  ALLOC #PW7
              │  └──────────┘    └──────────┘ │  initial cap; grows by doubling
              │   while(TryRead): drain all   │
              │     WriteInt32LE(len) → span  │  ZERO-ALLOC framing ✨
              │     payload.CopyTo(span)      │  COPY #PW8 (in-process memcpy)
              │   await prev WriteAsync       │  ALLOC #PW9 (.AsTask boxes ValueTask)
              │   stream.WriteAsync(active)   │
              │   activeIdx ^= 1              │  buffer swap
              └──────────────┬────────────────┘
                             │
                             ▼
              ┌───────────────────────────────┐
              │ NetworkStream (or SslStream)  │
              │   WriteAsync(memory, ct)      │  +1 internal copy if SSL enabled
              └──────────────┬────────────────┘
                             ▼
                       socket send()
```

So a single outbound user message currently performs, in the
**non-TLS, protobuf-codec** path:

- The **same** upstream copies as DotNetty (W1, W2, W3, W4, W5 — all
  in `MessageSerializer` and `AkkaPduCodec`, totally transport-agnostic).
- **`COPY #PW6` at channel enqueue** (`payload.ToByteArray()`).
- **`COPY #PW8`** when the payload is folded into the active
  `ArrayBufferWriter<byte>` for batched send.
- **`ALLOC #PW9`** — `ValueTask.AsTask()` boxing once per drain (not
  per message — this is amortised across the batch).

Note that `COPY #W6` from the DotNetty side (`payload.ToByteArray()`
inside `TcpAssociationHandle.Write`) is replaced by `COPY #PW6` here
— same cost, different location. It is the *equivalent* allocation, not
an additional one.

## 7.2 Stage-by-stage allocation deltas vs. DotNetty

| ID  | DotNetty equivalent | Pipe variant | Δ |
|-----|---------------------|--------------|----|
| W1–W5 | upstream `MessageSerializer` + `AkkaPduCodec` | identical (same upper layer) | ☑️ unchanged. |
| **PW6** | W6 (`payload.ToByteArray()` in `TcpAssociationHandle.Write`) | `payload.ToByteArray()` in `PipeConnection.TryEnqueueWrite` | ➡️ same cost, different layer. **Same `MemoryMarshal.TryGetArray` fix from §2 step 2 applies here.** See §7.3. |
| **PW7** | n/a in DotNetty (uses `Unpooled.WrappedBuffer`) | two `ArrayBufferWriter<byte>(8192)` instances retained for the connection lifetime | ✅ amortised — the buffers are reused indefinitely via the ping-pong swap. **But:** if a batch ever exceeds 8 KiB, the underlying `byte[]` is doubled and the old one becomes garbage. See §7.3. |
| **PW8** | n/a in DotNetty (no batching at the prepender level — `LengthFieldPrepender` writes a separate 4-byte buffer) | `payload.AsSpan().CopyTo(active.GetSpan(payload.Length))` once per message | ⚠️ **one extra in-process memcpy per write** vs. DotNetty's zero-copy `WrappedBuffer`. This is the cost of write coalescing — see §7.3 for whether it's worth it (spoiler: **yes**). |
| **PW9** | n/a (DotNetty handles `Task` plumbing internally) | `_stream.WriteAsync(active.WrittenMemory, ct).AsTask()` — **boxes** the `ValueTask` into a `Task` once per drain | ⚠️ **one Task allocation per batch**, not per message. Amortises to near-zero on busy connections, but on low-rate connections this is observable. See §7.4. |
| **PW7-fdc** | DotNetty has `LengthFieldPrepender` + 4-byte alloc + `FlushConsolidationHandler` (no per-write alloc) | none of these — replaced by the inline `BinaryPrimitives.WriteInt32LittleEndian(active.GetSpan(4), payload.Length)` | ✅ **strictly better** — zero alloc framing; no flush-consolidation handler at all because the channel-drain loop is itself the consolidator. |

### What the pipelines transport gives you for free

1. **Inline framing.** No `LengthFieldPrepender`, no per-write 4-byte
   buffer allocation, no `IByteBuffer` envelope. The 4-byte LE length
   prefix is written directly into the active batch buffer with
   `BinaryPrimitives.WriteInt32LittleEndian`. Zero-alloc. ✨
2. **Built-in write coalescing.** The `Channel<byte[]>` drain loop
   calls `_writeChannel.Reader.TryRead` in a tight loop and folds
   *all* immediately-available payloads into a single
   `Stream.WriteAsync` call. This replaces both `BatchWriter` and
   `FlushConsolidationHandler` from the DotNetty path with one
   simpler primitive.
3. **Double-buffer ping-pong (`activeIdx ^= 1`).** While the kernel
   is `send()`ing buffer A, the CPU is filling buffer B with the next
   batch — full overlap of user-space batching and kernel I/O.
4. **No DotNetty pipeline overhead** — no per-write `ChannelHandlerContext`
   fire propagation, no `IByteBuffer.Retain/Release` bookkeeping,
   no `WriteAndFlushAsync` future chaining.

### What it does **not** give you

1. **`payload.ToByteArray()` is still copied at enqueue time**
   (`COPY #PW6`). Same `MemoryMarshal.TryGetArray` fix as the DotNetty
   side applies — but the channel is `Channel<byte[]>`, so the fix
   needs to widen the channel item type (see §7.3).
2. **The `ArrayBufferWriter<byte>` slabs are never returned to a
   pool.** They live forever with the connection (good for steady
   state) but are individually-owned `byte[]` arrays, not pooled
   slabs — so a sudden burst that doubles them past 8 KiB leaves the
   old array as Gen0 garbage. See §7.3.
3. **All upstream codec/serializer allocations (W1–W5) are unchanged.**
   Same as the read side, the wins from §1–§6 carry over identically.

## 7.3 Hotspot deep-dive — `PipeConnection`

### `TryEnqueueWrite` (`COPY #PW6`)

```csharp
// PipeConnection.cs
public bool TryEnqueueWrite(ByteString payload)
{
    if (Volatile.Read(ref _closed) == 1)
        return false;

    // ToByteArray() copies — Phase 2 can eliminate this via IBufferWriter writer.
    return _writeChannel.Writer.TryWrite(payload.ToByteArray());  // COPY #PW6
}
```

- Same allocation pattern as DotNetty's `COPY #W6`: a freshly-allocated
  `byte[]` of `payload.Length` bytes, populated by `Buffer.BlockCopy`.
- **Cannot use the same `MemoryMarshal.TryGetArray(payload.Memory)`
  trick directly** because the `Channel<byte[]>` item type is
  `byte[]`, and the underlying `ByteString` array may extend beyond
  the slice we want (in practice it doesn't, because every
  `ByteString` we construct in the codec is exactly the right size,
  but the channel can't express the "offset + length" subview).

  **Easy fix:** widen the channel to carry a small struct wrapper:

  ```csharp
  // Carries either an array+offset+length view, or a pooled IMemoryOwner<byte>
  internal readonly struct PendingWrite
  {
      public readonly byte[] Array;
      public readonly int Offset;
      public readonly int Count;
      public readonly IMemoryOwner<byte>? Owner; // null when the array is shared (e.g., ByteString-backed)
      // ...
  }

  public bool TryEnqueueWrite(ByteString payload)
  {
      if (Volatile.Read(ref _closed) == 1) return false;
      if (MemoryMarshal.TryGetArray<byte>(payload.Memory, out var seg))
          return _writeChannel.Writer.TryWrite(new PendingWrite(seg.Array!, seg.Offset, seg.Count, owner: null));
      // Fallback for non-array-backed ByteStrings (extremely rare in our pipeline)
      var copy = payload.ToByteArray();
      return _writeChannel.Writer.TryWrite(new PendingWrite(copy, 0, copy.Length, owner: null));
  }
  ```

  Then `WriteLoopAsync` reads `PendingWrite` and copies `Array`/`Offset`/`Count`
  into the active `ArrayBufferWriter<byte>`. **Eliminates `COPY #PW6`
  entirely for every message produced by our codec.** ✨

### `WriteLoopAsync` ping-pong buffers (`ALLOC #PW7`)

```csharp
var buffers = new ArrayBufferWriter<byte>[]
{
    new(initialCapacity: 8192),
    new(initialCapacity: 8192),
};
var activeIdx   = 0;
Task? inflightWrite = null;
```

- Two `ArrayBufferWriter<byte>` slots, each starting at 8 KiB. Each
  slot's underlying `byte[]` doubles on overflow (`8 → 16 → 32 → 64
  → … KiB`) and **the old array is GC'd**. After a few large bursts
  the slots stabilise at the high-water mark and become essentially
  free.
- The ping-pong swap (`activeIdx ^= 1`) only happens **after** a
  non-empty drain — so the slot we are about to clear is guaranteed
  to be the one whose `Task inflightWrite` we just awaited. Lifetime
  is correct. ✨
- The `Channel<byte[]>` is bounded with `DropWrite` semantics, which
  matches the `AssociationHandle.Write` contract: full channel →
  `TryWrite` returns `false` → `Write` returns `false`. Good.

> 💡 **Consideration — should the slabs be pooled?**
>
> For most workloads: **no**. The two-slab steady-state is the
> cheapest possible thing — once warmed up, there are zero
> allocations per write batch (besides the one `Task` from
> `.AsTask()`).
>
> For workloads with **highly variable batch sizes** (e.g. occasional
> mega-batches followed by long quiet periods), the slabs settle at
> the high-water mark and "trap" memory. A pool would let us return
> the big slab and rent a smaller one. The trade-off is that pooling
> reintroduces per-batch alloc/return overhead.
>
> **Recommendation:** keep as-is for Phase 1; expose an optional
> "slab high-water reset" timer for Phase 2 if telemetry shows
> trapped memory.

### Inline framing copy (`COPY #PW8`)

```csharp
BinaryPrimitives.WriteInt32LittleEndian(active.GetSpan(FrameHeaderSize), payload.Length);
active.Advance(FrameHeaderSize);

payload.AsSpan().CopyTo(active.GetSpan(payload.Length));   // COPY #PW8
active.Advance(payload.Length);
```

- One in-process memcpy of the payload into the batch buffer per
  write. This is the **cost of write coalescing** — DotNetty uses
  scatter-gather (`WriteAndFlushAsync` queues `IByteBuffer`s
  individually and the OS does the gather), while pipelines do the
  gather in user space.
- Trade-off analysis:
  - **Pro (one big `WriteAsync`):** one syscall per batch instead of
    one per message; one TLS record per batch instead of one per
    message (huge win for SSL throughput); much better OS-side write
    coalescing into a single `send()`.
  - **Con (one extra memcpy per message):** memcpy of small messages
    is ~5–10 ns/KiB on modern hardware → negligible vs. the syscall
    + TLS-record amortisation savings.
- **Verdict:** keep `COPY #PW8`. It's a feature, not a bug. ✨

### `.AsTask()` boxing (`ALLOC #PW9`)

```csharp
inflightWrite = _stream.WriteAsync(active.WrittenMemory, ct).AsTask();
```

- `_stream.WriteAsync(ReadOnlyMemory<byte>, CancellationToken)`
  returns `ValueTask`. Storing it across loop iterations requires
  `.AsTask()`, which **always allocates** a `Task` (one per
  non-empty drain).
- **Alternative:** pre-allocate a `ValueTaskAwaiter` on the stack
  and use `await` directly — but then we lose the ping-pong overlap
  because we'd be awaiting before launching the next batch fill.
- **Better alternative:** use `IValueTaskSource` and a custom
  re-usable awaitable to keep the overlap without allocating a
  `Task`. This is the same pattern Kestrel uses internally.
- **Verdict:** `ALLOC #PW9` is per-batch (not per-message), so on
  busy connections it amortises to ~one alloc per N messages where
  N can be hundreds. Defer the `IValueTaskSource` rewrite until
  benchmarks demand it. ☑️

## 7.4 MessagePack codec (`envelope = messagepack`) write side

### `ConstructMessage` and `ConstructPureAck` — extra full-payload copy

```csharp
// AkkaPduMessagePackCodec.cs
private static MpPayload BuildMpPayload(SerializedMessage msg) =>
    new()
    {
        Message      = msg.Message.IsEmpty      ? null : msg.Message.ToByteArray(),    // COPY #PW2-mp (full payload)
        SerializerId = msg.SerializerId,
        Manifest     = msg.MessageManifest.IsEmpty ? null : msg.MessageManifest.ToByteArray()
    };

public override ByteString ConstructMessage(...)
{
    var env = new MpRemoteEnvelope { ..., Message = BuildMpPayload(serializedMessage), ... };
    var container = new MpAckAndEnvelope { Envelope = env, Ack = ... };

    return ByteString.CopyFrom(MP.MessagePackSerializer.Serialize(container));         // COPY #PW4b-mp
}
```

- `BuildMpPayload` does `msg.Message.ToByteArray()` — a full copy of
  the user payload out of the existing `ByteString`, just so
  MessagePack can hold it as `byte[]?`. **Avoidable** — if the
  MessagePack schema field type is widened from `byte[]?` to
  `ReadOnlyMemory<byte>?`, MessagePack-CSharp's built-in formatter
  can write the bytes directly without a wrapper copy.
- `ByteString.CopyFrom(MP.MessagePackSerializer.Serialize(container))` —
  `Serialize` returns a freshly-allocated `byte[]` that is otherwise
  unreferenced. Use **`ByteString.AttachBytes(MP.MessagePackSerializer.Serialize(container))`**
  to skip this copy. **One-line patch.** ✨

### `ConstructPayload` and `ConstructAssociate`

```csharp
public override ByteString ConstructPayload(ByteString payload)
{
    var frame = new MpProtocolFrame
    {
        Tag     = ProtocolTag.Payload,
        Payload = payload.ToByteArray()                                               // COPY #PW5a-mp
    };
    return ByteString.CopyFrom(SerializeFrame(frame));                                // COPY #PW5b-mp
}
```

- Same two opportunities: switch `Payload` field to
  `ReadOnlyMemory<byte>?` (or just keep `byte[]?` and
  `MemoryMarshal.TryGetArray` to avoid the copy), and use
  `ByteString.AttachBytes` for the result. ✨

### `ConstructHeartbeat` / `ConstructDisassociate`

```csharp
public override ByteString ConstructHeartbeat() =>
    ByteString.CopyFrom(s_heartbeatBytes);

public override ByteString ConstructDisassociate(DisassociateInfo reason)
{
    var bytes = reason switch { ... };
    return ByteString.CopyFrom(bytes);                                                // COPY per heartbeat ;_;
}
```

- The static `s_heartbeatBytes` array is cached at type init — but
  every call to `ConstructHeartbeat` **copies it** into a fresh
  `ByteString`! Heartbeats fire every few seconds per association —
  on a 1000-node cluster that's **thousands of avoidable copies per
  second**.
- **Fix:** cache the *`ByteString`*, not the bytes:

  ```csharp
  private static readonly ByteString s_heartbeatPdu =
      ByteString.AttachBytes(MP.MessagePackSerializer.Serialize(
          new MpProtocolFrame { Tag = ProtocolTag.Heartbeat }));

  public override ByteString ConstructHeartbeat() => s_heartbeatPdu;
  ```

  Same trick for the three disassociate variants. **Eliminates the
  copy entirely** for every control PDU. ✨

> **CopilotNotes:** the protobuf codec already does this correctly —
> `HeartbeatPdu` is a static `ByteString`. The MessagePack codec
> kept the static `byte[]` and re-wraps on each call. Easy alignment.

## 7.5 Suggested implementation order (Pipe-specific)

Stack these on top of the cross-cutting steps from §6:

1. **Cache `ByteString`s for heartbeat/disassociate** in the
   MessagePack codec (mirror the protobuf codec's `HeartbeatPdu`
   pattern). Trivial patch, big win for control-message-heavy
   workloads. ✨
2. **`ByteString.AttachBytes` for `MessagePackSerializer.Serialize`
   results** in `ConstructMessage`, `ConstructPureAck`,
   `ConstructPayload`, `ConstructAssociate`. ~6-line patch total. ✨
3. **Switch `MpRemoteEnvelope`/`MpProtocolFrame` payload fields to
   `ReadOnlyMemory<byte>?`** so `BuildMpPayload` can avoid the
   `ToByteArray()` call. Requires the MessagePack formatters to be
   regenerated, but the wire format is unchanged. ✨
4. **Widen `Channel<byte[]>` to `Channel<PendingWrite>`** in
   `PipeConnection`, where `PendingWrite` carries either an
   `ArraySegment<byte>` aliasing the source `ByteString` or a
   pooled `IMemoryOwner<byte>`. Eliminates `COPY #PW6` for every
   message produced by our codec. ✨
5. **Surface `StreamPipeReaderOptions` knobs** on
   `PipeTransportSettings` so operators can plug in an instanced
   `MemoryPool<byte>` (shared between read and write side via the
   `IInboundPayload` work from the read-side §7.5). ✨
6. **Phase 2:** consider an `IValueTaskSource`-backed reusable
   awaitable for `WriteLoopAsync` to remove `ALLOC #PW9`. Only
   tackle this once benchmarks show `Task` allocations dominating
   the write side (they currently won't on busy connections). ✨

> **Closing CopilotNotes:** the pipe write side has the **shorter
> critical path** of the two transports — fewer pipeline handlers,
> no `IByteBuffer` refcount domain, inline framing. Its three
> remaining hotspots (PW6 codec-side copy, PW7 buffer growth,
> PW9 Task boxing) are all amortised or trivially fixable. Once the
> MessagePack codec is cleaned up (§7.4), this is the **fastest
> Akka.NET write path we have shipped**. uwu let's gooo~ 🌸
