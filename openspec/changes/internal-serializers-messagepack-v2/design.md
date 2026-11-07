## Context

This design follows a maintainer-review draft grounded in code read directly from `dev`. The load-bearing facts:

**Read/write dispatch is exactly the shape this design needs** (`src/core/Akka/Serialization/Serialization.cs`):
- **Reads dispatch purely by numeric serializer id.** `Deserialize(byte[], int serializerId, string manifest)` and the V2 `Deserialize(ReadOnlySequence<byte>, id, manifest)` look up `_serializersById[serializerId]` and never consult `serialization-bindings`. So if both the legacy protobuf serializer id and the new MessagePack id are registered on a node, that node can decode either format, regardless of what it writes.
- **Writes dispatch by `serialization-bindings`** (type→serializer-name), resolved in `FindSerializerV2ForType`: exact-type match first (memoized), then *first assignable non-`object` binding wins* — that loop iterates a `ConcurrentDictionary` in arbitrary order (a `// TODO` in the source admits it is not truly most-specific). Implication: the V2 row takes over the legacy row's exact interface bindings in the C# module table, rather than adding a competing overlapping binding.
- `Serialization.cs` already stores `SerializerV2` internally; HOCON/V1 serializers are wrapped by `SerializerV1Adapter` via `AdaptSerializer`. Native `SerializerV2` serializers must return a non-empty, non-CLR manifest (`ManifestFor`) — satisfied by reusing the existing manifest tokens.
- Registration injection points (amended 2026-10-02): built-in serializers register as defaults from the modules' C# tables (#8676, #8702); the HOCON `serializers`, `serialization-bindings` and `serialization-identifiers` rows for built-ins are deleted (PR #8711). Users still override through HOCON `serialization-bindings` (by alias), `SerializationSetup.CreateSerializers` and `SerializationSetup.UseFor`. `SerializationSetup` bindings are applied last and always win.

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
| `ClusterShardingMessageSerializer` | 13 | 53 | Akka.Cluster.Sharding | `ShardingEnvelope` (wraps every entity message), `GetShardHome`/`ShardHome`/`HostShard`, handoff set | 20+ stats/registration types **+ persisted remember-entities state** (`CoordinatorState`, `EntityState`, `EntitiesStarted/Stopped`), which migrates with the rest (Decision 11) | Med-High (persisted types are payloads stamped by Akka.Persistence); Low-Med for routing |
| `ClusterMessageSerializer` | 5 | 45 | Akka.Cluster | `GossipEnvelope`, `GossipStatus`, `Heartbeat`, `HeartbeatRsp` | `Join`, `Welcome`, `Leave`, `Down`, `InitJoin*` | High — membership correctness; `Gossip` carries the full member set + `VectorClock` + `Reachability` |
| Remote core: `MiscMessageSerializer`(16→56), `SystemMessageSerializer`(22→62), `MessageContainerSerializer`(6→46), `DaemonMsgCreateSerializer`(3→43); `PrimitiveSerializers`(17) native, same id | 56, 62, 46, 43; 17 stays | Akka.Remote | Watch/`DeathWatchNotification`, RemoteWatcher heartbeat, primitives | most | In scope since 2026-10-02, see Decision 12 |
| `PersistenceMessageSerializer`(7), `PersistenceSnapshotSerializer`(8) | 7, 8 stay (native, same bytes) | Akka.Persistence | journal/snapshot outer records | — | Native route, see Decisions 11 and 12 |
| `StreamRefSerializer` (protocol messages) | 30 | 70 | Akka.Streams | `SequencedOnNext` | — | Low-Med; see #8693 |

Shared wire fragments reused across these protos: `UniqueAddress` (64-bit uid, `ClusterMessages.proto`), `AddressData`/`ActorRefData` (`ContainerFormats.proto`), `VersionVector`, and the two nesting envelopes `Payload` (`WrappedPayloadSupport`, `src/core/Akka.Remote/Serialization/WrappedPayloadSupport.cs`) and `OtherMessage` (`SerializationSupport.cs` in DData).

## Goals / Non-Goals

**Goals:**

