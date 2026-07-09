## Context

Artery replaces per-message actor-path and manifest **literals** with small integer indices once a
compression table has been negotiated. Profiling Artery TCP on a quiet 9900X attributes **~68% of
per-message allocation** to literal path/manifest serialization (`Encoding.UTF8.GetBytes` per literal
in `ArteryEnvelopeCodec.cs`, and a `string` allocation per literal on decode). Because GC/allocation
is on the throughput critical path, compressing paths/manifests to indices is the next throughput
lever after TCP write-coalescing.

The envelope wire format was built for this and needs **no wire change**:

- Each of the sender / recipient / manifest tags is a 32-bit value: `AbsentTag = 0`; a non-zero top
  byte marks COMPRESSED with the index in the low 16 bits (`CompressedIndexMask = 0x0000_FFFF`,
  max 65 535/table); otherwise the tag is a LITERAL body offset.
- The fixed 32-byte header already reserves an **actor-ref table-version byte** and a **manifest
  table-version byte** (offsets 2 and 3), matching Pekko's per-table `version: Byte`.
- `ArteryEnvelopeDecoded` already classifies tags and exposes `SenderCompressedIndex` /
  `RecipientCompressedIndex` / `ManifestCompressedIndex`; today it cannot resolve them (no table) and
  the inbound stage **drops** COMPRESSED messages.

This document records the Pekko compression model (verified against Apache Pekko `main`, Apache 2.0),
maps each mechanism to Akka.NET, and flags the parts to review — above all the **advertisement
protocol** and the **inbound ownership/threading** question — before the subtle protocol is built.

> Claims marked **(verified)** were read from Pekko source during design, not recalled.
> Pekko sources: `remote/.../artery/compress/{CompressionTable,DecompressionTable,InboundCompressions,CompressionProtocol}.scala`,
> `artery/Codecs.scala`, `artery/Association.scala`, `artery/ArteryTransport.scala`,
> `serialization/ArteryMessageSerializer.scala`.

## Goals / Non-Goals

**Goals:**
- Emit COMPRESSED sender/recipient/manifest tags for warm values; fall back to LITERAL otherwise.
- Resolve COMPRESSED indices on decode, completing the deferred "resolve index -> value".
- Port the receiver-driven, versioned table-advertisement protocol faithfully (semantics), while
  taking .NET-idiomatic liberties on wire encoding and threading where they don't change behavior.
- Keep compression **off by default** and the disabled/non-advertising path byte-identical to today.

**Non-Goals:**
- No envelope wire-layout change (tags + version bytes already reserved).
- No JVM-Artery wire compatibility (Akka.NET Artery is its own wire).
- No compression of control/handshake (`ArteryMessage`) traffic — ever (verified: Pekko
  `useOutboundCompression(!isArteryMessage)`).
- Not flipping the Encoder to COMPRESSED in the scaffold phase — that is gated on the full loop + tests.

## Verified Pekko compression model (Apache `main`)

**Direction — the receiver drives everything (verified).** Compression is negotiated *backwards*
relative to data flow. When node **B** receives messages from node **A**:

1. **Observe.** In the Decoder, B calls `hitActorRef` / `hitClassManifest` for the sender ref,
   recipient ref, and manifest of inbound messages, sampled (`if ((messageCount & heavyHitterMask) == 0)`),
   feeding a per-origin **frequency sketch** and a **`TopHeavyHitters`** set (default max 256).
   Temporary/`PromiseActorRef`s and empty manifests are excluded (verified).
2. **Build + advertise.** On a schedule (`advertisement-interval`, default **1 minute**), B builds a
   `CompressionTable[T]` mapping its top hitters to dense indices `0..N-1` at the next version, and
   sends it to A over the **control stream** as `ActorRefCompressionAdvertisement(from = B.localAddress, table)`
   (or the manifest variant). The table's `originUid` is **A's UID** — the system that will use it for
   outbound (verified).
3. **Install + ack.** A receives the advertisement, installs it as its **outbound** compression table
   for sending to B (`association(from).changeActorRefCompression(table)`), and replies
   `ActorRefCompressionAdvertisementAck(A.localAddress, table.version)` (verified, `ArteryTransport.scala`).
4. **Use.** A's Encoder now stamps the table version into the header's version byte and writes a
   COMPRESSED index for any ref/manifest in the table; misses still go out LITERAL (verified,
   `Codecs.scala` Encoder + `headerBuilder`).
