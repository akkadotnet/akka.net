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

**Connection cardinality (clarified at G2, 2026-07-04).** The stream ID is a per-connection *type tag* (written once in the preamble), not a unique channel id. Per association: **1 control connection** (mandatory single — reliable system-message delivery is a per-association monotonic sequence; parallel control connections would break its ordering), **1 large connection** (when used — its purpose is isolation, not parallelism), and **N ordinary connections where N = the sender's `outbound-lanes`** (Pekko default 1; each outbound lane is a separately materialized stream ending in its own TCP connection). The receiver additionally sees one ordinary connection per remote peer — ALL inbound connections of a type, across peers and lanes, feed the one shared inbound pipeline via the hubs (Decision 2 rule 3). Per-recipient ordering survives because outbound lane selection and inbound lane partitioning both hash by recipient: same recipient → same outbound connection (TCP in-order) → same inbound lane.

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
→ [ordinary only: fan out to N lanes by recipient hash — via HUBS (JVM: MergeHub feeds the decode
   flow, FixedSizePartitionHub feeds separately-materialized lane sinks; no Partition, no .async) —
   ordering preserved]
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

**Validation gate (measure-first, before building the full stack) — PASSED 2026-07-03, see `task0-results.md`.** The empirical unknown was whether .NET's per-message materializer cost clears the DotNetty baseline. Measured (N=3, naked, baseline re-pinned at ~1.39M msgs/s aggregate on the 9900X dev box — the documented 680K was 8-core hardware): the fused framing+decode island sustains **≈3.1M msgs/s** with deserialize off-island (2.2× the local baseline), interpreter tax vs a pure-actor pipeline is **5–8%** (the +30% estimate was pessimistic), and actor-lane fan-out scales heavy deserialize 3.6× by 4 lanes. **No `System.IO.Pipelines` fallback needed.** Two plumbing substitutions were forced by measurement (rules 2–3 below): `Source.Queue` is out, and the `Partition + .Async()` lane shape is out. *(The G0 follow-on amendment "lane fan-out is actor-based pending #8314" was superseded 2026-07-04 — see rule 3: canonical Artery lanes are hubs, which have no per-element boundary to fix.)*

**Fusion findings (verified against `Akka.Streams.Implementation.Fusing` / `ActorGraphInterpreter`).** `Fusing.Aggressive` fuses all stages into ONE interpreter island (one actor, in-process push/pull, no mailbox) unless separated by `.Async()` or a different dispatcher — `Partition`/`Balance`/`Merge` do **not** split. TCP does not add a fusion boundary; inbound `Tcp.Received` carries a whole `ByteString` (many framed messages per socket read), so the socket→stream hop is **amortized per TCP read, not per message** (~0/msg). Therefore:

- **Irreducible streams tax ≈ ONE boundary crossing per inbound message** — the `partition → lane` fan-out (1 mailbox `Tell` + 1 boxing alloc of the `OnNext` struct + a short chased push/pull through the fused chain). That single hop *is* the price of N-core parallel deserialize; it is 0 if you keep one island, but then deserialize is single-core. Outbound is **0–1/msg**: ~0 with a custom drain-many queue source + coalesced `Tcp.Write`, but **stock `Source.Queue` costs 1 hop/msg** (avoid it — see Decision 9).
- **The real throughput gate is NOT the lanes — it is the serial, single-actor islands** that cannot be parallelized per connection: the inbound decode/partition island, the outbound encode island, and the Akka.IO connection actor. Lanes scale deserialize linearly across cores until one of those serial islands saturates. If the decode/partition island's per-message work stays sub-microsecond (framing + a cheap recipient hash), its ceiling is multi-million/sec — comfortably above 680K — and lanes recover the ~+30% interpreter tax by spending cores. **The disqualifier is a serial-island ceiling at/near 680K**, or GC pressure from per-boundary boxing at rate. Measured, not assumed. If 680K must flow over a *single* connection, the single decode island is the number to prove.

