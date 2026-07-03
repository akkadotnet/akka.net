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

**Validation gate (measure-first, before building the full stack) — PASSED 2026-07-03, see `task0-results.md`.** The empirical unknown was whether .NET's per-message materializer cost clears the DotNetty baseline. Measured (N=3, naked, baseline re-pinned at ~1.39M msgs/s aggregate on the 9900X dev box — the documented 680K was 8-core hardware): the fused framing+decode island sustains **≈3.1M msgs/s** with deserialize off-island (2.2× the local baseline), interpreter tax vs a pure-actor pipeline is **5–8%** (the +30% estimate was pessimistic), and actor-lane fan-out scales heavy deserialize 3.6× by 4 lanes. **No `System.IO.Pipelines` fallback needed.** Two plumbing substitutions were forced by measurement (rules 2–3 below): `Source.Queue` is out, and lane fan-out is actor-based pending the `ActorGraphInterpreter` boundary-batching infrastructure fix.

**Fusion findings (verified against `Akka.Streams.Implementation.Fusing` / `ActorGraphInterpreter`).** `Fusing.Aggressive` fuses all stages into ONE interpreter island (one actor, in-process push/pull, no mailbox) unless separated by `.Async()` or a different dispatcher — `Partition`/`Balance`/`Merge` do **not** split. TCP does not add a fusion boundary; inbound `Tcp.Received` carries a whole `ByteString` (many framed messages per socket read), so the socket→stream hop is **amortized per TCP read, not per message** (~0/msg). Therefore:

- **Irreducible streams tax ≈ ONE boundary crossing per inbound message** — the `partition → lane` fan-out (1 mailbox `Tell` + 1 boxing alloc of the `OnNext` struct + a short chased push/pull through the fused chain). That single hop *is* the price of N-core parallel deserialize; it is 0 if you keep one island, but then deserialize is single-core. Outbound is **0–1/msg**: ~0 with a custom drain-many queue source + coalesced `Tcp.Write`, but **stock `Source.Queue` costs 1 hop/msg** (avoid it — see Decision 9).
- **The real throughput gate is NOT the lanes — it is the serial, single-actor islands** that cannot be parallelized per connection: the inbound decode/partition island, the outbound encode island, and the Akka.IO connection actor. Lanes scale deserialize linearly across cores until one of those serial islands saturates. If the decode/partition island's per-message work stays sub-microsecond (framing + a cheap recipient hash), its ceiling is multi-million/sec — comfortably above 680K — and lanes recover the ~+30% interpreter tax by spending cores. **The disqualifier is a serial-island ceiling at/near 680K**, or GC pressure from per-boundary boxing at rate. Measured, not assumed. If 680K must flow over a *single* connection, the single decode island is the number to prove.

