## Context

This design follows a maintainer-review draft grounded in code read directly from `dev`. The load-bearing facts:

**Read/write dispatch is exactly the shape this design needs** (`src/core/Akka/Serialization/Serialization.cs`):
- **Reads dispatch purely by numeric serializer id.** `Deserialize(byte[], int serializerId, string manifest)` and the V2 `Deserialize(ReadOnlySequence<byte>, id, manifest)` look up `_serializersById[serializerId]` and never consult `serialization-bindings`. So if both the legacy protobuf serializer id and the new MessagePack id are registered on a node, that node can decode either format, regardless of what it writes.
- **Writes dispatch by `serialization-bindings`** (type→serializer-name), resolved in `FindSerializerV2ForType`: exact-type match first (memoized), then *first assignable non-`object` binding wins* — that loop iterates a `ConcurrentDictionary` in arbitrary order (a `// TODO` in the source admits it is not truly most-specific). Implication: the flag must re-point the exact existing interface binding key deterministically (last-write-wins via `AddSerializationMap`/`_serializerMap[type]=`), not add a competing overlapping binding.
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

- Migrate the internal cluster/replication/delivery/sharding-routing message serializers from protobuf to source-generated MessagePack V2, behind a default-off write-side flag.
- Preserve rolling-upgrade safety: every v1.6 node can read protobuf and MessagePack-v2 for any migrated subsystem regardless of what it writes.
- Gate each subsystem's default-on transition on a measured CPU/allocation/payload-size benchmark result, not a target date.
- Reuse the `messagepack-sourcegen-validation` generator, attributes, and envelope-payload model unmodified.
- Give operators a pure-HOCON, per-subsystem rollout surface.

**Non-Goals:**

- Migrating Remote core internals (`MiscMessageSerializer`, `SystemMessageSerializer`, `PrimitiveSerializers`, `MessageContainerSerializer`, `DaemonMsgCreateSerializer`) — a separate later change; they underpin persistence and the upcoming Artery envelopes.
- Migrating durable/persisted state: DData LMDB storage and Sharding's remember-entities journal keep protobuf writes in this change.
- A framework-enforced `AppVersion` or capability-version handshake gate on v2 writes — rollout safety is operator-discipline plus documentation.
- A `SerializationSetup`-based or Akka.Hosting-based flag surface — HOCON only in this change; Hosting may wrap the HOCON flag in a later change.
- The cross-assembly MessagePack shared-schema contract for de-duplicating `UniqueAddress`/`VersionVector` formatters across subsystem assemblies — demoted to a dedup-only follow-up, not a dependency of this change.
- Changing the wire format of any existing protobuf serializer id in place.

## Decisions

### 1. Flag controls the WRITE side only; both serializers always registered

Verified against `Serialization.cs`: reads are id-dispatched (`_serializersById`), writes are binding-driven. Register both the legacy protobuf id and the new MessagePack id in every subsystem's reference.conf unconditionally. The flag only rewrites the `serialization-bindings` entry for the subsystem's marker interface (e.g. `IReplicatorMessage`, `IDeliverySerializable`, `IClusterShardingSerializable`) from the legacy serializer-name to the v2 serializer-name. Read-side-always-registered is sufficient for a homogeneous v1.6 cluster: every node holds both serializers, so any node decodes either format no matter which it writes.

### 2. Flag mechanism — central HOCON binding-rewrite hook (settled)

**Decision: a single central code hook in `Serialization` construction, driven purely by HOCON.** This was Open Question 1 in the design draft; the maintainer selected the central-hook option. `SerializationSetup`-based and Akka.Hosting-based flag surfaces are rejected **for now** — `SerializationSetup` would require code changes per application to flip a subsystem (defeating the "operator turns a knob" goal), and an Akka.Hosting extension is blocked on Hosting being inlined into this repository (`messagepack-sourcegen-validation` tasks.md 8.8, still open). Akka.Hosting can wrap the HOCON flag with a typed extension method later without changing the underlying mechanism.