**Graph-design rules (minimize per-element cost; rules 2–3 AMENDED by G0 measurement 2026-07-03 — see `task0-results.md`):** (1) keep framing+encode (outbound) and framing+decode (inbound) in single fused islands — no interior `.Async()` anywhere on the hot path; (2) feed outbound from the **existing `ChannelSource.FromReader`** (verified drain-many hot path: sync `TryRead` per pull, one coalesced wakeup on empty→non-empty; measured 67–74ns/msg, 1B/msg) — **`Source.Queue` is DISQUALIFIED by measurement**, not merely dispreferred: 1,148–1,229ns + 384B *per offer* (12–15×), which alone caps outbound below the DotNetty baseline; (3) **inbound lane fan-out is HUB-based — REVISED 2026-07-04** (supersedes the G0 "actor lanes pending #8314" amendment): the verified JVM Artery topology is `MergeHub.source → inboundFlow (framing/decode) → FixedSizePartitionHub(partitioner, lanes, buffer) → separately-materialized per-lane sinks` (Pekko `ArteryTcpTransport.scala:409–446`; the partitioner hashes destination+originUid, `ArteryTransport.scala:471`) — **no `Partition`, no `.Async()` on the message path**. G0's premise that `Partition + .Async()` was "the canonical Pekko inbound lane topology" was wrong; its measurement stands as a disqualification of that shape (`ActorOutputBoundary.OnNext` = one actor message + one `OnNext` alloc **per element**, ≈520ns + 230B/msg, deeper boundary buffers provably don't help), but the shape was never Artery's. Hubs sidestep the per-element boundary entirely: elements cross via a shared bounded buffer with amortized doorbell wakeups (verified in Akka.NET `Hub.cs` — producer `Offer` + `Wakeup` only when the consumer is parked; stock `MergeHub`/`PartitionHub` with the partitioner API already exist). The JVM wrote an Artery-internal `FixedSizePartitionHub` because stock `PartitionHub` was too heavy for the hot path — expect the same possibility here. Preliminary 4-way lane baseline (#8314 harness, i9-9900K, ~500ns/element representative work, warm): fused single island ≈2.0M/s flat; `Partition+.Async()` 1.07/1.35/1.26M at 1/4/8 lanes (below single-core inline, negative 4→8 scaling — the #8314 pathology reproduced with representative work); stock `PartitionHub` 0.70/2.04/2.59M (scales, ≈2× PartitionAsync at 8 lanes, producer-bound on the single feed island); actor `Tell` 2.05/6.64/8.21M (the ceiling, but fire-and-forget — no backpressure, not a drop-in). **G5-entry gate: re-baseline hub vs bounded actor-Tell fan-out on the Artery frame corpus + real decode island (9900X dev box, N≥3, naked)** to decide stock `PartitionHub` vs porting a fixed-size hub vs (fallback) actor lanes with explicit mailbox bounding. **#8314 (boundary element-batching) is a worthwhile general-purpose streams optimization but is DECOUPLED from Artery** — lanes do not wait on it, and it is no longer sequenced against G5; (4) keep the serial decode island light (recipient-hash only; heavy deserialize in the lanes — measured island ceiling ≈3.1M msgs/s with deserialize off-island); (5) coalesce `Tcp.Write`s; (6) pool framing buffers; (7) keep OTEL/tracing listeners off the hot path (the interpreter's per-push `Activity` check is free only when no listener is attached).

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
- **Envelope fixed header — 32 bytes** (offsets; the 28-byte draft was REVISED at G1, 2026-07-04: an explicit **payload-offset** field was added at offset 28. The draft derived the payload as `frame_length − header_length`, which breaks whenever a literal tail is present; the explicit offset makes the payload slice O(1) and position-independent of the tail. Cost: 4 B/frame; on the warm hot path it is the constant 32.):

```
 off  sz  field
  0   1   version                 (= 1; decoder rejects any other value)
  1   1   flags                   (bit 0 = metadata section present; bits 1–7 reserved-must-be-zero, decoder rejects)
  2   1   actorRef  compression-table version   (= 0 in MVP)
  3   1   manifest  compression-table version   (= 0 in MVP)
  4   8   origin UID              int64 LE
 12   4   serializer id           int32 LE
 16   4   sender    ref  TAG      u32 LE
 20   4   recipient ref  TAG      u32 LE
 24   4   manifest       TAG      u32 LE
 28   4   payload offset          u32 LE  (== 32 when no metadata section and no literals)
 32  ..   [metadata section iff flags.0: u32 LE length + bytes][literals]*
 payloadOffset .. frame end       payload
```

