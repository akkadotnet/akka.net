## Context

Artery is more than a wire format. It is association lifecycle, UID-scoped handshake state, control/ordinary/large streams, reliable system-message delivery, bounded queues, actor-ref/manifest compression, and inbound/outbound lanes.

Akka.NET 1.6 adds an Artery-style TCP remoting stack **beside** classic remoting. Classic remoting (`EndpointWriter`/`EndpointReader`, `AkkaProtocolTransport`, `AkkaPduCodec`, protobuf `Payload`, `AssociationHandle.Write(ByteString)`) stays as the compatibility path. Artery is the new high-throughput path on the validated `SerializerV2` payload contract.

> **This document records the Artery architecture verified against Apache Pekko `main` (Apache 2.0) during design, the .NET-idiomatic mapping of each mechanism, and the invariants that must be preserved. Claims marked "(verified)" were read from Pekko source, not recalled.**

## Goals / Non-Goals

**Goals:**
- Add Artery-style remoting beside classic remoting (its own `RemoteTransport`, not classic Endpoints).
- Use `SerializerV2` for payloads.
- Start with plaintext TCP over `Akka.Streams.IO.Tcp` (see Decision 2).
- Implement handshake, UID tracking, association state, ordinary stream, control stream, and reliable system-message delivery.
- Preserve classic remoting independently.
- Build a protocol that can later host QUIC.

**Non-Goals:**
- Classic wire compatibility for Artery.
- Removing classic remoting.
- QUIC in 1.6.
- Compression before basic association + system-message correctness.
- TCP-level chunking of large messages (large messages get an isolated stream, not fragmentation — see Invariants).

## Verified Artery architecture (Pekko `main`)

**Three streams**, multiplexed by a 1-byte stream ID in the TCP connection header:

- **control** — handshake, heartbeats, system-message ACK/NACK, quarantine notifications. **Pierces quarantine**: `sendControl()` still delivers to a quarantined association (verified); the control channel must stay open so both systems can reconcile association state.
- **ordinary** — user messages, partitioned across **N inbound + outbound lanes** by recipient hash (per-recipient ordering preserved). **Blocked under quarantine** except `ActorSelectionMessage` / `ClearSystemMessageDelivery` (verified).
- **large** — messages to `large-message-destinations`, sent as single frames up to `maximum-large-frame-size`. Purpose is **head-of-line-blocking isolation, not chunking** — Artery TCP does not fragment (that is an Aeron/UDP behavior); oversized payloads are chunked at the application layer (Akka.Delivery / stream refs).

**Outbound pipeline** (N actor threads → one socket):
```
Association.send() → pooled OutboundEnvelope → selectQueue()
  (priority→control · large-dest→large · else ordinary[uid % lanes])
→ bounded queue  (control/large = LinkedBlockingQueue; ordinary = ManyToOneConcurrentArrayQueue, lock-free MPSC, one per lane)
→ SendQueue (stream source over an EXTERNALLY-INJECTED queue)
→ OutboundHandshake
→ [control only: SystemMessageDelivery — seq / ACK / resend]
→ Encoder (envelope header + ref/manifest COMPRESSION + SerializerV2 payload → pooled EnvelopeBuffer)
→ TcpFraming (AKKA magic + 1-byte streamId header, 4-byte LE length)
→ Tcp().OutgoingConnection → socket
```

**Inbound pipeline** (socket → recipient actors):
```
Tcp().Bind → TcpFraming (parse header + frames → EnvelopeBuffer) → partition by streamId
→ Decoder (header + ref/manifest DECOMPRESSION → InboundEnvelope metadata)
→ [ordinary only: partition to N lanes by recipient hash — ordering preserved]
→ Deserializer (payload; the expensive step lanes parallelize)
→ InboundHandshake → InboundQuarantineCheck
→ [control only: SystemMessageAcker — dedup + ACK]
→ messageDispatcherSink → MessageDispatcher.dispatch → recipient actor
```

**Amortization — this is NOT batching (verified).** One message = one envelope = one stream element = one materializer trip = one TCP frame. `SendQueue` pushes exactly one element per pull; there is no outbound aggregation. Throughput comes from **lanes (cross-core parallelism) + pooled buffers (zero steady-state alloc) + lock-free MPSC queues + ref/manifest compression + a coarse graph** — not from coalescing messages per frame.