Because HOCON can't branch and the interface-resolution loop is non-deterministic (Decision 1), the flip is applied by a single central code hook that runs during `Serialization` construction (`Serialization.ApplyV2WriteBindings`), after the HOCON `serialization-bindings` are installed and before `SerializationSetup` bindings (Setup always wins). Each subsystem contributes its descriptor **declaratively in its own reference configuration** under `akka.actor.serialization.v2.write-bindings.<subsystem>` (type FQCN → v2 serializer config name) — not via a static code registry, which core Akka couldn't host without referencing downstream assemblies and which would be process-global rather than per-`ActorSystem`. The `<subsystem>` key doubles as the flag name. For each effectively-on subsystem the hook re-points each declared binding deterministically (exact-key overwrite of `_serializerMap`, avoiding the arbitrary-order hazard described in Decision 1, and deliberately bypassing the `log-serializer-override-on-start` warning for this operator-requested flip). This keeps the operator surface pure-HOCON and requires no code call from subsystems.

### 3. HOCON shape — global flag + per-subsystem override

```hocon
akka.actor.serialization.v2 {
  # Master write-side switch for internal MessagePack serializers.
  # off (default) = write legacy protobuf; both formats are always READ.
  # Only flip to on AFTER every node in the cluster is on v1.6 with the
  # v2 serializers registered (see Decision 6).
  enabled = off

  # Per-subsystem overrides. An explicit on/off always wins over `enabled`,
  # in both directions; empty ("") inherits the master switch.
  reliable-delivery = ""
  distributed-data  = ""
  pub-sub           = ""
  cluster-client    = ""
  cluster-metrics   = ""
  sharding          = ""   # routing subset only
  cluster           = ""

  # INTERNAL: subsystems declare their write-side rebinding here in their own
  # reference config (type FQCN -> v2 serializer name); see Decision 2.
  write-bindings {}
}
```

Inheritance is resolved in code (`SerializationV2WriteBindings.IsEnabledFor`: unset/empty → master switch), **not** via HOCON `${...}` substitution — Akka.NET's parser resolves substitutions at parse time within a single document, before user config is merged over the reference config, so a reference-config substitution would freeze at `off` and never observe a user's `enabled = on` override.

### 4. Serializer-id strategy — new ids from a reserved internal block (settled)

**Decision: reserve 40-79 for internal V2/MessagePack ports; mnemonic `v2_id = legacy_id + 40`** (5→45, 9→49, 10→50, 11→51, 12→52, 13→53, 14→54, 15→55, 36→76). This was Open Question 3; the maintainer confirmed both the block and the mnemonic. All stay `< 100` (internal-reserved, per `CustomSerializerSpec.cs`) and well clear of user-generated serializers (120000+, per the `Akka.Serialization.V2` examples). Ids, once shipped and written by any node, are never reused — they may sit in durable DData or journal data even though this change does not itself write v2 bytes into durable storage (Decision 11).

### 5. New serializers are native source-generated V2, translating domain ↔ `[AkkaSerializable]` DTO mirror ↔ MessagePack

The V2 serializer is `AkkaSerializer : SerializerV2` (source-generated), reusing the legacy manifest tokens (e.g. `"N"`, `"HB"`, `"a"`) — this satisfies the non-empty/non-CLR manifest invariant and lets intra-serializer dispatch mirror the protobuf serializer 1:1. Just as the protobuf serializers translate domain object → proto message → bytes, the V2 serializers translate domain object → hand-written `[AkkaSerializable]` DTO mirror → MessagePack bytes. Nested user payloads use `[AkkaEnvelopePayload]` (preserves serializerId+manifest+bytes) — the direct analog of `WrappedPayloadSupport`/`OtherMessage` (Decision 8).

### 6. Rolling-upgrade safety — default-off + operator-discipline docs, no framework gate (settled)

**Decision: rely on default-off + `AppVersion`-uniformity documentation. No framework-owned serializer-capability version, no remoting-handshake change.** This was Open Question 2; the maintainer chose operator discipline over a framework-enforced gate. The hazard is a node that has no v2 serializer registered (any pre-v1.6 node) receiving a v2 id → `Cannot find serializer with id [N]`. Once every node is v1.6, all hold both serializers, so:

- flipping the flag can itself be rolled gradually — mixed protobuf/v2 writers coexist, since all v1.6 readers handle both;
- the hard precondition is only that the v1.5→v1.6 upgrade has *completed* before any node writes v2.

This mirrors the `widen-system-uid-to-64bit` precedent exactly: widen the type/register the serializer everywhere now, keep the risky behavior (writing wide uids / writing v2) default-off, flip behind config only after the whole cluster is on v1.6. `HasMoreThanOneAppVersion`-style checks remain available to operators as an informational signal (the same one sharding already uses to gate rebalance) but this change does not wire the framework to refuse writes based on it — `AppVersion` is application-defined and not a reliable enforcement mechanism, and a framework-owned capability version big enough to be reliable can't ride the existing remoting handshake without a wire change, which is out of scope here (Decision 12 covers Remote-core deferral generally).

### 7. Migration order — hot-path + low-risk first, durability and correctness-critical last (settled)

**Decision: ReliableDelivery → DistributedData → Cluster tools → Sharding (routing subset) → Cluster core, with Sharding-persisted state and Remote-core internals deferred out of this change.** This was Open Question 4; the maintainer confirmed the order as drafted.

1. **ReliableDelivery (36→76)** — small, self-contained flow-control messages; delivery is the designated V2 buffer POC; no persistence on the hot subset. Lowest risk, real steady-state volume, best first proof.
2. **DistributedData (12→52 then 11→51)** — the headline hot-path and the best perf signal (gossip/delta every interval). Higher risk (gzip, `OtherMessage` user-payload nesting, `VersionVector`). This is where the "very noticeable improvement" mandate is proven or disproven. Durable (LMDB) writes stay protobuf in this change (Decision 11).
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

### 11. Durable/persisted scope — excluded from this change (settled)

**Decision: DData-LMDB and Sharding's remember-entities persisted state keep protobuf writes in this change (read-old-forever); only the ephemeral remote-gossip subset migrates.** This was Open Question 7; the maintainer confirmed the scope as drafted. Persistence needs the strictest compatibility rule (`messagepack-sourcegen-validation` design.md Decision 8.1): historical events, snapshots, and durable CRDT/journal state are durable wire contracts read forever. Migrating durable writes to v2 needs a separate migration tool and operational process, which is out of scope here. Concretely:

- `DurableDataEnvelope` (DData LMDB durable store) stays on the legacy `ReplicatorMessageSerializer`/`ReplicatedDataSerializer` protobuf path even after `distributed-data` is flagged on for the ephemeral gossip/delta traffic that shares those same serializer classes; the durable-store write path is pinned to the legacy binding independent of the flag.
- Sharding's `CoordinatorState`, `EntityState`, `EntitiesStarted`/`EntitiesStopped` (remember-entities journal) stay on the legacy `ClusterShardingMessageSerializer` protobuf path; only the routing/handoff subset (`ShardingEnvelope`, `GetShardHome`/`ShardHome`/`HostShard`) is eligible for the `sharding` flag.

### 12. Remote core internals — separate later change (settled)

**Decision: `MiscMessageSerializer`(16), `SystemMessageSerializer`(22), `PrimitiveSerializers`(17), `MessageContainerSerializer`(6), and `DaemonMsgCreateSerializer`(3) are explicitly out of scope for this change.** This was Open Question 8; the maintainer confirmed. These serializers underpin persistence directly (event/snapshot envelopes) and the upcoming Artery envelope work (Milestone 4/5), so migrating them belongs to a dedicated later change that can reason about both dependencies together rather than being squeezed into this cluster/replication/delivery-focused migration.

### 13. Coordinated invariants

Any v2 schema carrying the system uid must emit it as **64-bit `long`** (`widen-system-uid-to-64bit`, already merged). Manifest strings are wire+persistence contracts once shipped (`messagepack-sourcegen-validation` design.md Decision 4/Decision 14 in the `serializer-v2` foundation): reuse the legacy tokens and never repurpose them.