- **32-bit TAG** (sender / recipient / manifest) ✓ masks, CLOSED at G1: tag == `0x00000000` → **ABSENT** (no-sender / no-recipient / empty manifest — unambiguous because a literal offset is always ≥ 32); top byte `0xFF000000` != 0 → **COMPRESSED**, encoder writes `0xFF000000 | index`, low 16 bits `0x0000FFFF` = table index; else → **LITERAL**, tag value = absolute byte offset of the literal from envelope offset 0 (≥ 32, < `0x00FFFFFF`).
- **Literal encoding** (CLOSED at G1): u16 LE byte length + UTF-8 bytes; encode rejects a path/manifest over 64 KB.
- **Variable / optional tail** @32 (CLOSED at G1): optional metadata container (present iff flags bit 0; u32 LE length + bytes; MVP never writes one, the decoder skips it), then the length-prefixed literals for any LITERAL tag. Tags carry absolute offsets, so literal placement is not load-bearing; the encoder's convention is sender, recipient, manifest. All literals sit in `[32, payloadOffset)`.
- **Payload**: V2-serialized bytes (msgpack where the type uses the generator); slice = `[payloadOffset, frame end)` — explicit, O(1), independent of the tail. **Single-pass encode is possible because `SerializerV2.Manifest(object)` is available UPFRONT** (verified: abstract on `SerializerV2`): look up serializer → id + manifest known → write header + literals → `Serialize(obj, IBufferWriter)` streams the payload directly into the frame buffer → back-patch the frame length only.
- **Hot path** (compression warm): `[ len ][ 32B fixed hdr, all tags compressed, payloadOffset=32 ][ payload N ]` = 36 + N bytes, zero tail, every metadata field an O(1) offset read.
- **Manifest = V2 non-CLR token** — the manifest TAG behaves exactly like the ref tags (compressed index or literal string), no CLR-type coupling. This is the one intended divergence from Pekko's encoding.

**Decode order (structural, not an optimization).** The header is parsed *before* any payload deserialization, because it carries the recipient (→ which lane) and the serializer-id + manifest (→ how to deserialize). Flow: `TcpFraming → header parse + ref/manifest decompression on the SERIAL decode island → partition to lane by recipient hash → payload deserialization on the lane (parallel)`. The header parse is on the serial critical path, so it must stay O(1)/sub-microsecond — which is exactly why it is a fixed-offset binary header, and why keeping it cheap protects the serial-island ceiling (Decision 2).

**Sub-decisions CLOSED at G1 (2026-07-04):** version = 1 (reject others); flags bit 0 = metadata present, bits 1–7 reserved-must-be-zero (reject — silently tolerating an unknown semantic flag would misparse; layout changes bump the version); ABSENT = tag `0x0`; literal = u16 LE length + UTF-8 at an absolute offset; metadata container = u32 LE length + bytes at offset 32; payload boundary = explicit payload-offset header field (see layout note). The byte layout deliberately diverges from Pekko's `EnvelopeBuffer` (payload-offset field, V2 non-CLR manifests) per Decision 4 — semantics transfer, bytes do not.

## Handshake + association/UID (gate G2)

Verified against Pekko `Handshake.scala` / `Association.scala` / `ArteryTransport.scala` + Akka.NET classic `AkkaProtocolTransport.cs` / `RemoteActorRefProvider.cs`.

**`ProtocolStateActor` has NO Artery analogue — the FSM is replaced by stream stages.** Classic's per-connection handshake FSM (`AkkaProtocolTransport.ProtocolStateActor`, Closed→WaitHandshake→Open, `HandshakeInfo{Origin, Uid:int}`) is not used by Artery (it stays for classic remoting). Artery does the handshake as **`OutboundHandshake` / `InboundHandshake` GraphStages** over the control stream, with state in a lock-free `AssociationState` object.

**Handshake protocol:** `HandshakeReq(from: UniqueAddress, to: Address)` + `HandshakeRsp(from: UniqueAddress)` over the **control stream** (streamId 1); `UniqueAddress = (Address, uid — 64-bit as of #8317)`; both sides exchange full address + UID.

