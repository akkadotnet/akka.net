## Context

This design follows a maintainer-review draft grounded in code read directly from `dev`. The load-bearing facts:

**Read/write dispatch is exactly the shape this design needs** (`src/core/Akka/Serialization/Serialization.cs`):
- **Reads dispatch purely by numeric serializer id.** `Deserialize(byte[], int serializerId, string manifest)` and the V2 `Deserialize(ReadOnlySequence<byte>, id, manifest)` look up `_serializersById[serializerId]` and never consult `serialization-bindings`. So if both the legacy protobuf serializer id and the new MessagePack id are registered on a node, that node can decode either format, regardless of what it writes.
- **Writes dispatch by `serialization-bindings`** (type→serializer-name), resolved in `FindSerializerV2ForType`: exact-type match first (memoized), then *first assignable non-`object` binding wins* — that loop iterates a `ConcurrentDictionary` in arbitrary order (a `// TODO` in the source admits it is not truly most-specific). Implication: a migration re-points the exact existing interface binding key in reference.conf (last-write-wins), rather than adding a competing overlapping binding.
- `Serialization.cs` already stores `SerializerV2` internally; HOCON/V1 serializers are wrapped by `SerializerV1Adapter` via `AdaptSerializer`. Native `SerializerV2` serializers must return a non-empty, non-CLR manifest (`ManifestFor`) — satisfied by reusing the existing manifest tokens.
- Four registration injection points exist: HOCON `serializers`, HOCON `serialization-bindings`, `SerializationSetup.CreateSerializers`, and `SerializationSetup.UseFor`. `SerializationSetup` bindings are applied last and always win.

**Rolling-upgrade capability signal already exists.** `Member.AppVersion` is gossiped (`src/core/Akka.Cluster/Member.cs`; encoded in `ClusterMessageSerializer` Join/Gossip). There is a proven "hold this feature until the whole cluster is homogeneous" pattern — `ClusterEvent.HasMoreThanOneAppVersion` and `AbstractLeastShardAllocationStrategy.IsAGoodTimeToRebalance` refuse to rebalance during a rolling update. The classic remoting handshake carries no capability field (`WireFormats.proto` `AkkaHandshakeInfo` = origin+uid+unused cookie) and DistributedData has no node-version awareness (only CRDT causal versions). The capability channel available to this design is cluster `AppVersion`, not the handshake — and this design chooses **not** to wire a framework-enforced gate to it (Decision 6).

**Assembly dependency direction gates authoring.** `Akka.Serialization.V2.csproj` references core `Akka` and takes the MessagePack 3.1.7 dependency. Core Akka can never reference `Akka.Serialization.V2` (cycle), but every migration-candidate assembly (`Akka.Remote`, `Akka.Cluster`, `Akka.DistributedData`, `Akka.Cluster.Sharding`, `Akka.Cluster.Tools`, `Akka.Cluster.Metrics`) sits downstream of core and can. The generator is syntax-driven (`ForAttributeWithMetadataName`, current-compilation only), so `[AkkaSerializable]` types must be declared in the same assembly as their `[AkkaSerializer]` — this is what forces Decision 10 (per-assembly DTO mirrors, not a shared cross-assembly schema).

**Migration inventory** (all internal ids are `< 100`, reserved per `CustomSerializerSpec.cs`; free low ids before this change: 18-21, 24-35, 37-39):