### 14. Rejected alternatives

- **Mutate existing serializer ids' wire format in place** — rejected; breaks every mixed cluster and all persisted/durable data (`messagepack-sourcegen-validation` design.md Decision 8.1). Fork with new ids instead (Decision 4).
- **Negotiate serializer capability over the remoting handshake** — rejected; `AkkaHandshakeInfo` has no capability field, and adding one is a wire change out of scope here. Use default-off + docs instead (Decision 6).
- **`SerializationSetup`-based or Akka.Hosting-based flag surface** — rejected for this change; needs application code changes per subsystem flip (`SerializationSetup`) or is blocked on Hosting inlining (Akka.Hosting). HOCON-only for now (Decision 2).
- **Wait for the cross-assembly MessagePack shared-schema contract before starting** — rejected; per-assembly DTO mirrors and hand-written formatters unblock ReliableDelivery/PubSub/DistributedData immediately, and de-duplication can follow later without a wire change (Decision 10).
- **Per-message-type bindings** to migrate incrementally within one serializer — rejected as fragile (the interface-resolution non-determinism from Decision 1) and, as of the 2026-07-18 amendment to Decision 9, rejected wholesale: subsystems migrate as a unit, with no per-message-type carve-outs even for messages with a payload-size regression. Regressions are answered with serializer optimization, not hybrid bindings.
- **Alternative serialization libraries** — out of scope; MessagePack-CSharp via the `Akka.Serialization.V2` generator is settled (maintainer directive, `messagepack-sourcegen-validation`).

## Risks / Trade-offs

- **[Risk] A pre-v1.6 node receives a v2-id message after an operator enables the flag too early.** → Mitigation: default-off, explicit runbook requiring full v1.6 rollout first (Decision 6); no automatic enforcement, so this is a documentation-and-process risk, not a code risk that can be fully closed in this change.
- **[Risk] The non-deterministic binding-resolution loop in `FindSerializerV2ForType` could apply the flag inconsistently if the central hook doesn't use exact-key overwrite.** → Mitigation: Decision 1/2 require exact-key, last-write-wins overwrite of the marker-interface binding, not a competing overlapping binding.
- **[Risk] Payload-size regression on tiny hot messages increases gossip/heartbeat bandwidth even when CPU improves.** → Mitigation: payload size is a first-class, per-message benchmark metric (Decision 9, as amended); regressions are addressed by optimizing the V2 serializer (direct hand-written formatters, pooled buffers, span-based payload writes) and factored into when the subsystem's shipped default flips — accepted as a cost of uniform migration, since per-message carve-outs were rejected to keep one write path per subsystem and preserve the eventual protobuf exit.
- **[Risk] Per-assembly formatter duplication for `UniqueAddress`/`VersionVector` drifts out of sync across assemblies over time.** → Mitigation: accepted trade-off (Decision 10); the wire format is fixed and reviewed once per formatter, and a future cross-assembly contract can de-duplicate without a wire change.
- **[Trade-off] No framework-enforced version gate means the framework cannot itself prevent an operator from enabling the flag on a mixed-version cluster.** → Accepted trade-off (Decision 6), consistent with the `widen-system-uid-to-64bit` precedent; revisit only if operational experience shows the documentation-only approach is insufficient.

## Migration Plan

1. Land flag infrastructure and the reserved id block with no subsystem wired to it yet (nothing observable changes).
2. Land ReliableDelivery's forked serializer, wire its subsystem flag, and run its benchmark gate. Do not flip the shipped default; this only proves the mechanism end-to-end.
3. Repeat per subsystem in the Decision 7 order, each gated independently on its own benchmark results (Decision 9).
4. Each subsystem's default stays `off` in the shipped reference.conf regardless of gate results in this change; flipping any subsystem's shipped default to `on` is a separate, later decision reviewed against the accumulated benchmark evidence.
5. Rollback for an operator who already enabled a subsystem's flag is a config change back to `off` — no data migration is needed because durable formats never carried v2 bytes (Decision 11) and both serializers remain registered for reads indefinitely.