**G2 staging (2026-07-04):** the control stream itself lands at G3 (Decision 5; task 6.3 moves handshake routing there). At G2 the handshake stages ride the single **ordinary** connection — the stages are stream-position-agnostic, so relocating them onto the control stream at G3 is a wiring change, not a redesign. **Control/handshake message encoding (pinned at G2): V2 source-generated MessagePack types** — `HandshakeReq`/`HandshakeRsp` (and the G3 control messages) are internal V2 msgpack-serialized types with literal manifests, dogfooding the sourcegen on the hottest internal path; they must never depend on compression state (they are what bootstraps it). Fallback if the generator cannot target Akka.Remote cleanly: a hand-written `MessagePackSerializer<T>` subclass (still V2, still msgpack) with a tracked follow-up to move onto the generator.
- **OutboundHandshake stage** — Start → ReqInProgress → Completed. On the first user element it injects `HandshakeReq` and **holds the element (`pendingMessage`) without pulling further** — user traffic queues behind the stage, never dropped. Retry timers (`handshake-retry-interval` / `inject-handshake-interval` = 1s); `handshake-timeout` = 20s → `HandshakeTimeoutException` fails the outbound stream (association retries). On completion it emits the pending element, then passes through transparently.
- **InboundHandshake stage** — validates `to == localAddress`, `completeHandshake(from)` registers the peer UID, replies `HandshakeRsp` over control; drops envelopes from unknown origin (`isKnownOrigin`).

**Association state machine:** `AssociationRegistry` keyed by remote **Address** (one Association per address, CAS-materialized) + an `association(uid)` reverse lookup (None until handshake completes). Per-association `AssociationState` (volatile, CAS-swapped) with `uniqueRemoteAddress`: **Associating** (UID unknown — gates OutboundHandshake) → **Associated** (`completeHandshake` sets it) → **Quarantined**. A **different** incoming UID (remote restart) → `newIncarnation` + atomic swap + clear outbound compression (UID-change → reset); the old UID is not auto-quarantined.

**Quarantine (UID-scoped):** acts only if the uid matches the current `uniqueRemoteAddress().uid` (stale-UID request ignored); swaps `newQuarantined`, emits `QuarantinedEvent`, clears compression, sends `ClearSystemMessageDelivery(incarnation)`. Only `ActorSelectionMessage` + `ClearSystemMessageDelivery` pierce. A **new incarnation re-associates** (keyed by Address; a new UID installs a fresh non-quarantined incarnation while the old UID stays quarantined). Prune after `remove-quarantined-association-after` = 1h.

**Provider integration:** the `RemoteTransport` seam already exists — `RemoteActorRef.Tell → Remote.Send`; the provider creates refs via `new RemoteActorRef(Transport,…)`; `DefaultAddress` / `LocalAddressForRemote` / `Quarantine` all delegate to the transport. So `ArteryRemoting : RemoteTransport` implements **9 abstract members** (two `ManagementCommand` overloads — the design draft said 8) and needs **no change** to `RemoteActorRef` or the ref-creation path. **The one wiring change:** `RemoteActorRefProvider.CreateInternals()` hard-codes `new Remoting(…)` — add a config switch (`akka.remote.artery.enabled = on` → `ArteryRemoting`, else classic) by making it read `RemoteSettings` or overriding in a subclass. **Two nodes must run the same transport** (wire + scheme differ: classic `akka.tcp://`, Artery `akka://`) — homogeneous cluster; fail fast on a mixed config.

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

## Association outbound-stream lifecycle: reconnect (group 9, DESIGNED 2026-07-05)