**`SendQueue` lifecycle (verified).** The queue is **externally owned and injected** via `inject(q)` on the materialized `QueueValue`; `offer` throws before injection; the queue **survives stream restart** (reconnect re-attaches a new consumer to the same queue, so buffered messages persist). This is why Artery does not use `Source.queue`, which owns its buffer, bakes in one `OverflowStrategy`, and dies with the materialization.

## Decisions

### 1. New `RemoteTransport`
`ArteryRemoting : RemoteTransport`, selected by config; not forced through `AkkaProtocolTransport`. Artery needs association state, stream separation, reliable system-message delivery, bounded queues, and compression-table lifecycle that do not fit `AssociationHandle.Write(ByteString)`.

### 2. Transport substrate = `Akka.Streams.IO.Tcp` — REVISED (was "direct System.IO.Pipelines")
Canonical Artery TCP is built on Streams TCP — **verified** against Pekko `ArteryTcpTransport`: inbound `Tcp(system).bind(...)`, outbound `Tcp(system).outgoingConnection(...)`, framing `.via(new TcpFraming(...))`, and the inbound/outbound pipelines are stream graphs. Akka.NET Artery TCP will do the same: **`Akka.Streams.IO.Tcp` (`Tcp().Bind` / `Tcp().OutgoingConnection`) as the socket + framing substrate**, with Artery owning framing/queueing/backpressure via the `TcpFraming` stage + the injected bounded queue + lanes.

**This reverses the earlier "direct `System.IO.Pipelines`" decision.** Rationale for the reversal: (a) it matches canonical Artery; (b) Artery's throughput comes from **lanes + pooling + compression**, so the per-message materializer cost is *recovered by parallelism*, not avoided; (c) it reuses the modernized Akka.IO/Streams TCP substrate rather than maintaining a parallel raw-Pipelines stack.

**Validation gate (measure-first, before building the full stack).** The one empirical unknown: does .NET's per-message materializer cost × (lanes/cores) clear the **680K msgs/sec** DotNetty baseline? A minimal prototype — `Akka.Streams.IO.Tcp` + N lanes + pooled buffers, run naked (no scripts) — must confirm it. **Fallback, only if measurement fails:** targeted materializer/stage surgery, or a `System.IO.Pipelines` fast-path for the *substrate* while keeping the Artery protocol/stages. We do not pre-emptively deviate.

**Fusion findings (verified against `Akka.Streams.Implementation.Fusing` / `ActorGraphInterpreter`).** `Fusing.Aggressive` fuses all stages into ONE interpreter island (one actor, in-process push/pull, no mailbox) unless separated by `.Async()` or a different dispatcher — `Partition`/`Balance`/`Merge` do **not** split. TCP does not add a fusion boundary; inbound `Tcp.Received` carries a whole `ByteString` (many framed messages per socket read), so the socket→stream hop is **amortized per TCP read, not per message** (~0/msg). Therefore:

- **Irreducible streams tax ≈ ONE boundary crossing per inbound message** — the `partition → lane` fan-out (1 mailbox `Tell` + 1 boxing alloc of the `OnNext` struct + a short chased push/pull through the fused chain). That single hop *is* the price of N-core parallel deserialize; it is 0 if you keep one island, but then deserialize is single-core. Outbound is **0–1/msg**: ~0 with a custom drain-many queue source + coalesced `Tcp.Write`, but **stock `Source.Queue` costs 1 hop/msg** (avoid it — see Decision 9).
- **The real throughput gate is NOT the lanes — it is the serial, single-actor islands** that cannot be parallelized per connection: the inbound decode/partition island, the outbound encode island, and the Akka.IO connection actor. Lanes scale deserialize linearly across cores until one of those serial islands saturates. If the decode/partition island's per-message work stays sub-microsecond (framing + a cheap recipient hash), its ceiling is multi-million/sec — comfortably above 680K — and lanes recover the ~+30% interpreter tax by spending cores. **The disqualifier is a serial-island ceiling at/near 680K**, or GC pressure from per-boundary boxing at rate. Measured, not assumed. If 680K must flow over a *single* connection, the single decode island is the number to prove.