**Graph-design rules (minimize per-element cost; rules 2–3 AMENDED by G0 measurement 2026-07-03 — see `task0-results.md`):** (1) keep framing+encode (outbound) and framing+decode (inbound) in single fused islands — no interior `.Async()` anywhere on the hot path; (2) feed outbound from the **existing `ChannelSource.FromReader`** (verified drain-many hot path: sync `TryRead` per pull, one coalesced wakeup on empty→non-empty; measured 67–74ns/msg, 1B/msg) — **`Source.Queue` is DISQUALIFIED by measurement**, not merely dispreferred: 1,148–1,229ns + 384B *per offer* (12–15×), which alone caps outbound below the DotNetty baseline; (3) **inbound lane fan-out is actor-based**: the fused island's sink `Tell`s each decoded envelope to one of N lane actors by recipient hash (per-recipient ordering preserved: same recipient → same lane actor → mailbox FIFO). The canonical Pekko `Partition + .Async()` fan-out is disqualified *as the boundary is currently implemented* — `ActorOutputBoundary.OnNext` sends one actor message + one `OnNext` allocation **per element** (≈520ns + 230B/msg, and per-element, so deeper boundary buffers provably don't help; measured: negative lane scaling, ~700K flat at 16 lanes). If the planned `ActorGraphInterpreter` boundary element-batching fix (**#8314**, its own OpenSpec change, sequenced before G5) re-measures at mailbox-class cost, the canonical stream-lane shape may be re-adopted at G5 — decided by measurement, preferring the infrastructure fix over the workaround; (4) keep the serial decode island light (recipient-hash only; heavy deserialize in the lanes — measured island ceiling ≈3.1M msgs/s with deserialize off-island); (5) coalesce `Tcp.Write`s; (6) pool framing buffers; (7) keep OTEL/tracing listeners off the hot path (the interpreter's per-push `Activity` check is free only when no listener is attached).

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
| `ManyToOneConcurrentArrayQueue` + `SendQueue` | bounded lock-free MPSC buffer + pull-on-demand stream source w/ cross-thread wakeup | **one** bounded `Channel` (`SingleReader=true`) owned by the Association + the **existing `ChannelSource.FromReader`** (G0-verified drain-many hot path: sync `TryRead` per pull, coalesced wakeup, #7940 completion-race guard; measured 67–74ns/msg, 1B/msg, ~14M msgs/s). **NOT `Source.Queue`** — G0 DISQUALIFIED it by measurement: 1,148–1,229ns + 384B per offer (12–15× slower), below the DotNetty baseline on its own. Survives restart because the Channel outlives the consumer |
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

## Handshake + association/UID (gate G2)

Verified against Pekko `Handshake.scala` / `Association.scala` / `ArteryTransport.scala` + Akka.NET classic `AkkaProtocolTransport.cs` / `RemoteActorRefProvider.cs`.

**`ProtocolStateActor` has NO Artery analogue — the FSM is replaced by stream stages.** Classic's per-connection handshake FSM (`AkkaProtocolTransport.ProtocolStateActor`, Closed→WaitHandshake→Open, `HandshakeInfo{Origin, Uid:int}`) is not used by Artery (it stays for classic remoting). Artery does the handshake as **`OutboundHandshake` / `InboundHandshake` GraphStages** over the control stream, with state in a lock-free `AssociationState` object.

**Handshake protocol:** `HandshakeReq(from: UniqueAddress, to: Address)` + `HandshakeRsp(from: UniqueAddress)` over the **control stream** (streamId 1); `UniqueAddress = (Address, uid)`; both sides exchange full address + UID.
- **OutboundHandshake stage** — Start → ReqInProgress → Completed. On the first user element it injects `HandshakeReq` and **holds the element (`pendingMessage`) without pulling further** — user traffic queues behind the stage, never dropped. Retry timers (`handshake-retry-interval` / `inject-handshake-interval` = 1s); `handshake-timeout` = 20s → `HandshakeTimeoutException` fails the outbound stream (association retries). On completion it emits the pending element, then passes through transparently.
- **InboundHandshake stage** — validates `to == localAddress`, `completeHandshake(from)` registers the peer UID, replies `HandshakeRsp` over control; drops envelopes from unknown origin (`isKnownOrigin`).

**Association state machine:** `AssociationRegistry` keyed by remote **Address** (one Association per address, CAS-materialized) + an `association(uid)` reverse lookup (None until handshake completes). Per-association `AssociationState` (volatile, CAS-swapped) with `uniqueRemoteAddress`: **Associating** (UID unknown — gates OutboundHandshake) → **Associated** (`completeHandshake` sets it) → **Quarantined**. A **different** incoming UID (remote restart) → `newIncarnation` + atomic swap + clear outbound compression (UID-change → reset); the old UID is not auto-quarantined.

**Quarantine (UID-scoped):** acts only if the uid matches the current `uniqueRemoteAddress().uid` (stale-UID request ignored); swaps `newQuarantined`, emits `QuarantinedEvent`, clears compression, sends `ClearSystemMessageDelivery(incarnation)`. Only `ActorSelectionMessage` + `ClearSystemMessageDelivery` pierce. A **new incarnation re-associates** (keyed by Address; a new UID installs a fresh non-quarantined incarnation while the old UID stays quarantined). Prune after `remove-quarantined-association-after` = 1h.

**Provider integration:** the `RemoteTransport` seam already exists — `RemoteActorRef.Tell → Remote.Send`; the provider creates refs via `new RemoteActorRef(Transport,…)`; `DefaultAddress` / `LocalAddressForRemote` / `Quarantine` all delegate to the transport. So `ArteryRemoting : RemoteTransport` implements **8 abstract members** and needs **no change** to `RemoteActorRef` or the ref-creation path. **The one wiring change:** `RemoteActorRefProvider.CreateInternals()` hard-codes `new Remoting(…)` — add a config switch (`akka.remote.artery.enabled = on` → `ArteryRemoting`, else classic) by making it read `RemoteSettings` or overriding in a subclass. **Two nodes must run the same transport** (wire + scheme differ: classic `akka.tcp://`, Artery `akka://`) — homogeneous cluster; fail fast on a mixed config.

**Reuse:** the `RemoteTransport`/`RemoteActorRef` seam (no change), `QuarantinedEvent` + lifecycle events (Cluster already consumes them), `AddressUidExtension` (but see UID width), Akka.Streams `GraphStage`. **Build new:** `AssociationState` (immutable + `Interlocked.CompareExchange`), `Association`, Address-keyed `AssociationRegistry` + uid reverse index, `InboundContext`, the two handshake stages, `HandshakeReq/Rsp` proto + framing.

### UID width — DECIDED: widen Akka.NET to 64-bit UID (v1.6-wide)

Akka.NET's classic UID is a 32-bit `int` (`AddressUidExtension.Uid` → `int`; `Cluster.SelfUniqueAddress` is built from it; `MiscMessageSerializer` even has a `// TODO: change to uint32`). Pekko/Artery's UID is a 64-bit `long`.

**DECISION (maintainer, v1.6): widen Akka.NET's UID to 64-bit** — make it the v1.6 baseline, not an Artery-only concern. Rationale: adopting Artery already requires a full-cluster restart (homogeneous transport; no classic↔Artery interop), so the breaking UID change rides that same downtime; it matches Pekko's wire; and the widening is independently wanted (the `MiscMessageSerializer` TODO).

**This is a v1.6 FOUNDATION change, bigger than Artery — and a prerequisite for it.** It touches `AddressUidExtension`, `Cluster.SelfUniqueAddress`, gossip, `RemoteWatcher`, the quarantine API (`RemoteTransport.Quarantine(Address, int? uid)` → add a `long`-uid overload, extend-only), and the affected serializers / wire formats. It needs its own scoping (task #39), likely **its own OpenSpec change/milestone sequenced before Artery**, plus a `BREAKING_CHANGES_V1.6.md` entry.

**Scoping outcome (#39, verified):**
- **Scope:** only the **address/system UID** is in scope. The ActorPath incarnation uid (`#12345`) is **already `long`** — leave it (`ActorPath.Uid`, `Failed.Uid`, `SystemMessageFormats.proto` all 64-bit already).
- **Wire is far cheaper than feared:** only `ClusterMessages.proto`'s `UniqueAddress.uid` (`uint32`) must widen to `uint64`. The handshake (`WireFormats.proto fixed64`), RemoteWatcher heartbeat (`ContainerFormats.proto uint64`) and DistributedData (`ReplicatorMessages.proto int64`) are **already 64-bit on the wire** and only narrow in C#.
- **Rolling upgrade is safe:** `uint32 → uint64` is the same protobuf varint wire type, so widening is binary-compatible for values ≤ `uint32.MaxValue`. Widen the **type** everywhere now but keep generated values in int-range (default `ThreadLocalRandom.Next()`); flip to true 64-bit generation (custom RNG — `Random.NextInt64` is net6+ only, absent on netstandard2.0/net48) behind a config switch **only** after the whole cluster + all remote peers are on v1.6. Full-cluster restart is required only to enable >32-bit generation, not to adopt the wider type.
- **The real cost is the public API surface** (10 approved members: `UniqueAddress`, `AddressUid`, `AddressUidExtension.Uid`, `QuarantinedEvent`, `RemoteWatcher.HeartbeatRsp`/`AddressUid`, and all `Quarantine(Address, int? uid)` members) + the internal `int→long` sweep (`Endpoint*.cs`, `EndpointManager.cs`, `EndpointRegistry.cs`, `AkkaProtocolTransport.cs`, `RemoteWatcher.cs`).
- **→ Its own OpenSpec change `widen-system-uid-to-64bit`, sequenced FIRST** (before Artery, coordinated with `serializer-v2`'s schema).
- **One sub-decision:** *extend-only* (add `long` members + `[Obsolete]` the `int` ones — awkward for the un-overloadable `Uid` fields/props) vs a *clean hard re-type* (defensible since v1.6 is already a breaking cycle).

**G2 correctness suite:** happy-path associate (UID both ways; `association(uid)` None→Some); traffic-stall buffering (pre-completion messages delivered in order, zero drops); timeout + retry cadence; incarnation change (new UID resets association, ordering preserved); quarantine (+ new-UID re-associate, stale-UID ignored, pruning); InboundHandshake guards (wrong `to`, unknown origin); **Cluster integration — `SelfUniqueAddress` UID == observed handshake UID (the test that pins the int/long decision)**; config switch + coexistence (mixed-transport fails fast).

## Reliable system-message delivery (gate G3)

Verified against Pekko `SystemMessageDelivery.scala` + Akka.NET `AckedDelivery.cs` / `Endpoint.cs`.

**Protocol decision: port the Artery protocol (new code); reuse only `SeqNo`.** Two protocols exist — Akka.NET *classic* (`AckedDelivery`) is a heavyweight selective-NACK + inbound reorder-buffer scheme; Pekko *Artery* is deliberately simpler for the stream model: one strictly-monotonic seqNo per message, single-point `Ack(n)`/`Nack(n)`, **no inbound reorder buffer** (out-of-order is dropped + NACK'd; the *sender* re-sends in order). Reuse Akka.NET's wrap-safe `SeqNo`; build the two GraphStages new. Do **not** bolt the classic reorder buffers onto Artery.

- **Outbound `SystemMessageDelivery` stage (control stream):** per-incarnation seqNo; wrap each msg `SystemMessageEnvelope(msg, seqNo, ackReplyTo)`; bounded `unacknowledged` deque (`system-message-buffer-size` = 20000); resend timer (`system-message-resend-interval`); `Ack(n)` pops seq ≤ n; `Nack(n)` pops prefix + immediate tail resend; **give-up → quarantine** on buffer overflow OR `give-up-system-message-after` timeout (bypasses the restart counter); `ClearSystemMessageDelivery(incarnation)` resets seqNo + empties the buffer.
- **Inbound `SystemMessageAcker` stage (control stream):** per-sender-**UID** expected-seq map; `n == expected` → deliver + `Ack(n)`; `n < expected` → duplicate, drop, re-`Ack(expected-1)`; `n > expected` → gap, drop, `Nack(expected-1)`. No inbound buffering — the sender restores order.
- **Semantics:** at-least-once on the wire (resend) + inbound dedup ⇒ **effectively exactly-once, strictly in-order** to the destination actor. `Ack`/`Nack` are best-effort control-stream replies (loss covered by the resend timer); only the `SystemMessageEnvelope` is reliable.
- **What rides it:** the DeathWatch triple (`Watch`/`Unwatch`/`DeathWatchNotification`) + `Terminate` — correctness-critical (a lost `Watch` → no `Terminated` → broken Cluster failure detection / Singleton / Sharding). **Remote deploy does NOT ride it** — `DaemonMsgCreate` is an ordinary message; only the DeathWatch on the deployed child must be reliable. Don't over-scope the reliable layer to remote deploy.

**Invariants (must preserve):** (1) exactly-once, strictly in-order to the destination; (2) per-incarnation seqNo reset, inbound state keyed by sender UID, new incarnation → expected restarts at 1; (3) **stale-ACK guard (mandatory):** a late `Ack` from a *prior* association (seq > current max) must NEVER quarantine — `isFromCurrentRemote` UID check (= Akka.NET #6414's `CumulativeAck > MaxSeq` fix); (4) give-up (overflow OR timeout) → quarantine, never a silent drop; (5) control lane dedicated + un-starvable, system messages NEVER hashed onto ordinary lanes (hard constraint on the lanes work); (6) Ack/Nack best-effort — correctness rests only on the resend timer + inbound dedup.

**Open decisions:** inbound expected-seq map needs an eviction policy (Pekko has an unbounded-growth `TODO`); wire-format scope (.NET-native vs Pekko-protobuf for future JVM interop — freeze extend-only in `BREAKING_CHANGES_V1.6.md`); default divergence (Pekko give-up 6 h vs classic ~3 m — pick deliberately); give-up vs Cluster failure-detector interplay; graceful-shutdown for in-flight system messages (dead-letter vs quarantine).

**G3 correctness suite:** happy-path; ACK-loss (resend + dedup, no dupes); gap → NACK → in-order restore; reordering (proves no inbound buffer needed); buffer-overflow → quarantine; give-up-timeout → quarantine; new-incarnation + stale-ACK regression (#6414); control-lane non-starvation under ordinary-lane load; DeathWatch end-to-end under loss; idempotent `Clear`; graceful-shutdown.

## Actor-ref / manifest compression (deferred — post-MVP)

Verified against Pekko `compress/*`. **Deferred**, but researched now to confirm the envelope accommodates it and to have the design ready.

**MVP obligation (the only now-work):** the envelope already reserves the two table-version header bytes (actorRef @off 2, manifest @off 3) and the compressed-index-or-literal tag scheme — **verified byte-for-byte compatible with Pekko** (`TagTypeMask 0xFF000000` top byte = compressed, `TagValueMask 0x0000FFFF` low 16 = index). Writing version `0` + all-literal tags is the forward-safe no-compression encoding; **not reserving these would be a guaranteed wire break** when compression lands, so they stay in the MVP header. One derived constraint: keep `maximum-frame-size` **well under 16 MB** so literal offsets stay < `0x00FFFFFF` (the tag discriminator depends on it).

**The scheme (receiver-driven, per-UID, versioned string interning):** the *receiver* observes its heavy-hitter refs/manifests, builds a `value → int` table, and **advertises it to the sender over the CONTROL stream**; the sender then writes a 16-bit index instead of the literal and stamps the table version into the header byte. Decode = bounds-checked array index (O(1), alloc-free). Ownership is inverted — receiver builds (holds `int → ActorRef` resolution), sender encodes. Max 65,536 entries/table (16-bit index; never a real limit).

- **Heavy-hitters:** CountMinSketch (approx frequency) + fixed top-K `TopHeavyHitters` (open-addressing hash + min-heap). **Adaptive sampling** (effectively off < 1000 msg/s, then ~every 64th/128th/256th) keeps it off the per-message hot path — port the sampling, do not count every message.
- **Advertisement protocol:** `ActorRefCompressionAdvertisement` / `ClassManifestCompressionAdvertisement` (+ Acks) over control; receiver builds → advertises (resend ≤3), sender adopts → acks, receiver confirms → promotes (`nextTable → activeTable`, retains ≤3 old tables so in-flight messages on the prior version still decode). Header version byte selects the table (active / ≤3 old / next); **no match → drop the message, do not reset the stream**.
- **UID-scoped lifecycle:** keyed by originUid; remote restart = new UID = fresh table (v0), old dead; `close(originUid)` on quarantine/removal. Outbound compression is disabled for handshake/system traffic (avoids desync across restart).

**.NET mapping:** `DecompressionTable<T>` = `T?[]` + originUid + version, `Get` = bounds-check + index (zero alloc); table-selection = linear scan over a ≤5-entry immutable snapshot; cache the per-association `InboundCompression` on the association to skip a per-message dictionary hit; outbound table = `FrozenDictionary<TKey,int>` (.NET 8) built once on advertisement; port `TopHeavyHitters` + `CountMinSketch` (fixed-size, sampled); funnel advertise/confirm mutations onto the single owning stage via async callback (single-threaded, lock-free).

**Open decisions:** the interning KEY for `IActorRef` — Pekko uses object identity, but we should likely key on the serialized path string (`RemoteActorRef`/`RepointableActorRef` identity is historically thorny in Akka.NET); table-selection structure; sampling thresholds; lifecycle wiring; and mirror Pekko's "don't reserve index 0 / skip temporary (ask) refs".

## Invariants to preserve

- **Outbound queue is Association-owned and survives stream restart** (not stream-owned); the consumer re-attaches on reconnect.
- **Single reader per lane** (MPSC / `SingleReader`) — preserves per-lane ordering and keeps the queue cheap.
- **Per-recipient lane ordering**: same recipient → same lane (hash by recipient); different recipients parallelize.
- **Quarantine gating at the send-routing layer, per message type**: ordinary dropped; control + `ActorSelection` allowed; the control stream stays alive and drainable while quarantined.
- **Large stream = isolation, not chunking**: separate stream/connection; single frames up to `maximum-large-frame-size`; application-level chunking above that.
- **Overflow asymmetry**: ordinary drops, control quarantines; never block the producing actor.
- **Envelope metadata decoded before payload deserialization.**

## Risks / Trade-offs

- **The serial single-actor islands are the throughput gate — not the lanes** (verified; Decision 2; G0-measured). The inbound decode island ceiling measured ≈3.1M msgs/s on the 9900X dev box (2.2× the local DotNetty peak) — comfortable, but it is still the per-connection bound; keep the island's per-message work fixed-offset and sub-microsecond. The measured risk moved: the `.Async()` boundary's per-element cost (≈520ns + 230B/msg) is what disqualified stream lanes — any future graph change that reintroduces an interior boundary on the hot path re-opens it. Actor-lane mailbox bounding is deferred to G4 (an actor mailbox does not backpressure the island the way a stream lane would).
- **Lane ordering**: wrong partitioning violates actor ordering. Start single-lane; add lane-ordering tests before enabling N lanes.
- **System-message reliability**: ACK/NACK/resend must be correct *before* performance tuning.
- **Remote lifecycle compatibility**: quarantine, DeathWatch, and remote deployment are subtle; keep tests close to classic behavior.
- **Serialization shape churn**: V2/sourcegen differs from Pekko's model; the envelope + compression *encoding* will not dovetail 1:1 — do not transliterate it.
- **Backpressure**: bounded queues expose problems classic remoting hid by buffering unboundedly.

## Implementation milestones & gates

Phased, **correctness-before-throughput**. Each gate is a HARD stop — do not start the next phase until it passes. All benchmarks are naked / baseline-first (per the repo benchmarking discipline: no scripts, read the tool's own output, N≥3 for any decision). These are the *within-change* gates; they sit under the epic-level M4/M5 criteria in `IMPLEMENTATION_ORDER.md`.

- **G0 — Substrate (Task 0). ✅ PASSED 2026-07-03 (maintainer sign-off; `task0-results.md`).** Measured against the re-pinned local baseline (~1.39M msgs/s): decode-island ceiling ≈3.1M (criterion i), lanes recover deserialize 3.6× at 5–8% tax via actor-lane fan-out (criterion ii). Amendments: `ChannelSource.FromReader` mandatory for outbound ingress; interior `.Async()` disqualified as implemented (per-element boundary ≈520ns+230B/msg) — actor lanes now, canonical stream lanes re-evaluated at G5 if the boundary element-batching fix (#8314, its own change, sequenced before G5) measures at mailbox-class cost; default inbound lanes = 4.
- **G1 — Framing + envelope round-trip.** `TcpFraming` (connection header, LE length, oversized-frame rejection) + envelope encode/decode (fixed header + tags + literal fallback + payload back-patch) round-trip in-memory and multi-segment. Unit tests green. No async/lifecycle yet.
- **G2 — Basic ordinary messaging.** Two ActorSystems exchange ordinary user messages over Artery TCP (single stream, single lane, no compression); handshake + UID established. Gate: message sent → received → dispatched to the correct actor, with classic remoting unaffected.
- **G3 — Control stream + reliable system messages (CORRECTNESS GATE — before any perf work).** Control stream cannot be starved; system-message ACK/NACK/resend correct under duplicate, gap, and out-of-order; DeathWatch + remote-deploy behave; quarantine is UID-scoped and control-pierce works. Gate: the system-message correctness suite is green. **Do NOT tune throughput before this passes.**
- **G4 — Bounded queues + backpressure.** Bounded per-lane + control queues; overflow policy (ordinary → drop to dead-letters, control → quarantine); slow-receiver tests prove memory cannot grow unbounded.
- **G5 — Lanes + ordering.** Enable N ordinary lanes. Gate: per-recipient ordering tests green across lanes before N>1 is allowed on by default.
- **G6 — Performance milestone (headline gate).** RemotePingPong on Artery TCP **> 680K msgs/sec** (naked, N≥3); the Artery envelope codec beats the classic protobuf PDU path on allocation + throughput microbenchmarks; generated MessagePack beats V1-adapter fallback. FAIL → profile the serial islands (Decision 2) before shipping.
- **G7 — Soak + hardening.** Long-running soak (connection churn, restart-with-new-UID, quarantine cycles) with no leaks/hangs; bug-fix pass; API-approval + `dotnet build -warnaserror` clean.

**Deferred to later changes (not MVP gates):** ref/manifest compression (tables + advertisement), large-message-stream tuning, TLS (its own change), QUIC (1.7).