- Migrate the internal cluster/replication/delivery/sharding/remote message serializers from protobuf to source-generated MessagePack V2, writing MessagePack when the user turns on the global opt-in switch (off by default), with legacy serializers registered forever for reads.
- Preserve read compatibility unconditionally: every node containing a port can read both its protobuf and MessagePack-v2 formats regardless of what it writes; earlier v1.6 betas may lack that reader.
- Gate the switch-on recommendation for each subsystem on a measured CPU/allocation/payload-size benchmark result, not a target date.
- Reuse the `messagepack-sourcegen-validation` generator, attributes, and envelope-payload model unmodified.
- Give operators one switch for the whole migration, plus the existing `serialization-bindings` override mechanism to pin a subsystem to legacy. The switch is the only new knob.

**Non-Goals:**

- Changing the bytes or ids of Primitive (17), PersistenceMessage (7) and PersistenceSnapshot (8): they get native `SerializerV2` rewrites with identical output (Decision 12).
- Bulk migration of existing durable data. Akka.Persistence payloads recover by stored id; LMDB retains its legacy storage serializer, and persistence outer records keep their bytes (Decision 11).
- A framework-enforced `AppVersion` or capability-version handshake gate on v2 writes — rollout safety is operator-discipline plus documentation.
- **Per-subsystem flag keys or a stringly-typed flag registry** — the original `akka.actor.serialization.v2.*` design was withdrawn 2026-07-18 (PR #8403). One global opt-in switch (#8713) replaces it (Decision 2); per-subsystem control is standard `serialization-bindings` overrides.
- The cross-assembly MessagePack shared-schema contract for de-duplicating `UniqueAddress`/`VersionVector` formatters across subsystem assemblies — demoted to a dedup-only follow-up, not a dependency of this change.
- Changing the wire format of any existing protobuf serializer id in place.

## Decisions

### 1. The binding controls the WRITE side only; both serializers always registered

Verified against `Serialization.cs`: reads are id-dispatched (`_serializersById`), writes are binding-driven. Register both the legacy serializer id and the new V2 id in every subsystem's C# module table (`<Module>Serializers`) unconditionally: the legacy row keeps its bindings, the V2 row is read-only (alias `<legacy-alias>-v2`, empty `UseFor`) until the global switch (Decision 2) is on. Reads need only the id, so an empty `UseFor` still makes the id readable. Migration is nothing more than which row a subsystem's marker-interface binding (e.g. `IReplicatorMessage`, `IDeliverySerializable`, `IClusterShardingSerializable`) belongs to. Read-side-always-registered is sufficient once every participating node contains the port: each holds both serializers and decodes either format regardless of its writer setting. (Stores that resolve by the current binding need a fixed legacy storage serializer; see Decision 11.) No `.conf` rows: HOCON rows for built-in serializers are deleted (#8676, PR #8711).
### 2. One global opt-in switch, off by default (amended 2026-10-02, supersedes the 2026-07-18 no-flag ruling)

**Decision: there is one global V2 switch, off by default.** The 2026-07-18 ruling removed all flag machinery after PR #8403 (closed unmerged): that PR's central HOCON-driven hook reinvented ordinary `serialization-bindings` overriding behind a stringly-typed registry, with imperative mutation of the binding table at startup. The maintainer reversed the "no switch at all" part on 2026-10-02, because moving bindings per subsystem in code makes the whole 1.6 wire change land by accident of release timing. What stays withdrawn: per-subsystem flag keys and a stringly-typed registry.

- **V2 serializers ship as read-only rows** in each module's C# table: alias `<legacy-alias>-v2`, id `legacy + 40`, empty `UseFor`. Every node can read V2 as soon as it runs a release containing them.
- **When the user turns the switch on, each module's V2 row takes over its legacy row's bindings.** Legacy rows stay registered forever for reads.
- **The legacy serializer and its id stay registered unconditionally, forever.** Reads dispatch by id, so every node containing the port decodes both formats regardless of its writer setting.
- **No ceremonial release gate.** Operators turn the switch on once every node runs a version that can read V2. That is docs guidance (Decision 6), not a hard rule in code.
- **User bindings still win.** A user `serialization-bindings` entry (by alias) or `SerializationSetup` binding overrides the table defaults and the switch (Decision 3).
- **Implemented by #8724/#8729:** `akka.actor.serialization-v2` is HOCON-only and defaults off; Hosting exposes `WithSerializationV2`. User bindings still win. There is no separate Setup equivalent.

The swap lives in the library that defines the serializer (its own C# table), and the switch is the only new configuration surface.
### 3. Operator recipe: pinning a subsystem back to legacy

```hocon
# application.conf: keep a subsystem on the legacy serializer even when the global V2
# switch is on (reads of both formats always work; remove the override once no longer needed).
akka.actor.serialization-bindings {
  "Akka.Delivery.Internal.IDeliverySerializable, Akka" = reliable-delivery   # legacy protobuf, id 36
}
```

This is standard `serialization-bindings` precedence (user config over table defaults), with no new semantics. The alias is the legacy alias (`reliable-delivery`); the V2 alias is `reliable-delivery-v2`. The docs runbook (tasks 8.2) publishes the per-subsystem marker-interface/alias table so operators can copy the exact line for each subsystem. Turning the switch off is the other way back.
### 4. Serializer-id strategy — new ids from a reserved internal block (settled)

**Decision: reserve 40-79 for internal V2/MessagePack ports; mnemonic `v2_id = legacy_id + 40`** (5→45, 9→49, 10→50, 11→51, 12→52, 13→53, 14→54, 15→55, 36→76). This was Open Question 3; the maintainer confirmed both the block and the mnemonic. **Aliases (2026-10-02): `<legacy-alias>-v2`** (`reliable-delivery-v2`, `akka-pubsub-v2`, `akka-cluster-v2`), short, and what an operator types in an `application.conf` pin. Primitive (17), PersistenceMessage (7) and PersistenceSnapshot (8) take no V2 id: native same-bytes serializers keep their ids. All stay `< 100` (internal-reserved, per `CustomSerializerSpec.cs`) and well clear of user-generated serializers (120000+, per the `Akka.Serialization.V2` examples). Ids, once shipped and written by any node, are never reused — they may sit in durable DData or journal data even though this change does not itself write v2 bytes into durable storage (Decision 11).

### 5. New serializers are native source-generated V2, translating domain ↔ `[AkkaSerializable]` DTO mirror ↔ MessagePack

The V2 serializer is `AkkaSerializer : SerializerV2` (source-generated), reusing the legacy manifest tokens (e.g. `"N"`, `"HB"`, `"a"`). This satisfies the non-empty/non-CLR manifest invariant and lets intra-serializer dispatch mirror the legacy serializer 1:1. Just as the protobuf serializers translate domain object → proto message → bytes, the V2 serializers translate domain object → hand-written `[AkkaSerializable]` DTO mirror → MessagePack bytes. A nested payload is a property typed `object` (the `[AkkaEnvelopePayload]` attribute was removed in #8518); its wire form is the (serializerId, manifest, bytes) triple, the direct analog of `WrappedPayloadSupport`/`OtherMessage` (Decision 8).

**Core-assembly types.** `Akka.dll` can't reference `Akka.Serialization.V2` (cycle), so types that live there (Delivery, Remote, Persistence wrappers) can't carry `[AkkaSerializable]`. Those ports use the wrapper + codec pattern from PR #8409: a hand-written `internal sealed class XMessagePackSerializer : SerializerV2` owns the id and manifests, converts domain types to `[AkkaSerializable]` wire mirrors, and delegates the bytes to a generated codec that is never registered.

**Native route (maintainer, 2026-10-02).** Primitive (17), PersistenceMessage (7) and PersistenceSnapshot (8) get no MessagePack port and no new id. Each becomes a hand-written native `SerializerV2` that writes the same bytes under the same id (Decisions 11 and 12).
### 6. Rolling-upgrade safety: read-forever registration + switch-on guidance, no framework gate (amended 2026-10-02)

**Decision: the switch is off by default, V2 rows ship read-only, and operators turn the switch on once every node can read V2. There is no framework-owned capability gate and no remoting-handshake change.** The hazard is unchanged: a node with no V2 serializer registered (any node on a release without the row) receiving a V2 id → `Cannot find serializer with id [N]`. Consequences:

- **Nodes containing the same port interoperate in either mode**: both serializers register on those nodes, so mixed legacy/V2 writers coexist (including nodes where an operator pinned the legacy binding or left the switch off).
- **v1.5→v1.6 rolling upgrades need no pin**: the switch is off by default, so v1.6 nodes keep writing legacy bytes that v1.5 nodes read. The operator turns the switch on after the last node reaches a V2-capable version. Docs say so; code doesn't enforce it.
- **Correctness-critical subsystems can sit out**: the switch design (#8713) may offer a per-module opt-out, and Cluster core in particular may stay on legacy longer (Decision 7).

No framework `AppVersion` gate is wired (application-defined, unreliable as enforcement), and a capability version can't ride the existing handshake without a wire change. `HasMoreThanOneAppVersion` remains an informational signal operators can consult mid-roll.
### 7. Migration order: hot-path + low-risk first, durability and correctness-critical last (settled, widened 2026-10-02)

**Decision: ReliableDelivery → DistributedData → Cluster tools and Metrics → Sharding (as a unit) → Cluster core, with Remote core, Persistence and the stream-ref protocol tracked in the same epic (#8675).** This was Open Question 4; the maintainer confirmed the order as drafted. The order governs read-only PRs and the benchmark/sign-off sequence; the global switch moves every deployed Supersedes mapping; sign-off is a recommendation gate, not a separate runtime filter.

1. **ReliableDelivery (36→76)**: small, self-contained flow-control messages; delivery is the designated V2 buffer POC. Lowest risk, real steady-state volume, best first proof.
2. **DistributedData (12→52 then 11→51)**: the headline hot-path and the best perf signal (gossip/delta every interval). Higher risk (gzip, `OtherMessage` user-payload nesting, `VersionVector`). This is where the "very noticeable improvement" mandate is proven or disproven. Requires the LMDB legacy pin and recovery proof (Decision 11) before activating either affected DData mapping.
3. **Cluster tools**: PubSub (9→49), ClusterClient (15→55), Singleton (14→54, cold, empty payloads; in scope so the subsystem moves as a unit); **Metrics (10→50)**.
4. **Sharding (13→53)** as a unit, including the persisted-state types. Persisted remember-entities records are payloads stamped by Akka.Persistence (Decision 11).
5. **Cluster core (5→45)**: Heartbeat/HeartbeatRsp first (tiny, hot; measure size per Decision 9), then Gossip (complex, membership-critical). Highest correctness sensitivity, so last.
6. **Remote core (16, 22, 6, 3), Persistence (7, 8) and Primitive (17), stream-ref protocol (30→70)**: Decision 12.
### 8. Nested payloads are serializer boundaries, not re-encoded

`Payload`/`OtherMessage`/`SequencedMessage` carry an inner (serializerId, manifest, bytes) triple owned by whatever serializer wrote the inner value (often a user serializer). The V2 wrapper preserves that triple verbatim through an `object`-typed property (the serializer boundary; Decision 5); user payloads are never re-serialized. This is why the migration is safe even when a wrapper's payload is an application type Akka doesn't own.
### 9. Benchmark acceptance gate, including payload-size tolerance (settled)

Extend existing harnesses to run both encodings per subsystem and compare on the same hardware, same run (mirroring the M5 gate language). Read-only PRs may quote ShortRun numbers; the maintainer signs off on a full (not ShortRun) run before switch-on testing:

- **Micro (primary gate):** `ClusterMessageSerializerBenchmarks` (`src/benchmark/Akka.Cluster.Benchmarks/Serialization/`), `DDataSerializationBenchmarks` (ShardCount 1/20/100/1000), and the DData CRDT serializer benchmarks (`Akka.Benchmarks/DData/Serializer*Benchmarks.cs`) — add a v2 arm to each. Metrics: ns/op serialize+deserialize, B/op (`MemoryDiagnoser`), and payload size in bytes.
- **End-to-end (secondary gate):** the `RemotePingPong` RealPayload harness already A/B's `--serializer v2|protobuf|msgpack` over the full remoting path via `serialization-bindings` and reports msgs/sec + bytes-on-wire — extend it to carry real subsystem message shapes, and add a DData write-propagation / cluster-formation-time throughput check.
- **Switch-on criterion per subsystem (before the switch is recommended for it):** ≥ ~30-50% lower serialize+deserialize CPU **and** ≥ ~2x lower allocations **and** payload size within tolerance on that subsystem's hottest small message.

**Payload-size tolerance (Open Question 5, settled; amended 2026-07-18):** protobuf is extremely compact (varint field numbers); MessagePack with `[AkkaField]` field-id maps can be larger for tiny messages (map framing + keys — the sourcegen POC logged ~128-130 B for a small message). The maintainer's original ruling allowed ~10% payload growth on tiny hot messages when CPU/allocation wins are substantial, with a per-message-type carve-out for messages failing the gate. **The carve-out is now withdrawn (maintainer ruling, 2026-07-18): subsystems migrate as a unit — every message type a subsystem's serializer handles moves to MessagePack together, with no per-message-type protobuf/MessagePack hybrid.** Rationale: less branching — one write path per subsystem, and a credible path to eventually dropping the protobuf write code (and ultimately the `Google.Protobuf` dependency) instead of maintaining two formats indefinitely. Payload size, CPU, and allocations remain first-class benchmark metrics, measured per message type — but they now inform *when the switch is recommended for a subsystem* and *where optimization effort goes* (direct hand-written formatters for hot messages, pooled buffers, span-based payload writes, `IBufferWriter`-path measurement), not which messages migrate.

### 10. Cross-assembly dependency — proceed now with per-assembly formatters (settled)

**Decision: proceed now with per-assembly `[AkkaSerializable]` DTO mirrors plus hand-written `IAkkaMessagePackFormatter<T>` formatters for shared fragments (`UniqueAddress`, `VersionVector`). The cross-assembly shared-schema contract is demoted to a dedup-only follow-up, not a dependency of this change.** This was Open Question 6; the maintainer chose to unblock immediately rather than wait.

- **Unblocked today:** any subsystem whose serializer + `[AkkaSerializable]` DTO mirrors live in one downstream assembly can be generated now — the single-compilation generator handles it, and `Address`/`ActorPath` shared fields use the existing built-in `AddressFormatter`/`ActorPathFormatter`, which are byte-compatible with Artery's control-message wire format (`messagepack-sourcegen-validation` design.md Decision 11). ReliableDelivery and PubSub are unblocked immediately this way.
- **Duplication accepted:** authoring the shared wire fragments (`UniqueAddress` with its 64-bit uid, `VersionVector`, `ActorRefData`, and the generic nesting-envelope schema) once and structurally nesting them across subsystem assemblies is not available yet. The generator is current-compilation-only, so a referenced-assembly `[AkkaSerializable]` type is invisible to it (`messagepack-sourcegen-validation` design.md Decisions 8 and 11). Per-assembly `IAkkaMessagePackFormatter<T>` mirror duplication (extending the existing `AddressFormatter`/`ActorPathFormatter` precedent) is the accepted approach for `UniqueAddress` and `VersionVector` in this change, one hand-written formatter per assembly that needs it.
- A future cross-assembly MessagePack schema contract (the "explicit cross-assembly MessagePack contract" named in `messagepack-sourcegen-validation` design.md Decision 8) would let a generated serializer in assembly B structurally write/read an `[AkkaSerializable]` schema type declared and generated in a referenced assembly A. When it lands, the duplicated formatters in this change become de-duplication candidates, not a correctness problem — the wire format they produce does not change.

**Amended 2026-10-02:** cross-assembly `[AkkaSerializable]` types work since #8534/#8537 (public types or `InternalsVisibleTo`), so one shared `UniqueAddress`/`VersionVector` fragment is possible. The first DData PR decides between that and per-assembly formatters (G-5, #8678).

### 11. Durable/persisted scope — stored-id recovery or an unchanged storage format

Akka.Persistence stamps the serializer id and manifest of payloads inside events and snapshots. Delivery producer-queue state and Sharding remember-entities journal state may therefore mix old and new payload formats while recovering each record by its stored id. Turning V2 off only changes subsequent writes: binaries without the relevant V2 reader cannot recover already stored V2 records, including earlier v1.6 betas without that port.

Several persistence plugins and LocalSnapshotStore do not stamp the serializer of the outer record. PersistenceMessage (7) and PersistenceSnapshot (8) therefore retain their existing bytes and ids through native SerializerV2 implementations (#8691/#8692).

**Latest LMDB decision (#8716, 2026-10-02): always resolve the legacy storage serializer by id or alias.** LmdbDurableStore stores raw bytes without a per-record id. Resolving it through the network type binding would read old protobuf bytes with the V2 reader after enablement. Pinning its serializer keeps storage stable while DData uses V2 on the wire. The pin and an off/on/off restart-and-recovery test must integrate before either affected DData takeover merges. There is no new record header or bulk migration. This supersedes the earlier July header proposal.

Delivery's entire IDeliverySerializable binding moves together, including durable manifests f-i. Real serialized journal and LocalSnapshotStore tests must prove legacy recovery, V2 writes, and subsequent recovery with the switch disabled; nonserializing in-memory journals do not establish this guarantee.

### 12. Remote core internals: now in scope, tracked in the epic (amended 2026-10-02)

**Decision: Remote core is in scope.** The original ruling deferred `MiscMessageSerializer`(16), `SystemMessageSerializer`(22), `PrimitiveSerializers`(17), `MessageContainerSerializer`(6) and `DaemonMsgCreateSerializer`(3) to a later change because they underpin persistence and the Artery envelope work. That work is now the epic #8675, with design notes in #8627 and one issue per serializer (#8686-#8690). Persistence (7, 8) and the stream-ref protocol (30→70, #8693) are in the epic too.

- **MiscMessage (16→56), SystemMessage (22→62), MessageContainer (6→46), DaemonMsgCreate (3→43)** are MessagePack ports with read-only rows, using the wrapper + codec pattern because their types live in `Akka.dll` (Decision 5).
- **Primitive (17), PersistenceMessage (7) and PersistenceSnapshot (8)** take the native route: hand-written native `SerializerV2`, same bytes, same id, no new id (#8688, #8691, #8692). Primitive's format is already minimal, so a MessagePack port saves nothing; the gain is skipping the V1 adapter's `byte[]` and copy. Persistence's outer records are read back by type binding in several plugins (Decision 11), so their bytes can't change under the same binding.
- **Other hot Remote serializers** (SystemMessage, MessageContainer, cluster heartbeats) may take the native route later. That is an option, not a change to their issues' scope.
- **Artery** parses the legacy `SelectionEnvelope` proto directly (`ArteryInboundProcessingStage.cs`); the MessageContainer port must teach that parser to recognize id 46.
- **Exceptions** need a reusable `Exception` formatter that mirrors `ExceptionSupport` (G-8).
### 13. Coordinated invariants

Any v2 schema carrying the system uid must emit it as **64-bit `long`** (`widen-system-uid-to-64bit`, already merged). Manifest strings are wire+persistence contracts once shipped (`messagepack-sourcegen-validation` design.md Decision 4/Decision 14 in the `serializer-v2` foundation): reuse the legacy tokens and never repurpose them.

### 14. Rejected alternatives

- **Mutate existing serializer ids' wire format in place** — rejected; breaks every mixed cluster and all persisted/durable data (`messagepack-sourcegen-validation` design.md Decision 8.1). Fork with new ids instead (Decision 4), or keep the bytes identical under the same id (native route, Decision 12).
- **Negotiate serializer capability over the remoting handshake** — rejected; `AkkaHandshakeInfo` has no capability field, and adding one is a wire change out of scope here. The off-by-default switch plus switch-on guidance in the docs instead (Decision 6).
- **Per-subsystem flag keys or a stringly-typed flag registry** (the original `akka.actor.serialization.v2.*` central binding-rewrite hook, Akka.Hosting extensions per subsystem) — **withdrawn 2026-07-18 after implementation review (PR #8403, closed unmerged)**. One global opt-in switch (#8713) and the existing `application.conf` binding-override mechanism replace it (Decisions 2/3).
- **Wait for the cross-assembly MessagePack shared-schema contract before starting** — rejected; per-assembly DTO mirrors and hand-written formatters unblock ReliableDelivery/PubSub/DistributedData immediately, and de-duplication can follow later without a wire change (Decision 10).
- **Per-message-type bindings** to migrate incrementally within one serializer — rejected as fragile (the interface-resolution non-determinism from Decision 1) and, as of the 2026-07-18 amendment to Decision 9, rejected wholesale: subsystems migrate as a unit, with no per-message-type carve-outs even for messages with a payload-size regression. Regressions are answered with serializer optimization, not hybrid bindings.
- **No global switch: cut over by moving each module's bindings in its C# table** (the 2026-09-30 direction) — reversed 2026-10-02: it ties each wire change to a release instead of to an operator decision.
- **Alternative serialization libraries** — out of scope; MessagePack-CSharp via the `Akka.Serialization.V2` generator is settled (maintainer directive, `messagepack-sourcegen-validation`).

## Risks / Trade-offs

- **[Risk] A node without the V2 row receives a V2-id message because an operator turned the switch on before every node could read V2.** → Mitigation: the switch is off by default; the runbook and `## Breaking changes` PR sections say to turn it on only once every node runs a V2-capable version (Decision 6). Documentation-and-process risk, accepted in exchange for eliminating a code-enforced gate.
- **[Risk] LMDB durable-store recovery follows the CURRENT binding, not a stored serializer id — moving DData's interface binding would feed protobuf bytes to the MessagePack serializer.** → Mitigation: the latest #8716 decision pins LMDB to the legacy storage serializer, with recovery tests before either affected DData takeover merges (Decision 11).
- **[Risk] Payload-size regression on tiny hot messages increases gossip/heartbeat bandwidth even when CPU improves.** → Mitigation: payload size is a first-class, per-message benchmark metric (Decision 9, as amended); regressions are addressed by optimizing the V2 serializer (direct hand-written formatters, pooled buffers, span-based payload writes) and factored into when the switch is recommended for the subsystem — accepted as a cost of uniform migration, since per-message carve-outs were rejected to keep one write path per subsystem and preserve the eventual protobuf exit.
- **[Risk] Per-assembly formatter duplication for `UniqueAddress`/`VersionVector` drifts out of sync across assemblies over time.** → Mitigation: accepted trade-off (Decision 10); the wire format is fixed and reviewed once per formatter, and a future cross-assembly contract can de-duplicate without a wire change.
- **[Trade-off] No framework-enforced version gate means the framework cannot itself prevent an operator from turning the switch on in a mixed-version cluster.** → Accepted trade-off (Decision 6), consistent with the `widen-system-uid-to-64bit` precedent; revisit only if operational experience shows the documentation-only approach is insufficient.
- **[Risk] Persistence outer records are read back by type binding in several plugins.** → Mitigation: native same-bytes serializers under the same ids (Decisions 11 and 12); no format change for those records.

## Migration Plan

1. Land each subsystem's forked serializer **additively** as a read-only row in its module's C# table (new id registered, empty `UseFor`, no `.conf` rows, legacy bindings untouched) with parity specs, golden V2 and legacy bytes, and its benchmark A/B. PR bodies carry `## Breaking changes`, `## Generator gaps found` and `## Generator changes made`. ReliableDelivery (PR #8409) is the first; it needs rework to the table model.
2. Primitive (17) is already native. Rewrite PersistenceMessage (7) and PersistenceSnapshot (8) with identical bytes and ids.
3. Reuse the merged generator work (#8715) and global switch (#8724/#8729). Reuse the merged #8725 port test kit (#8714); integrate the LMDB legacy pin (#8716) with DData.
4. When a subsystem clears the benchmark gate and any prerequisite (for DData, the LMDB legacy pin and recovery tests), the maintainer signs off and the docs recommend the switch for it. The switch moves every deployed Supersedes mapping, including ports introduced by a package upgrade. Correctness and durability gate the mapping's merge; benchmark sign-off gates the recommendation to enable it.
5. Operator writer rollback is turning the switch off and restarting the affected actor systems or the Decision 3 `application.conf` binding pin. No data migration is needed: durable formats never carry V2 bytes without id-dispatched reads (Decision 11), and both serializers remain registered indefinitely.
6. Long-term (future major version, once durable-store migration tooling exists): delete protobuf write paths and ultimately the `Google.Protobuf` dependency; legacy reads remain supported throughout v1.6.