**Graph-design rules (minimize per-element cost):** (1) keep framing+encode (outbound) and framing+decode+partition (inbound) in single fused islands — no interior `.Async()`; (2) feed outbound from a **custom drain-many MPSC source with coalesced wakeup**, not `Source.Queue`; (3) put the *only* inbound `.Async()` at the lane fan-out; (4) keep the serial decode/partition island light (recipient-hash only; heavy deserialize in the lanes); (5) coalesce `Tcp.Write`s; (6) pool framing buffers; (7) keep OTEL/tracing listeners off the hot path (the interpreter's per-push `Activity` check is free only when no listener is attached).

### 3. Framing (verified)
`TcpFraming`: connection header = `AKKA` magic + 1-byte stream ID; frame header = 4-byte little-endian length. Simple, proven, cleanly separates control/ordinary/large.

### 4. Artery envelope separate from `SerializerV2`
`SerializerV2` serializes payloads (`IBufferWriter<byte>` / `ReadOnlySequence<byte>`). The Artery envelope owns remoting metadata: version, flags, origin UID, serializer id, manifest, sender, recipient, control/system markers, payload boundaries. They evolve separately. **The envelope header *encoding* is serialization-shape-dependent and will differ from Pekko** (V2 non-CLR manifests, buffer-first) — the *semantics* transfer; the byte layout does not. Do not transliterate the header.

**V2 dependency status (verified against `dev`).** `SerializerV2` + the MessagePack sourcegen are already committed (PRs #8222, #8230) — the OpenSpec checkboxes lag the code. The four API-shape decisions the envelope consumes are **settled and exercised end-to-end**: buffer shape (`IBufferWriter<byte>` write / `ReadOnlySequence<byte>` read), bytes-written result, native non-CLR manifests, and exact-or-unknown `SizeHint`. The payload-write hook already exists: `internal Serialization.Serialize(object, IBufferWriter<byte>) → {SerializerId, Manifest, BytesWritten}`; `Deserialize(ReadOnlySequence<byte>, serializerId, manifest)` structurally enforces decode-metadata-before-payload; a working envelope POC lives in `Akka.Serialization.V2/MessagePackSerializer.cs`. **Envelope *design* can start now.** Three items must close before *locking the wire byte-layout* (not before designing): (a) explicit **sync-vs-async** sign-off (currently sync; async deferred — serializer-v2 task 1.7); (b) messagepack task **6.8** oversized-payload determinism (frame-length accounting); (c) messagepack task **8.7** "record V2 API changes required before Artery" (formal hand-off gate). The classic-remoting / persistence V2 bridge tasks do **not** block Artery (compat, covered by `SerializerV2 : Serializer` inheritance).

### 5. Control stream before lanes
Correctness before throughput. Control stream + reliable system-message delivery land before ordinary lanes; system messages must not be starved by user traffic.

### 6. UID-scoped state
Handshake, quarantine, compression tables, and reliable system-message state are keyed by remote address + UID/incarnation. Stale state after a remote `ActorSystem` restart corrupts actor refs, manifests, and system-message delivery.

### 7. Bounded queues + overflow policy (verified asymmetry)
Outbound queues are bounded. **Overflow is asymmetric by lane:** ordinary overflow → drop to dead-letters (deterministic, no quarantine); control/system overflow → **quarantine**. In .NET: a bounded `Channel` per lane, `TryWrite` → false → apply the policy — never `WriteAsync`-await, which would block a producing actor thread on a slow remote.

### 8. Faithful semantics, idiomatic primitives (standing rule for the whole port)
> **Implement Artery's protocol and invariants faithfully; express every mechanism with the best-fit .NET primitive; validate each substitution against the invariant it replaces, baseline-first.**

Keep faithful: framing/envelope semantics, association + UID + handshake + quarantine, reliable system-message ACK/NACK/resend, lane ordering, bounded-queue + overflow policy, compression-table lifecycle. Re-express idiomatically: the plumbing (Decision 9). Each substitution is a **choice to validate, not a free win** — proven by the 1.6 transport experiments where "idiomatic" swaps (consumer-driven `PipeReader`, lock-free SPSC hand-off) *regressed* against the thing they replaced.

### 9. .NET primitive mapping

| Pekko/JVM mechanism | Purpose | Idiomatic .NET |
|---|---|---|
| `EnvelopeBufferPool` (direct `ByteBuffer`s) | zero-alloc reusable wire buffers | `MemoryPool<byte>` / `ArrayPool<byte>` / the V2 `IBufferWriter`; POH-pinned array only if pinning churn shows in measurement |
| `ManyToOneConcurrentArrayQueue` + `SendQueue` | bounded lock-free MPSC buffer + pull-on-demand stream source w/ cross-thread wakeup | **one** bounded `Channel` (`SingleReader=true`) owned by the Association + a **custom drain-many `GraphStage` source** over its `ChannelReader` with a coalesced wakeup. **NOT `Source.Queue`** (verified: 1 mailbox hop per offer). Survives restart because the Channel outlives the consumer |
| direct `ByteBuffer` (off-heap, to skip JVM bounce-copy) | socket-ready buffer | `Memory<byte>` over a pooled managed array (no bounce-copy in .NET); POH/native only if pinning is measured to hurt |
| `ImmutableLongMap` / `LruBoundedCache` (compression tables) | ref/manifest ↔ small-int tables | .NET immutable/LRU equivalents |
| async callbacks / `Future` per offer | hot-path async | `ValueTask` / `IValueTaskSource`; sync `TryWrite` on the offer path |
| runtime serialization | payload codec | `SerializerV2` source-generated MessagePack over `ReadOnlySequence` |

## Envelope wire layout (working draft)

Little-endian throughout. **✓ = verified from Pekko source; ◇ = our design decision / to verify.**

- **Connection preamble** (once per TCP connection) ✓: `AKKA` magic (4B) + stream id (1B: 1=control, 2=ordinary, 3=large).
- **Per frame** ✓: `[ frame length u32 LE ][ envelope ]`; length = header + payload, **back-patched from bytes-written, not predicted** (no `SizeHint` dependency — see below).
- **Envelope fixed header — 28 bytes** ✓ (offsets):

```
 off  sz  field
  0   1   version
  1   1   flags                         (bitfield: bit M = optional metadata section present ◇)
  2   1   actorRef  compression-table version
  3   1   manifest  compression-table version
  4   8   origin UID              int64 LE
 12   4   serializer id           int32 LE
 16   4   sender    ref  TAG
 20   4   recipient ref  TAG
 24   4   manifest       TAG
 28  ..   variable / optional tail
```

- **32-bit TAG** (sender / recipient / manifest) ✓ masks: top byte `0xFF000000` == 0 → LITERAL (string in tail); != 0 → COMPRESSED, low 16 bits `0x0000FFFF` = compression-table index. ◇ reserve one value = ABSENT (no-sender / no-recipient); ◇ literal length encoding.
- **Variable / optional tail** @28 ◇ (verify vs Pekko): optional metadata container (present iff `flags.M`) then length-prefixed literals — sender path, recipient path, manifest — for any LITERAL tag.
- **Payload**: V2-serialized bytes (msgpack where the type uses the generator); length = `frame_length − header_length`.
- **Hot path** (compression warm): `[ len ][ 28B fixed hdr, all tags compressed ][ payload N ]` = 32 + N bytes, zero tail, every metadata field an O(1) offset read.
- **Manifest = V2 non-CLR token** — the manifest TAG behaves exactly like the ref tags (compressed index or literal string), no CLR-type coupling. This is the one intended divergence from Pekko's encoding.

**Decode order (structural, not an optimization).** The header is parsed *before* any payload deserialization, because it carries the recipient (→ which lane) and the serializer-id + manifest (→ how to deserialize). Flow: `TcpFraming → header parse + ref/manifest decompression on the SERIAL decode island → partition to lane by recipient hash → payload deserialization on the lane (parallel)`. The header parse is on the serial critical path, so it must stay O(1)/sub-microsecond — which is exactly why it is a fixed-offset binary header, and why keeping it cheap protects the serial-island ceiling (Decision 2).

**Open sub-decisions (◇) to close in envelope design (#34):** flags bit assignments; literal length/encoding; optional metadata-container format (+ verify against Pekko); absent-sender/recipient sentinel; final field order/sizes given V2 non-CLR manifests.

## Invariants to preserve

- **Outbound queue is Association-owned and survives stream restart** (not stream-owned); the consumer re-attaches on reconnect.
- **Single reader per lane** (MPSC / `SingleReader`) — preserves per-lane ordering and keeps the queue cheap.
- **Per-recipient lane ordering**: same recipient → same lane (hash by recipient); different recipients parallelize.
- **Quarantine gating at the send-routing layer, per message type**: ordinary dropped; control + `ActorSelection` allowed; the control stream stays alive and drainable while quarantined.
- **Large stream = isolation, not chunking**: separate stream/connection; single frames up to `maximum-large-frame-size`; application-level chunking above that.
- **Overflow asymmetry**: ordinary drops, control quarantines; never block the producing actor.
- **Envelope metadata decoded before payload deserialization.**

## Risks / Trade-offs

- **The serial single-actor islands are the throughput gate — not the lanes** (verified; Decision 2). The inbound decode/partition island, the outbound encode island, and the Akka.IO connection actor each run on one core per connection and cannot be parallelized; lanes only scale deserialize. Measure the **single-island ceiling first** (must clear 680K with margin); per-boundary boxing GC is the secondary risk. Keep the Pipelines / materializer-surgery fallback ready.
- **Lane ordering**: wrong partitioning violates actor ordering. Start single-lane; add lane-ordering tests before enabling N lanes.
- **System-message reliability**: ACK/NACK/resend must be correct *before* performance tuning.
- **Remote lifecycle compatibility**: quarantine, DeathWatch, and remote deployment are subtle; keep tests close to classic behavior.
- **Serialization shape churn**: V2/sourcegen differs from Pekko's model; the envelope + compression *encoding* will not dovetail 1:1 — do not transliterate it.
- **Backpressure**: bounded queues expose problems classic remoting hid by buffering unboundedly.

## Implementation milestones & gates

Phased, **correctness-before-throughput**. Each gate is a HARD stop — do not start the next phase until it passes. All benchmarks are naked / baseline-first (per the repo benchmarking discipline: no scripts, read the tool's own output, N≥3 for any decision). These are the *within-change* gates; they sit under the epic-level M4/M5 criteria in `IMPLEMENTATION_ORDER.md`.

- **G0 — Substrate (Task 0).** The 3-config lane prototype clears the gate: single decode/partition island > 680K with margin AND lanes recover the +30% within the core budget. FAIL → materializer/stage surgery or a `System.IO.Pipelines`-substrate fallback *before proceeding*. No production code beyond the prototype until G0 passes.
- **G1 — Framing + envelope round-trip.** `TcpFraming` (connection header, LE length, oversized-frame rejection) + envelope encode/decode (fixed header + tags + literal fallback + payload back-patch) round-trip in-memory and multi-segment. Unit tests green. No async/lifecycle yet.
- **G2 — Basic ordinary messaging.** Two ActorSystems exchange ordinary user messages over Artery TCP (single stream, single lane, no compression); handshake + UID established. Gate: message sent → received → dispatched to the correct actor, with classic remoting unaffected.
- **G3 — Control stream + reliable system messages (CORRECTNESS GATE — before any perf work).** Control stream cannot be starved; system-message ACK/NACK/resend correct under duplicate, gap, and out-of-order; DeathWatch + remote-deploy behave; quarantine is UID-scoped and control-pierce works. Gate: the system-message correctness suite is green. **Do NOT tune throughput before this passes.**
- **G4 — Bounded queues + backpressure.** Bounded per-lane + control queues; overflow policy (ordinary → drop to dead-letters, control → quarantine); slow-receiver tests prove memory cannot grow unbounded.
- **G5 — Lanes + ordering.** Enable N ordinary lanes. Gate: per-recipient ordering tests green across lanes before N>1 is allowed on by default.
- **G6 — Performance milestone (headline gate).** RemotePingPong on Artery TCP **> 680K msgs/sec** (naked, N≥3); the Artery envelope codec beats the classic protobuf PDU path on allocation + throughput microbenchmarks; generated MessagePack beats V1-adapter fallback. FAIL → profile the serial islands (Decision 2) before shipping.
- **G7 — Soak + hardening.** Long-running soak (connection churn, restart-with-new-UID, quarantine cycles) with no leaks/hangs; bug-fix pass; API-approval + `dotnet build -warnaserror` clean.

**Deferred to later changes (not MVP gates):** ref/manifest compression (tables + advertisement), large-message-stream tuning, TLS (its own change), QUIC (1.7).