5. **Confirm + rotate.** B activates the advertised table when it sees either the Ack **or** the first
   inbound message stamped with the new version (`confirmAdvertisement` -> `startUsingNextTable`).
   B keeps up to **`KeepOldTablesNumber = 3`** superseded tables so in-flight messages at an older
   version still decode (verified, `InboundCompression.Tables`).

**Data types (verified):**
- `CompressionTable[T](originUid, version: Byte, dictionary: Map[T,Int])` — outbound-facing
  `value -> index`; `compress(v)` returns `NotCompressedId = -1` on a miss; `invert -> DecompressionTable`.
- `DecompressionTable[T](originUid, version, table: Array[T])` — inbound-facing `index -> value`; `get(idx)`.
- `version`: `-1` disabled, else `0..127`, wraps `127 -> 0` (verified `incrementTableVersion`).

**Table rotation state per origin (verified, `InboundCompression.Tables`):** `activeTable` (starts
empty v0), `nextTable` (starts empty v1), `oldTables` (starts `List(disabled@v-1)`, capped at 3),
`advertisementInProgress: Option[CompressionTable]`. `selectTable(version)` checks active, then old,
then — if it equals the in-progress version — flips to it and retries; an unknown/greater version
warns and returns None (message dropped, previous-incarnation table).

**Advertisement lifecycle (verified, `runNextTableAdvertisement`):** if none in progress and the
association exists + ordinary stream active, build `nextTable` from heavy hitters, set
`advertisementInProgress`, mark `alive = false`, and send. While in progress, **resend** up to
`maxResendCount = 3`; after that **give up** (`confirmAdvertisement(gaveUp = true)` — flip anyway so
the system isn't wedged). Don't advertise to a quarantined origin; `close(originUid)` drops all state.

**Wire form of the advertisement (verified, `ArteryMessageSerializer`):** protobuf
`CompressionTableAdvertisement { from: UniqueAddress, originUid: int64, tableVersion: int32,
repeated keys: string, repeated values: int32 }` — `keys` are serialized actor paths (or manifest
strings). The Ack is `{ from, version }`. Refs serialize as **paths** on the wire either way.

## Akka.NET port — decisions

### Decision 1 — String-keyed tables (not `ActorRef`-keyed)

Pekko keys the actor-ref table on `ActorRef`. The Akka.NET Encoder already works in **serialized path
strings**: `ArteryRemoting.Send` computes `recipientPath`/`senderPath` via
`ToSerializationFormatWithAddress(...)` and packs them into `OutboundEnvelope`; the Encode stage
never sees an `IActorRef` (verified from the wiring map: `ArteryEncodeStage.cs:153`,
`ArteryRemoting.cs:265/276`). So the port keys **both** categories on `string`
(`CompressionTable<string>`, `DecompressionTable<string>`):

- No `IActorRef` resolution on the hot encode path.
- The sender's lookup key is byte-identical to what the receiver observed and advertised (the receiver
  echoes back exactly the path string the sender sent), which is strictly more robust than
  round-tripping through `ActorRef` and back.
- The heavy-hitter observation on the inbound side uses the **decoded LITERAL string** directly.

*Trade-off:* two distinct paths for the same logical ref (rare, e.g. differing address formatting)
would be two table entries. Acceptable — the sender's formatting is deterministic per ref.

### Decision 2 — Outbound table lives on `Association`, swapped as an immutable reference

The outbound table is per-destination and must survive outbound-stream restarts, exactly like the
existing `SystemMessageDeliveryState` (which is `Association`-owned for that reason —
`AssociationRegistry.cs:215`). Add to `Association`:

```
private volatile CompressionTable<string> _outboundActorRefTable = CompressionTable<string>.Empty;
private volatile CompressionTable<string> _outboundManifestTable  = CompressionTable<string>.Empty;
```

- **Reader:** each `ArteryEncodeStage` reads the current table via `Volatile.Read` per message.
- **Writer:** when an advertisement arrives (control-stream thread), swap via `Volatile.Write`
  (or `Interlocked.Exchange`). The table is **immutable**, so `(version, dictionary)` can never tear —
  a lane always reads a consistent version-and-indices pair. This is simpler and safer than Pekko's
  per-lane `headerBuilder` mutation + async-callback fan-out to N encoders (verified Pekko does the
  fan-out because each Encoder owns its own mutable builder; we don't need to, one shared immutable
  reference serves all lanes).

