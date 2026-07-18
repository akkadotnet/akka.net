## 1. Feature flag + registration infrastructure

- [x] 1.1 Add `akka.actor.serialization.v2.{enabled, <subsystem>}` config keys + docs (default off) to `reference.conf` — `src/core/Akka/Configuration/akka.conf` (`akka.actor.serialization.v2`: master `enabled = off`, 7 per-subsystem overrides, rolling-upgrade rule in comments)
- [x] 1.2 Add central binding-rewrite hook in `Serialization` construction with deterministic exact-key override (see design.md Decision 2) — `Serialization.ApplyV2WriteBindings` (`src/core/Akka/Serialization/Serialization.cs`) + `SerializationV2WriteBindings` contract type; subsystem descriptor is HOCON-declared under `write-bindings.<subsystem>` (not a static code registry — Decision 2 as amended), applied after HOCON bindings and before `SerializationSetup` (Setup always wins)
- [ ] 1.3 Reserve serializer-id block 40-79; document `legacy + 40` mapping in code comments and design docs (design docs done in this change; code-comment reservation lands with the first forked serializer)
- [x] 1.4 Spec/tests: both serializers registered regardless of flag; read either format by id; write per flag — `src/core/Akka.Tests/Serialization/SerializationV2WriteFlagSpec.cs` (6 specs incl. deserialize-by-id under flag off and on)
- [x] 1.5 Spec/tests: interface binding override is deterministic (exact-key, last-write-wins) — `SerializationV2WriteFlagSpec` (override asserted in both directions: explicit subsystem `on`/`off` vs. master); exact-key overwrite is structural (`_serializerMap[type] = v2`, no competing binding added)

## 2. Subsystem 1 — ReliableDelivery (id 36 -> 76) [lowest risk, first]

- [ ] 2.1 `[AkkaSerializable]` DTO mirrors for `SequencedMessage`/`Ack`/`Request`/`Resend`/`RegisterConsumer` (+ durable-queue types, cold)
- [ ] 2.2 Source-generated `ReliableDeliveryMessagePackSerializer` (reuse manifests `"a"`..`"i"`)
- [ ] 2.3 `[AkkaEnvelopePayload]` for `SequencedMessage`'s wrapped user payload
- [ ] 2.4 Register both ids in `Cluster.conf`; wire `reliable-delivery` flag through the central hook
- [ ] 2.5 Byte-golden tests both formats + cross-read; round-trip; flag on/off write-id assertion

## 3. Subsystem 2 — DistributedData (ids 12->52, 11->51) [headline hot-path]

- [ ] 3.1 DTO mirrors for the `ReplicatorMessage` set; hand-written `IAkkaMessagePackFormatter<T>` for `UniqueAddress`/`VersionVector` (design.md Decision 10)
- [ ] 3.2 Preserve/re-evaluate gzip on `Gossip`/`ORSet`/`ORMap`; `OtherMessage` -> `[AkkaEnvelopePayload]`
- [ ] 3.3 `ReplicatedData` CRDT mirrors + delta-op serializers
- [ ] 3.4 Keep durable (LMDB) writes on protobuf regardless of flag state (design.md Decision 11); register v2 ids; wire `distributed-data` flag
- [ ] 3.5 Golden + cross-read + durable-read-back (v2 flag on, restart, read durable store) tests

## 4. Subsystems 3-4 — Cluster tools (9, 15, 10; optional 14) + Sharding routing subset (13)

- [ ] 4.1 PubSub (9->49): DTO mirrors, `DistributedPubSubMessagePackSerializer`, wire `pub-sub` flag
- [ ] 4.2 ClusterClient (15->55): DTO mirrors (reuse PubSub shapes where applicable), wire `cluster-client` flag
- [ ] 4.3 ClusterMetrics (10->50): DTO mirrors, wire `cluster-metrics` flag
- [ ] 4.4 ClusterSingleton (14->54, optional/low-value): DTO mirrors for the 4 empty-payload handover messages, wire flag if pursued
- [ ] 4.5 Sharding routing subset (13->53): DTO mirrors for `ShardingEnvelope`, `GetShardHome`/`ShardHome`/`HostShard`, handoff set; wire `sharding` flag
- [ ] 4.6 Sharding: confirm remember-entities persisted state (`CoordinatorState`, `EntityState`, `EntitiesStarted`/`EntitiesStopped`) stays on the legacy protobuf binding independent of the `sharding` flag (design.md Decision 11)

## 5. Subsystem 5 — Cluster core (id 5 -> 45) [last, correctness-critical]

- [ ] 5.1 Heartbeat/HeartbeatRsp DTO mirrors + serializer; measure payload size against the tolerance gate first (design.md Decision 9)
- [ ] 5.2 Gossip/GossipStatus DTO mirrors (`VectorClock`, `Reachability`, member set) + serializer
- [ ] 5.3 Wire `cluster` flag through the central hook

## 6. Benchmark acceptance gate

- [ ] 6.1 Add a v2 arm to `ClusterMessageSerializerBenchmarks`, `DDataSerializationBenchmarks`, and the DData CRDT benchmarks (`MemoryDiagnoser` + payload-size column)
- [ ] 6.2 Extend the `RemotePingPong --serializer` harness with real subsystem message shapes; add a DData write-throughput / cluster-formation-time end-to-end check
- [ ] 6.3 Record per-subsystem protobuf-vs-v2 results (CPU, allocations, payload size per message type); results inform the default-on recommendation and optimization targets — subsystems migrate as a unit, no per-message carve-outs (design.md Decision 9, as amended)
- [ ] 6.4 STOP for maintainer review of numbers before any subsystem's shipped default changes

## 7. Rolling-upgrade + compatibility tests

- [ ] 7.1 Mixed-config spec: node with both serializers registered reads v2; node without v2 registered fails cleanly on a v2 id (documents the rolling-upgrade gate from design.md Decision 6)
- [ ] 7.2 Flag-on config variant run of the full DData/Sharding/Cluster.Tools/Cluster test suites, per subsystem
- [ ] 7.3 MNTR rolling-upgrade test: all nodes on v1.6, flag flipped on a subset of subsystems, cross-node interop holds

## 8. Docs + ledger

- [ ] 8.1 `BREAKING_CHANGES_V1.6.md` entry: new rolling-upgrade rule ("all nodes on v1.6 before enabling `akka.actor.serialization.v2.enabled`"); default-off, not a wire break
- [ ] 8.2 Operator runbook: "all nodes v1.6 first, then enable"; document per-subsystem flags and the `sharding`/`distributed-data` durable-write exclusions
- [ ] 8.3 API-approval baselines updated for new serializer types
- [ ] 8.4 Note in this change's docs (and cross-reference from `messagepack-sourcegen-validation`) that this change supersedes that change's "protobuf wrapper wire formats are not replaced by default" non-goal for the specific subsystems migrated here, while Remote core internals and durable/persisted writes remain non-goals (see proposal.md)