| Subsystem / serializer | Legacy id | New id | Assembly | Hot-path (steady-state) | Cold | Migration risk |
|---|---|---|---|---|---|---|
| `ReliableDeliverySerializer` | 36 | 76 | Akka.Cluster | `SequencedMessage` (wraps every delivery), `Ack`, `Request`, `Resend` | `RegisterConsumer`, 4 durable-queue types | Low — small flow-control messages; delivery is the designated V2 buffer POC |
| `ReplicatorMessageSerializer` | 12 | 52 | Akka.DistributedData | `Gossip` (gzip), `Status`, `DeltaPropagation`, `DataEnvelope`, `Write`, `Read`, `Changed`, `WriteAck`, `DeltaNack` | `Get*`, `Subscribe`, `DurableDataEnvelope` | Med-High — gzip, `OtherMessage` user-payload nesting, `VersionVector`/`UniqueAddress` |
| `ReplicatedDataSerializer` | 11 | 51 | Akka.DistributedData | CRDT delta ops (`ORSetAdd/Remove/DeltaGroup`, `ORMap*`, counters), full-state CRDTs embedded in `DataEnvelope`/`DeltaPropagation` | 10 Key types | Med-High — gzip on ORSet/ORMap; nested arbitrary user values |
| `DistributedPubSubMessageSerializer` | 9 | 49 | Akka.Cluster.Tools | `Status`, `Delta` (registry gossip, ~1s), `Send`, `SendToAll`, `Publish`, `SendToOneSubscriber` | — | Low-Med — `Address` reuse + user payload wrap |
| `ClusterClientMessageSerializer` | 15 | 55 | Akka.Cluster.Tools | `Heartbeat`, `HeartbeatRsp`, `Send`, `SendToAll`, `Publish` | `Contacts`, `GetContacts`, `ReceptionistShutdown` | Low-Med (reuses PubSub proto shapes) |
| `ClusterSingletonMessageSerializer` | 14 | 54 | Akka.Cluster.Tools | — | all 4 (handover only, empty payloads) | Low but low-value (cold, empty) |
| `ClusterMetricsMessageSerializer` | 10 | 50 | Akka.Cluster.Metrics | `MetricsGossipEnvelope` (~3s) | 5 router-config types | Low-Med |
| `ClusterShardingMessageSerializer` | 13 | 53 | Akka.Cluster.Sharding | `ShardingEnvelope` (wraps every entity message), `GetShardHome`/`ShardHome`/`HostShard`, handoff set | 20+ stats/registration types **+ persisted remember-entities state** (`CoordinatorState`, `EntityState`, `EntitiesStarted/Stopped`) — **excluded from this change**, see Decision 11 | High for the persisted subset; Low-Med for routing |
| `ClusterMessageSerializer` | 5 | 45 | Akka.Cluster | `GossipEnvelope`, `GossipStatus`, `Heartbeat`, `HeartbeatRsp` | `Join`, `Welcome`, `Leave`, `Down`, `InitJoin*` | High — membership correctness; `Gossip` carries the full member set + `VectorClock` + `Reachability` |
| Remote core: `MiscMessageSerializer`(16), `SystemMessageSerializer`(22), `PrimitiveSerializers`(17), `MessageContainerSerializer`(6), `DaemonMsgCreateSerializer`(3) | — | — | Akka.Remote | Watch/`DeathWatchNotification`, RemoteWatcher heartbeat, primitives | most | **Out of scope**, see Decision 12 |

Shared wire fragments reused across these protos: `UniqueAddress` (64-bit uid, `ClusterMessages.proto`), `AddressData`/`ActorRefData` (`ContainerFormats.proto`), `VersionVector`, and the two nesting envelopes `Payload` (`WrappedPayloadSupport`, `src/core/Akka.Remote/Serialization/WrappedPayloadSupport.cs`) and `OtherMessage` (`SerializationSupport.cs` in DData).

## Goals / Non-Goals

**Goals:**

- Migrate the internal cluster/replication/delivery/sharding-routing message serializers from protobuf to source-generated MessagePack V2, writing MessagePack **by default** once each subsystem's flip lands (staged per release), with legacy serializers registered forever for reads.
- Preserve read compatibility unconditionally: every v1.6 node can read protobuf and MessagePack-v2 for any migrated subsystem regardless of what it writes.
- Gate each subsystem's default flip on a measured CPU/allocation/payload-size benchmark result, not a target date.
- Reuse the `messagepack-sourcegen-validation` generator, attributes, and envelope-payload model unmodified.
- Give operators a pure-HOCON opt-out and mixed-version-roll story using only the existing `serialization-bindings` override mechanism — no new knobs.