The Encode stage gains one constructor argument — the `Association` (or a narrow
`IOutboundCompressionSource` exposing the two `Volatile.Read`s) — threaded in at
`ArteryRemoting.cs:659` where the stage is materialized and the `Association` is in scope (the same
seam `SystemMessageDeliveryState` uses at `:682`).

### Decision 3 — Inbound tables + heavy hitters keyed by origin UID

Inbound compression state is per **origin UID** (the join key already threaded everywhere: header
`OriginUid`, `InboundEnvelope.OriginUid`, `IControlMessageSubscriber.ControlMessageReceived(long originUid,...)`,
`AssociationRegistry.TryGetByUid(long)`). `InboundCompressionsImpl` holds
`ConcurrentDictionary<long, InboundCompression>` created on demand per origin, each owning its table
rotation + frequency sketch + top-N. Keying purely on UID (not on a registered `Association`) is the
robust seam: **pre-handshake** inbound frames carry an origin UID before the association is in `_byUid`
(verified caveat from the wiring map). Advertisement, however, only fires when `TryGetByUid` resolves a
non-quarantined association (matches Pekko's "no association yet -> don't advertise").

### Decision 4 — Decode-side resolution + drop-not-crash

The two inbound hook sites already exist as "drop COMPRESSED" branches (verified wiring map,
`ArteryInboundProcessingStage.cs:223-240`). Replace each drop with
`compressions.TryDecompress{ActorRef,ClassManifest}(originUid, tableVersion, idx, out value)`
(`tableVersion` from `decoded.Header.ActorRefTableVersion` / `ManifestTableVersion`; `idx` from the
existing `*CompressedIndex` accessors). A miss (unknown/stale/previous-incarnation table) keeps the
current behavior — **drop with a warning, don't fault the stream** — and lets a fresh table be
advertised (verified Pekko behavior).

### Decision 5 — Advertisement wire form: single ordered list

Because indices are dense `0..N-1`, advertise one ordered `IReadOnlyList<string> Table` (position =
index) instead of Pekko's parallel `keys[]`/`values[]`. Equivalent, smaller, gap-impossible. The four
control messages become `IArteryControlMessage` + `[AkkaSerializable]` records with MessagePack V2
manifest constants in `ArteryControlMessageSerializer` (the established pattern — `UniqueAddress` is
already `[AkkaSerializable]`, `Address` via the `AddressFormatter` escape hatch). Dispatch: an
`IControlMessageSubscriber` (or an added case in `ArteryRemoting.ControlMessageReceived`, `:436`)
handles advertisements → swap outbound table + reply Ack; handles Acks → confirm inbound table.
Send via the existing `EnqueueControl` / `SendControlToAddress` (`ArteryRemoting.cs:539/465`).

### Decision 6 — Heavy-hitter detector: start simple, keep the seam

Pekko offers `count-min-sketch` (~128 KB/conn) and the default `fast-frequency-sketch` (TinyLFU
aging, ~4 KB/conn). Porting `FastFrequencySketch` faithfully is a meaningful chunk of work whose
quality affects only *which* refs get compressed, never correctness. **Proposal:** ship an
`IFrequencySketch<T>` seam with a small, bounded count-based implementation for the MVP (a capped
counter + `TopHeavyHitters`), and port `FastFrequencySketch` later behind the same interface and the
`frequency-sketch-implementation` setting. Flagged for review (see Q4).

### Decision 7 — Settings under `artery.advanced.compression`, off by default

New keys parsed in `ArterySettings` (defaults in `Akka.Remote/Configuration/Remote.conf`), following
the "parse now, use later" precedent of `inbound-lanes`/`outbound-lanes`:
`advanced.compression.enabled` (default `off`), `.actor-refs.max` (256, `off` disables),
`.manifests.max` (256), `.advertisement-interval` (1 minute), `.frequency-sketch-implementation`.
`Enabled` gates the whole feature; when off, the Decoder installs `NoInboundCompressions` and the
Encoder reads only `CompressionTable<string>.Empty`, so the wire is byte-identical to today.

## Open questions — REVIEW BEFORE IMPLEMENTATION

**Q1 (the subtle one) — Advertisement scheduling + ownership/threading.** Where does the per-origin
advertisement timer live, and how do the timer, the inbound Ack handler, and the hot decode path
share the inbound tables safely?
- **Option A (Pekko-faithful):** `InboundCompressions` owned by the inbound decode stage; timer +
  Ack marshaled onto the stage thread via GraphStage async callbacks; no locks, but real stage
  plumbing, and it assumes a single decode point.
- **Option B (recommended, .NET-idiomatic):** `InboundCompressions` owned by the `AssociationRegistry`
  (keyed by UID via the existing `_byUid`). Hot-path **decompression reads are lock-free** (the active
  `DecompressionTable` per origin behind a `volatile` immutable reference); the sampled heavy-hitter
  mutation and the periodic table build take a short per-origin lock. The advertisement timer is a
  plain `Context.System.Scheduler` tick (no dedicated dispatcher — consistent with the repo's
  "let the ThreadPool cook" stance); the Ack handler is just the control subscriber. This avoids
  async-callback plumbing and matches the registry structure that already exists.
- **Decide:** A vs B, and confirm whether Artery inbound decode is a single point (before lane
  fan-out) — if it is, B's per-origin locking is essentially uncontended.

**Q2 — Confirmation triggers.** Port both Pekko triggers (explicit Ack **and** first stamped message)
or rely on the Ack alone for the MVP? The Ack alone is simpler; the "first stamped message" path is
Pekko's belt-and-suspenders for when the Ack is lost. Recommendation: port both (cheap once the
rotation state machine exists).