G2 shipped with a documented limitation: a failed outbound connection ended the association's
stream permanently. That violates the verified `SendQueue` lifecycle invariant ("the queue is
**externally owned and injected** … the queue **survives stream restart** — reconnect re-attaches
a new consumer to the same queue, so buffered messages persist") and blocks every
restart-with-new-UID scenario. Group 9 implements it:

- **The channels already survive** (Association-owned; `CompleteOutbound` only fires at transport
  shutdown). What restarts is the CONSUMER: on outbound-stream termination (connection refused,
  reset, `HandshakeTimeoutException`, write failure), the association's materialize-once gate for
  that stream RESETS, and re-materialization is scheduled after `outbound-restart-backoff` (new
  `advanced` key, default 1s). Applies independently per stream (ordinary, control).
- **Retry policy — MVP-simple, termination via existing mechanisms:** unlimited restarts with the
  fixed backoff. There is deliberately NO restart-count give-up: the association's *reliability*
  give-up already exists where it matters — `give-up-system-message-after` quarantines when
  unacked system messages age out, and quarantine gates ordinary sends. An unreachable peer with
  no pending system messages costs one bounded queue + one backoff timer — same cost class as
  Pekko's idle associations. Buffered ordinary messages persist until delivery or process
  shutdown (bounded at queue capacity; overflow policy unchanged).
- **Handshake across restart is free:** the handshake stages are per-materialization; a fresh
  stream re-injects `HandshakeReq`; the peer's reply is idempotent (`CompleteHandshake` same-uid
  no-op) or installs a new incarnation (peer restarted with new UID) — exactly the G2 semantics.
  System-message seqNo state is per-incarnation and lives OUTSIDE the stage materialization
  (delivery-stage buffer must survive restart or re-send from the buffer on re-materialization —
  implementation must preserve invariant: no unacked system message is lost by a stream restart).
- **Quarantined associations still restart their CONTROL stream** (piercing requires a live
  control channel); ordinary remains gated at `Send()`.
- **Inbound requires nothing:** new inbound connections are accepted at any time; acker state is
  per-connection/incarnation by construction.
- **Config:** `advanced.outbound-restart-backoff = 1s`. Tests use progress/order assertions only.

**Group 9 correctness suite:** kill the peer's listener mid-traffic → restart it (same address,
NEW uid) → association re-associates, new incarnation installed, old uid stays quarantinable,
buffered messages from the old incarnation are NOT delivered out of order (**PINNED, implemented
2026-07-06**: an ordinary envelope still sitting in the association-owned outbound channel — i.e.
not yet dequeued by a consumer — at the moment the old stream dies is neither dropped nor
reordered: it stays queued and is delivered, in original per-recipient order, to the new
incarnation once the fresh handshake completes. The ONLY messages that may be lost are ones
already dequeued from the channel and handed to a materialization that itself fails again before
completing its OWN handshake — an accepted, pre-existing best-effort characteristic of the
ordinary stream (it has no ack/resend, unlike the reliable system-message lane), not a new gap
introduced by reconnect. This required two implementation fixes beyond simple gate-reset-and-retry:
(1) `OutboundHandshakeStage`'s G2-era "already associated by address ⇒ skip re-handshake" fast path
is unsafe across a restart — it must be forced through a fresh `HandshakeReq` round trip
(`ForceReqOnStart`, gated on a monotonic per-association `HandshakeGeneration` counter, since a
same-uid re-handshake is a no-op on `AssociationState` and provides no other observable signal);
(2) `Akka.IO.TcpConnection` never proactively observed its write pump's completion, so a write-side
I/O failure on an otherwise-idle one-way connection could go undetected indefinitely — fixed
generally (not Artery-specific) by mirroring the existing read-pump-monitoring pattern);
(3) **the ordinary stream has no keep-alive, so it detects a dead peer only when an ordinary write
happens to fail — and a single write to a just-gracefully-closed socket can succeed locally (the
peer's RST lands only afterwards), leaving an idle ordinary stream stranded on a dead connection
indefinitely** (observed as a slow-CI-only 30s hang in the queued-redelivery spec, deterministic
on fast boxes only because loopback RST latency there wins the race). Fixed by having the CONTROL
stream — which detects the same death RELIABLY, since its periodic heartbeat always produces a
"second write" that hits the errored socket — trip a published per-materialization ordinary
`UniqueKillSwitch` (`Association._outboundKillSwitch`) when control's own connection genuinely fails,
driving the ordinary stream down through the standard termination → `ScheduleOutboundRestart` path
so it reconnects to the live incarnation alongside control instead of lingering. The trip is
**edge-triggered — once per death, not per control fault**: control captures its `OutgoingConnection`
materialized task and arms the detector (`MarkControlHealthy`) only when the connection is actually
ESTABLISHED (a connection-refused reconnect attempt against a still-dead peer faults that task and
leaves it disarmed), and the fault path fires the trip only via `TryConsumeControlHealthy()`
(atomic read-and-clear). Without the edge gate, control's ~per-backoff reconnect-loop faults would
each re-trip the ordinary stream, and a trip landing while the ordinary consumer is mid-handshake
against the revived peer would drop its single held `pendingMessage` — the accepted best-effort
window, but needless churn that empirically dropped the first buffered message ~50% of the time
under a 2-core load. With the edge gate the ordinary stream is kicked exactly once (initial death),
then self-manages its own reconnect loop and delivers its still-queued messages in order,
uninterrupted, once the peer returns. Read-side EOF cannot substitute (it fires on every healthy
one-way connection, so it was deliberately rejected as the teardown trigger); adding an ordinary
keep-alive was rejected in favour of reusing control's existing reliable detection. Non-lossy for
the pinned invariant above (a spurious trip during a control-only transient costs only a cheap
ordinary reconnect; only already-dequeued envelopes are at-most-once-dropped, queued ones survive).

**Test split (channel-buffering unit-tested; end-to-end proves ORDER).** The precise "messages
still in the channel survive a stream restart" property is asserted deterministically at the unit
level in `AssociationRestartSpec` (gate/killswitch/channel logic, no real sockets). The end-to-end
`ArteryReconnectSpec.Should_Redeliver_Queued_Ordinary_Messages_After_Reconnect` proves the
observable half — after the peer restarts under a new uid, a burst of ordinary messages to the same
path is delivered to the new incarnation **in original order** (a contiguous in-order suffix; k==0
== all delivered in the common case). It confirms reconnect by retrying a throwaway probe until one
round-trips, NOT by observing the exact moment the dead-peer stream tears down: that internal
transition's observability is subject to OS socket-close timing (a lone write to a
gracefully-closed socket can succeed locally, so a busy Windows agent may not surface the dead
connection until after the reconnect to the live one has already happened — which made an earlier
`!IsOutboundMaterialized` gate wait flaky on Windows while the reconnect itself worked fine, as the
sibling `Should_Reassociate` spec independently proves on the same platform). DeathWatch
across peer restart (watch → peer dies → Terminated via give-up/quarantine path); clean start/stop
cycles (9.2); QuarantinedEvent publication (9.4); cluster formation over Artery (9.5 — first full
integration proof; `akka://` scheme in seed nodes — verified working with the production
seed-nodes join path, no changes needed).