**Non-Goals:**

- Migrating Remote core internals (`MiscMessageSerializer`, `SystemMessageSerializer`, `PrimitiveSerializers`, `MessageContainerSerializer`, `DaemonMsgCreateSerializer`) — a separate later change; they underpin persistence and the upcoming Artery envelopes.
- Bulk migration of existing durable data. Durable stores become self-describing (LMDB gains a header; Akka.Persistence already stamps ids) so new writes may be MessagePack and old records read as protobuf forever — but no tool rewrites historical records.
- A framework-enforced `AppVersion` or capability-version handshake gate on v2 writes — rollout safety is operator-discipline plus documentation.
- **Any feature-flag surface** — the original `akka.actor.serialization.v2.*` flag design was withdrawn 2026-07-18 (Decision 2); operator control is standard `serialization-bindings` overrides.
- The cross-assembly MessagePack shared-schema contract for de-duplicating `UniqueAddress`/`VersionVector` formatters across subsystem assemblies — demoted to a dedup-only follow-up, not a dependency of this change.
- Changing the wire format of any existing protobuf serializer id in place.

## Decisions

### 1. The binding controls the WRITE side only; both serializers always registered

Verified against `Serialization.cs`: reads are id-dispatched (`_serializersById`), writes are binding-driven. Register both the legacy protobuf id and the new MessagePack id in every subsystem's reference.conf unconditionally. Migration is nothing more than which serializer-name the subsystem's marker-interface `serialization-bindings` entry (e.g. `IReplicatorMessage`, `IDeliverySerializable`, `IClusterShardingSerializable`) points at. Read-side-always-registered is sufficient for a homogeneous v1.6 cluster: every node holds both serializers, so any node decodes either format no matter which it writes. (Caveat: stores that resolve serializers by current binding rather than stored id — see the LMDB finding in Decision 11 — need explicit pins.)

### 2. No feature flag — subsystem defaults flip in reference.conf; operator control via existing binding overrides (amended 2026-07-18, supersedes the original central-hook ruling)