**Q3 — Old-table retention + version wraparound.** Confirm `KeepOldTables = 3` and the `127 -> 0`
wrap are ported verbatim, including the "unknown/greater version -> drop + re-advertise" path that
covers a restarted-incarnation sender. This is the main correctness edge case.

**Q4 — Frequency sketch fidelity (Decision 6).** Approve the "simple bounded counter now, port
`FastFrequencySketch` later behind the seam" plan, or require the faithful sketch up front?

**Q5 — Outbound table concurrency (Decision 2).** Approve the single shared immutable
`Volatile.Read`/`Write` reference on `Association` (vs Pekko's per-lane async-callback fan-out). This
is a deliberate simplification enabled by immutability; confirm it's acceptable.

**Q6 — When does the sender start stamping the new version?** On install, A's subsequent messages use
the new table/version; queued/in-flight messages keep the old version. This relies on Q3's old-table
retention on B. Confirm no barrier/flush is needed (Pekko has none — verified).

**Q7 — Metrics/observability.** Pekko publishes `Received{ActorRef,ClassManifest}CompressionTable`
events and flight-recorder hooks. Do we want EventStream events / logging parity for tests and ops
visibility? (Tests will need *some* observable signal that a table was advertised/installed.)

## Risks / edge cases

- **Restarted incarnation:** a sender using a table built for a previous incarnation of B stamps an
  unknown version → B drops + re-advertises (Q3). Must not crash the inbound stream. Pekko has a
  dedicated spec (`HandshakeShouldDropCompressionTableSpec`) — port an equivalent.
- **Index space:** 16-bit tag index (max 65 535) vs `max = 256` default — comfortable; enforce
  `max <= 65 535`.
- **Quarantine:** stop advertising and `Close(uid)` on quarantine; control stream still pierces
  quarantine for reconciliation (existing Artery invariant).
- **MNTR:** compression changes ref/manifest carriage across the wire — run the multi-node
  Remote/Cluster specs before merge (type-erasure bugs only surface at MNTR runtime).

## Scaffolding delivered in this change (compiles; wire behavior UNCHANGED)

`src/core/Akka.Remote/Artery/Compression/`:
- `CompressionTable.cs` — `CompressionTable<string>` (`Compress`, `Invert`, `Empty`).
- `DecompressionTable.cs` — `DecompressionTable<string>` (`Get`, `Empty`, `Disabled@0xFF`).
- `IInboundCompressions.cs` — the interface + `NoInboundCompressions` no-op (the off-by-default path).
- `CompressionTagCodec.cs` — pure encode/decode HOOKS (`MakeCompressedTag`, `TryBuild*Tag`,
  `TryResolve`) — **not yet called by the hot codec**.
- `CompressionProtocol.cs` — the four advertisement/ack shape records (plain records; not yet
  `IArteryControlMessage`/`[AkkaSerializable]`, so nothing new hits the wire or API approvals yet).

Plus one additive constant `ArteryEnvelopeHeader.CompressedTagMarker = 0x0100_0000` (the non-zero
top-byte marker the decoder already classifies as COMPRESSED). **`ArteryEnvelopeCodec.Encode` still
emits LITERAL; the inbound stage still drops COMPRESSED.** No behavior change.