## Reliable system-message delivery (gate G3)

Verified against Pekko `SystemMessageDelivery.scala` + Akka.NET `AckedDelivery.cs` / `Endpoint.cs`.

**Protocol decision: port the Artery protocol (new code); plain `long` seqNos (AMENDED 2026-07-05, maintainer-approved — was "reuse `SeqNo`").** Two protocols exist — Akka.NET *classic* (`AckedDelivery`) is a heavyweight selective-NACK + inbound reorder-buffer scheme; Pekko *Artery* is deliberately simpler for the stream model: one strictly-monotonic seqNo per message, single-point `Ack(n)`/`Nack(n)`, **no inbound reorder buffer** (out-of-order is dropped + NACK'd; the *sender* re-sends in order). Sequence numbers are **plain `long`s with total-order comparison**, NOT the classic wrap-safe `SeqNo` struct: (a) seqNos are per-incarnation, reset on restart — wrapping an Int64 within one incarnation is unreachable (~29,000 years at 10M msgs/s); (b) Pekko's own `SystemMessageDelivery` uses a bare `Long`; (c) total ordering is MORE robust to garbage input — TCP-style serial arithmetic classifies half the number space as "behind", so a corrupt huge seqNo could read as a silently-dropped duplicate instead of a gap → Nack → orderly resend. Do **not** bolt the classic reorder buffers onto Artery.

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

- **The serial single-actor islands are the throughput gate — not the lanes** (verified; Decision 2; G0-measured). The inbound decode island ceiling measured ≈3.1M msgs/s on the 9900X dev box (2.2× the local DotNetty peak) — comfortable, but it is still the per-connection bound; keep the island's per-message work fixed-offset and sub-microsecond. The `.Async()` boundary's per-element cost (≈520ns + 230B/msg) disqualifies any graph shape that puts a per-element interior boundary on the hot path — hubs (the canonical lane mechanism, rule 3) don't have one. Remaining lane risks: hub feed-island saturation (stock `PartitionHub` measured producer-bound ≈2.6M/s in the #8314 harness — above the decode-island ceiling, but re-measure with the real corpus), and, if bounded actor lanes end up the fallback, mailbox bounding/backpressure (an actor mailbox does not backpressure the island the way a hub's bounded buffer does).
- **Lane ordering**: wrong partitioning violates actor ordering. Start single-lane; add lane-ordering tests before enabling N lanes.
- **System-message reliability**: ACK/NACK/resend must be correct *before* performance tuning.
- **Remote lifecycle compatibility**: quarantine, DeathWatch, and remote deployment are subtle; keep tests close to classic behavior.
- **Serialization shape churn**: V2/sourcegen differs from Pekko's model; the envelope + compression *encoding* will not dovetail 1:1 — do not transliterate it.
- **Backpressure**: bounded queues expose problems classic remoting hid by buffering unboundedly.