**Decision: there is no flag mechanism at all.** The original ruling (a central HOCON-driven binding-rewrite hook in `Serialization` construction, `akka.actor.serialization.v2.*` keys) was withdrawn after maintainer review of the implementation PR (#8403, closed unmerged): the hook reinvented a capability Akka.NET has always had — `serialization-bindings` entries are ordinary, operator-overridable HOCON — behind a stringly-typed registry living in public config space, with imperative mutation of the binding table at startup.

The replacement is radically simpler:

- **Each subsystem's migration PR flips its own `reference.conf` `serialization-bindings` entry** from the legacy serializer name to the V2 serializer name when that subsystem is deemed ready (benchmark gate cleared, parity proven). Flips are staged per release, one reviewable line each; nothing flips in the same PR that introduces a serializer.
- **The legacy protobuf serializer and its id stay registered unconditionally, forever** — reads dispatch by id, so every v1.6 node decodes both formats regardless of what any node writes.
- **Operator control is the existing mechanism**: overriding the subsystem's `serialization-bindings` entry in `application.conf` pins writes back to the legacy serializer (opt-out, or mixed-version-roll management per Decision 6). One documented recipe replaces all flag machinery.

The swap thus lives entirely in the library that defines the serializer (its own reference.conf), configuration stays declarative, and core `Akka` gains no new code or config surface.

### 3. Operator recipe — pinning a subsystem back to protobuf

```hocon
# application.conf: opt a subsystem out of MessagePack writes (reads of both
# formats always work; remove the override once no longer needed).
akka.actor.serialization-bindings {
  "Akka.Delivery.Internal.IDeliverySerializable, Akka" = reliable-delivery   # legacy protobuf, id 36
}
```

This is standard `serialization-bindings` precedence (user config over reference config) — no new semantics. The docs runbook (tasks 8.2) publishes the per-subsystem marker-interface/serializer-name table so operators can copy the exact line for each subsystem.

### 4. Serializer-id strategy — new ids from a reserved internal block (settled)

**Decision: reserve 40-79 for internal V2/MessagePack ports; mnemonic `v2_id = legacy_id + 40`** (5→45, 9→49, 10→50, 11→51, 12→52, 13→53, 14→54, 15→55, 36→76). This was Open Question 3; the maintainer confirmed both the block and the mnemonic. All stay `< 100` (internal-reserved, per `CustomSerializerSpec.cs`) and well clear of user-generated serializers (120000+, per the `Akka.Serialization.V2` examples). Ids, once shipped and written by any node, are never reused — they may sit in durable DData or journal data even though this change does not itself write v2 bytes into durable storage (Decision 11).

### 5. New serializers are native source-generated V2, translating domain ↔ `[AkkaSerializable]` DTO mirror ↔ MessagePack

The V2 serializer is `AkkaSerializer : SerializerV2` (source-generated), reusing the legacy manifest tokens (e.g. `"N"`, `"HB"`, `"a"`) — this satisfies the non-empty/non-CLR manifest invariant and lets intra-serializer dispatch mirror the protobuf serializer 1:1. Just as the protobuf serializers translate domain object → proto message → bytes, the V2 serializers translate domain object → hand-written `[AkkaSerializable]` DTO mirror → MessagePack bytes. Nested user payloads use `[AkkaEnvelopePayload]` (preserves serializerId+manifest+bytes) — the direct analog of `WrappedPayloadSupport`/`OtherMessage` (Decision 8).

### 6. Rolling-upgrade safety — defaults-forward + documented binding-override recipe, no framework gate (amended 2026-07-18)

**Decision: v1.6 ships with migrated subsystems writing MessagePack by default; mixed-version rolls are managed with the Decision 3 binding override; no framework-owned capability gate, no remoting-handshake change.** The hazard is unchanged: a node with no v2 serializer registered (any pre-v1.6 node) receiving a v2 id → `Cannot find serializer with id [N]`. Consequences under defaults-forward:

- **v1.6↔v1.6 is always safe**: every v1.6 node registers both serializers, so mixed protobuf/v2 writers coexist freely (including nodes where an operator pinned the legacy binding).
- **v1.5→v1.6 rolling upgrades of a flipped subsystem require the documented recipe**: apply the Decision 3 `application.conf` override (write legacy) on v1.6 nodes for the duration of the roll, remove it once every node is ≥ v1.6. This is the same operator action the old flag design required, expressed through a decade-old existing mechanism instead of new machinery.
- **Correctness-critical subsystems can defer**: a subsystem's default flip is a per-release decision (Decision 7 order); Cluster core in particular may hold its flip to a later release if preserving zero-config v1.5→v1.6 rolls is judged more valuable than the perf win at that time.

No framework `AppVersion` gate is wired (application-defined, unreliable as enforcement), and a capability version can't ride the existing handshake without a wire change (out of scope; Decision 12). `HasMoreThanOneAppVersion` remains an informational signal operators can consult mid-roll.

### 7. Migration order — hot-path + low-risk first, durability and correctness-critical last (settled)

**Decision: ReliableDelivery → DistributedData → Cluster tools → Sharding (routing subset) → Cluster core, with Sharding-persisted state and Remote-core internals deferred out of this change.** This was Open Question 4; the maintainer confirmed the order as drafted.

1. **ReliableDelivery (36→76)** — small, self-contained flow-control messages; delivery is the designated V2 buffer POC; no persistence on the hot subset. Lowest risk, real steady-state volume, best first proof.
2. **DistributedData (12→52 then 11→51)** — the headline hot-path and the best perf signal (gossip/delta every interval). Higher risk (gzip, `OtherMessage` user-payload nesting, `VersionVector`). This is where the "very noticeable improvement" mandate is proven or disproven. Requires the LMDB self-describing-header prerequisite (Decision 11) before its flip.
3. **Cluster tools** — PubSub (9→49), ClusterClient (15→55), Metrics (10→50); Singleton (14→54) optional/low-value (cold, empty payloads).
4. **Sharding routing subset (13→53)** — `ShardingEnvelope`, shard-home/handoff. Persisted remember-entities state stays protobuf (Decision 11).
5. **Cluster core (5→45)** — Heartbeat/HeartbeatRsp first (tiny, hot; measure size per Decision 9), then Gossip (complex, membership-critical). Highest correctness sensitivity, so last.
6. **Remote core internals (16/22/17/6/3)** — deferred to a separate later change (Decision 12).

### 8. Nested payloads are serializer boundaries, not re-encoded

`Payload`/`OtherMessage`/`SequencedMessage` carry an inner (serializerId, manifest, bytes) triple owned by whatever serializer wrote the inner value (often a user serializer). The v2 wrapper preserves that triple verbatim via `[AkkaEnvelopePayload]`; user payloads are never re-serialized. This is why the migration is safe even when a wrapper's payload is an application type Akka doesn't own.

### 9. Benchmark acceptance gate, including payload-size tolerance (settled)

Extend existing harnesses to run both encodings per subsystem and compare on the same hardware, same run (mirroring the M5 gate language):

- **Micro (primary gate):** `ClusterMessageSerializerBenchmarks` (`src/benchmark/Akka.Cluster.Benchmarks/Serialization/`), `DDataSerializationBenchmarks` (ShardCount 1/20/100/1000), and the DData CRDT serializer benchmarks (`Akka.Benchmarks/DData/Serializer*Benchmarks.cs`) — add a v2 arm to each. Metrics: ns/op serialize+deserialize, B/op (`MemoryDiagnoser`), and payload size in bytes.
- **End-to-end (secondary gate):** the `RemotePingPong` RealPayload harness already A/B's `--serializer v2|protobuf|msgpack` over the full remoting path via `serialization-bindings` and reports msgs/sec + bytes-on-wire — extend it to carry real subsystem message shapes, and add a DData write-propagation / cluster-formation-time throughput check.
- **Default-on criterion per subsystem:** ≥ ~30-50% lower serialize+deserialize CPU **and** ≥ ~2x lower allocations **and** payload size within tolerance on that subsystem's hottest small message.

**Payload-size tolerance (Open Question 5, settled; amended 2026-07-18):** protobuf is extremely compact (varint field numbers); MessagePack with `[AkkaField]` field-id maps can be larger for tiny messages (map framing + keys — the sourcegen POC logged ~128-130 B for a small message). The maintainer's original ruling allowed ~10% payload growth on tiny hot messages when CPU/allocation wins are substantial, with a per-message-type carve-out for messages failing the gate. **The carve-out is now withdrawn (maintainer ruling, 2026-07-18): subsystems migrate as a unit — every message type a subsystem's serializer handles moves to MessagePack together, with no per-message-type protobuf/MessagePack hybrid.** Rationale: less branching — one write path per subsystem, and a credible path to eventually dropping the protobuf write code (and ultimately the `Google.Protobuf` dependency) instead of maintaining two formats indefinitely. Payload size, CPU, and allocations remain first-class benchmark metrics, measured per message type — but they now inform *when a subsystem's shipped default flips* and *where optimization effort goes* (direct hand-written formatters for hot messages, pooled buffers, span-based payload writes, `IBufferWriter`-path measurement), not which messages migrate.

### 10. Cross-assembly dependency — proceed now with per-assembly formatters (settled)

**Decision: proceed now with per-assembly `[AkkaSerializable]` DTO mirrors plus hand-written `IAkkaMessagePackFormatter<T>` formatters for shared fragments (`UniqueAddress`, `VersionVector`). The cross-assembly shared-schema contract is demoted to a dedup-only follow-up, not a dependency of this change.** This was Open Question 6; the maintainer chose to unblock immediately rather than wait.

- **Unblocked today:** any subsystem whose serializer + `[AkkaSerializable]` DTO mirrors live in one downstream assembly can be generated now — the single-compilation generator handles it, and `Address`/`ActorPath` shared fields use the existing built-in `AddressFormatter`/`ActorPathFormatter`, which are byte-compatible with Artery's control-message wire format (`messagepack-sourcegen-validation` design.md Decision 11). ReliableDelivery and PubSub are unblocked immediately this way.
- **Duplication accepted:** authoring the shared wire fragments (`UniqueAddress` with its 64-bit uid, `VersionVector`, `ActorRefData`, and the generic nesting-envelope schema) once and structurally nesting them across subsystem assemblies is not available yet. The generator is current-compilation-only, so a referenced-assembly `[AkkaSerializable]` type is invisible to it (`messagepack-sourcegen-validation` design.md Decisions 8 and 11). Per-assembly `IAkkaMessagePackFormatter<T>` mirror duplication (extending the existing `AddressFormatter`/`ActorPathFormatter` precedent) is the accepted approach for `UniqueAddress` and `VersionVector` in this change, one hand-written formatter per assembly that needs it.
- A future cross-assembly MessagePack schema contract (the "explicit cross-assembly MessagePack contract" named in `messagepack-sourcegen-validation` design.md Decision 8) would let a generated serializer in assembly B structurally write/read an `[AkkaSerializable]` schema type declared and generated in a referenced assembly A. When it lands, the duplicated formatters in this change become de-duplication candidates, not a correctness problem — the wire format they produce does not change.

### 11. Durable/persisted scope — self-describing formats, migrate going forward (amended 2026-07-18)

**Principle (maintainer): every durable record must signal its own wire format. A record with no format signal is assumed legacy protobuf; records written going forward carry the signal and may be MessagePack.** This replaces the original "durable stays frozen on protobuf forever" framing. It splits cleanly by whether a store is already self-describing:

- **Already stamped (safe today):** Akka.Persistence stamps `(serializerId, manifest)` on every journal event and snapshot. So Sharding's remember-entities journal (`CoordinatorState`/`EntityState`/`EntitiesStarted`/`EntitiesStopped`) and Akka.Delivery's `EventSourcedProducerQueue` durable state already recover old entries by their stored id regardless of the current binding. These may write MessagePack going forward once their subsystem flips, with old protobuf entries reading forever — no extra work, no bulk migration.
- **Not yet stamped (needs the header):** DData's `LmdbDurableStore` writes raw headerless bytes (see below). It gets a self-describing header as a prerequisite, after which it behaves like the stamped stores.

Nothing requires a bulk data-migration tool; the only hard rule is the general one — don't downgrade a node below v1.6 after it has written v2 durable bytes.

**LMDB structural finding (verified in code, 2026-07-18) — and the maintainer's chosen fix: make the store self-describing.** `LmdbDurableStore` stores raw bytes with **no per-record serializer id or manifest**: it resolves ONE serializer at actor startup via the current binding (`FindSerializerForType(typeof(DurableDataEnvelope))`, `LmdbDurableStore.cs:73`) and recovery is `_serializer.FromBinary(bytes, _manifest)` (`LmdbDurableStore.cs:210`). Read-dispatch-by-id never applies to this store — recovery format is whatever the binding currently resolves, so a naive DData flip would feed protobuf bytes to the MessagePack serializer.

**Decision (maintainer, 2026-07-18): give the LMDB record its own format signal instead of freezing it on protobuf.** Prepend each stored value with a small self-describing header — the writing serializer's `(serializerId, manifest)` — exactly what Akka.Persistence already stamps per record. Recovery then dispatches by the stored id, the same way every other id-dispatched store does. **Backward compatibility for existing databases:** a record written before this change has no header, so recovery MUST treat a headerless record as the legacy protobuf `DurableDataEnvelope` (current serializer id + manifest). Disambiguation uses a leading sentinel that cannot begin a valid legacy record — protobuf never emits field number 0, so a leading `0x00` byte unambiguously marks "new self-describing format follows"; any other leading byte is legacy protobuf. Consequence: this is a **prerequisite PR** (`LmdbDurableStore` format upgrade, backward-compatible read of headerless records) that lands BEFORE DData's binding flip; once it ships, DData durable data migrates to MessagePack going forward like everything else — new durable writes carry the header + MessagePack payload, old headerless records keep reading as protobuf forever. No permanent protobuf pin, and no bulk data migration.

**Delivery durable queue — resolved to "accept" under the stamping principle (was an open question).** The adversarial review flagged that `EventSourcedProducerQueue` (the durable queue behind `ShardingProducerController`) persists `MessageSent`/`Confirmed`/`State` (manifests `f`/`g`/`h`) through the same `IDeliverySerializable` binding the delivery flip re-points — so after the flip it would persist id-76 MessagePack into journals/snapshots. Under the amended principle this is simply the "already stamped" case: `PersistenceMessageSerializer.cs:73,190` / `PersistenceSnapshotSerializer.cs:46,79` stamp the id, and recovery via `Serialization.Deserialize(bytes, storedId, manifest)` reads old (id 36) and new (id 76) entries correctly on any v1.6 node. So the whole `IDeliverySerializable` binding flips as a unit — no binding split, no carve-out — and the only unsafe move is the already-forbidden pre-v1.6 downgrade after a v2 durable write. (The rejected alternative was splitting the durable manifest subset `f`-`i` onto a pinned binding; unnecessary given the stamping.)

### 12. Remote core internals — separate later change (settled)

**Decision: `MiscMessageSerializer`(16), `SystemMessageSerializer`(22), `PrimitiveSerializers`(17), `MessageContainerSerializer`(6), and `DaemonMsgCreateSerializer`(3) are explicitly out of scope for this change.** This was Open Question 8; the maintainer confirmed. These serializers underpin persistence directly (event/snapshot envelopes) and the upcoming Artery envelope work (Milestone 4/5), so migrating them belongs to a dedicated later change that can reason about both dependencies together rather than being squeezed into this cluster/replication/delivery-focused migration.

### 13. Coordinated invariants

Any v2 schema carrying the system uid must emit it as **64-bit `long`** (`widen-system-uid-to-64bit`, already merged). Manifest strings are wire+persistence contracts once shipped (`messagepack-sourcegen-validation` design.md Decision 4/Decision 14 in the `serializer-v2` foundation): reuse the legacy tokens and never repurpose them.

### 14. Rejected alternatives

- **Mutate existing serializer ids' wire format in place** — rejected; breaks every mixed cluster and all persisted/durable data (`messagepack-sourcegen-validation` design.md Decision 8.1). Fork with new ids instead (Decision 4).
- **Negotiate serializer capability over the remoting handshake** — rejected; `AkkaHandshakeInfo` has no capability field, and adding one is a wire change out of scope here. Defaults-forward + the documented roll recipe instead (Decision 6).
- **A feature-flag surface of any kind** (the original `akka.actor.serialization.v2.*` central binding-rewrite hook, `SerializationSetup` variants, Akka.Hosting extensions) — **withdrawn 2026-07-18 after implementation review (PR #8403, closed unmerged)**: the central hook reinvented ordinary `serialization-bindings` overriding behind a stringly-typed registry in public config space with imperative binding-table mutation at startup. Replaced by reference.conf default flips owned by each subsystem library + the existing `application.conf` binding-override mechanism (Decisions 2/3).
- **Wait for the cross-assembly MessagePack shared-schema contract before starting** — rejected; per-assembly DTO mirrors and hand-written formatters unblock ReliableDelivery/PubSub/DistributedData immediately, and de-duplication can follow later without a wire change (Decision 10).
- **Per-message-type bindings** to migrate incrementally within one serializer — rejected as fragile (the interface-resolution non-determinism from Decision 1) and, as of the 2026-07-18 amendment to Decision 9, rejected wholesale: subsystems migrate as a unit, with no per-message-type carve-outs even for messages with a payload-size regression. Regressions are answered with serializer optimization, not hybrid bindings.
- **Alternative serialization libraries** — out of scope; MessagePack-CSharp via the `Akka.Serialization.V2` generator is settled (maintainer directive, `messagepack-sourcegen-validation`).

## Risks / Trade-offs

- **[Risk] A pre-v1.6 node receives a v2-id message during a v1.5→v1.6 rolling upgrade of a subsystem whose default has flipped.** → Mitigation: prominent runbook + `BREAKING_CHANGES_V1.6.md` entry documenting the Decision 3 override recipe for the duration of the roll; correctness-critical subsystems may defer their flip a release (Decision 6). Documentation-and-process risk, accepted in exchange for eliminating flag machinery.
- **[Risk] LMDB durable-store recovery follows the CURRENT binding, not a stored serializer id — flipping DData's interface binding would feed protobuf bytes to the MessagePack serializer.** → Mitigation: a prerequisite PR makes `LmdbDurableStore` self-describing (per-record `(serializerId, manifest)` header; headerless records read as legacy protobuf via a `0x00` leading sentinel), landing before DData's flip, with a spec proving pre-header databases still recover after the flip (Decision 11). No permanent pin; durable data migrates going forward.
- **[Risk] Payload-size regression on tiny hot messages increases gossip/heartbeat bandwidth even when CPU improves.** → Mitigation: payload size is a first-class, per-message benchmark metric (Decision 9, as amended); regressions are addressed by optimizing the V2 serializer (direct hand-written formatters, pooled buffers, span-based payload writes) and factored into when the subsystem's shipped default flips — accepted as a cost of uniform migration, since per-message carve-outs were rejected to keep one write path per subsystem and preserve the eventual protobuf exit.
- **[Risk] Per-assembly formatter duplication for `UniqueAddress`/`VersionVector` drifts out of sync across assemblies over time.** → Mitigation: accepted trade-off (Decision 10); the wire format is fixed and reviewed once per formatter, and a future cross-assembly contract can de-duplicate without a wire change.
- **[Trade-off] No framework-enforced version gate means the framework cannot itself prevent an operator from enabling the flag on a mixed-version cluster.** → Accepted trade-off (Decision 6), consistent with the `widen-system-uid-to-64bit` precedent; revisit only if operational experience shows the documentation-only approach is insufficient.

## Migration Plan

1. Land each subsystem's forked serializer **additively** (new id registered, binding untouched) with parity specs and its benchmark A/B — ReliableDelivery already landed this way.
2. When a subsystem clears its benchmark gate and any subsystem-specific prerequisite (e.g. DData's LMDB self-describing-header PR, Decision 11), a follow-up one-line PR flips that subsystem's reference.conf binding to the V2 serializer, together with the `BREAKING_CHANGES_V1.6.md` entry and runbook update.
3. Repeat per subsystem in the Decision 7 order; correctness-critical subsystems may defer their flip to a later release (Decision 6).
4. Operator rollback at any time is the Decision 3 `application.conf` binding override — no data migration needed: durable formats never carried v2 bytes without id-dispatched reads (Decision 11), and both serializers remain registered indefinitely.
5. Long-term (future major version, after Remote-core migrates and durable-store migration tooling exists): delete protobuf write paths and ultimately the `Google.Protobuf` dependency; protobuf reads remain supported throughout v1.6.