## Implementation milestones & gates

Phased, **correctness-before-throughput**. Each gate is a HARD stop — do not start the next phase until it passes. All benchmarks are naked / baseline-first (per the repo benchmarking discipline: no scripts, read the tool's own output, N≥3 for any decision). These are the *within-change* gates; they sit under the epic-level M4/M5 criteria in `IMPLEMENTATION_ORDER.md`.

- **G0 — Substrate (Task 0). ✅ PASSED 2026-07-03 (maintainer sign-off; `task0-results.md`).** Measured against the re-pinned local baseline (~1.39M msgs/s): decode-island ceiling ≈3.1M (criterion i), lanes recover deserialize 3.6× at 5–8% tax via actor-lane fan-out (criterion ii). Amendments: `ChannelSource.FromReader` mandatory for outbound ingress; interior per-element `.Async()` boundaries disqualified on the hot path (≈520ns+230B/msg); default inbound lanes = 4. *(2026-07-04 correction: the further "actor lanes pending #8314" amendment rested on treating `Partition + .Async()` as the canonical lane shape — it is not; JVM Artery lanes are hubs, which have no per-element boundary. See rule 3; #8314 is decoupled from Artery and no longer sequenced against G5.)*
- **G1 — Framing + envelope round-trip.** `TcpFraming` (connection header, LE length, oversized-frame rejection) + envelope encode/decode (fixed header + tags + literal fallback + payload back-patch) round-trip in-memory and multi-segment. Unit tests green. No async/lifecycle yet.
- **G2 — Basic ordinary messaging.** Two ActorSystems exchange ordinary user messages over Artery TCP (single stream, single lane, no compression); handshake + UID established. Gate: message sent → received → dispatched to the correct actor, with classic remoting unaffected.
- **G3 — Control stream + reliable system messages (CORRECTNESS GATE — before any perf work).** Control stream cannot be starved; system-message ACK/NACK/resend correct under duplicate, gap, and out-of-order; DeathWatch + remote-deploy behave; quarantine is UID-scoped and control-pierce works. Gate: the system-message correctness suite is green. **Do NOT tune throughput before this passes.**
- **G4 — Bounded queues + backpressure.** Bounded per-lane + control queues; overflow policy (ordinary → drop to dead-letters, control → quarantine); slow-receiver tests prove memory cannot grow unbounded.
- **G5 — Lanes + ordering.** Enable N ordinary lanes. **Entry:** the hub-vs-bounded-actor-Tell re-baseline on the Artery frame corpus + real decode island (rule 3; 9900X, N≥3, naked) decides the lane mechanism — stock `PartitionHub`, a ported Artery-internal fixed-size hub, or bounded actor lanes. Gate: per-recipient ordering tests green across lanes before N>1 is allowed on by default.
- **G6 — Performance milestone (headline gate).** RemotePingPong on Artery TCP **> 680K msgs/sec** (naked, N≥3); the Artery envelope codec beats the classic protobuf PDU path on allocation + throughput microbenchmarks; generated MessagePack beats V1-adapter fallback. FAIL → profile the serial islands (Decision 2) before shipping.
- **G7 — Soak + hardening.** Long-running soak (connection churn, restart-with-new-UID, quarantine cycles) with no leaks/hangs; bug-fix pass; API-approval + `dotnet build -warnaserror` clean.

**Deferred to later changes (not MVP gates):** ref/manifest compression (tables + advertisement), large-message-stream tuning, TLS (its own change), QUIC (1.7).
