Implementation handoff: [accepted delivery plan](../../../.systematize/plans/builtin-serializers-v2/plan.html). The epic remains [#8675](https://github.com/akkadotnet/akka.net/issues/8675).

## 1. Foundation (amended 2026-10-02: one global opt-in switch, V2 rows are read-only C# table rows)

- [ ] 1.1 Document the operator recipe (design.md Decision 3): how to turn the global switch on, how to pin a binding back to legacy in `application.conf`, and the per-subsystem marker-interface/alias table (feeds 8.2)
- [ ] 1.2 Reserve serializer-id block 40-79: document the `legacy + 40` mapping in code comments, and fix any stale "Identifier values from 0 to 40 are reserved" comment
- [ ] 1.3 First additive registration proves the pattern: ReliableDelivery PR #8409 (not merged yet)
- [ ] 1.4 Rework PR #8409 to the C# table model: read-only row `reliable-delivery-v2` (id 76, empty `UseFor`) in `ClusterSerializers`, delete its `Cluster.conf` rows, and remove the stale text about the withdrawn #8403 `write-bindings` flag
- [x] 1.5 Global opt-in switch implemented by #8724/#8729: HOCON-only `akka.actor.serialization-v2`, default off, with Hosting `WithSerializationV2`; user bindings retain precedence. Issue #8713 remains open for wider integration evidence.
- [x] 1.6 V2 port test kit and table approval infrastructure is merged in #8725: V2PortSpec, Verify golden snapshots, MixedBindingPair and the existing live table approval. Reuse it for subsequent ports; adoption and broader integration evidence remain tracked by #8714.
- [x] 1.7 Shared generator support (#8715) is merged: formatters in collection positions (G-1), native `TimeSpan` and small scalars (G-3).

## 2. Subsystem 1: ReliableDelivery (id 36 -> 76) [lowest risk, first]

- [ ] 2.1 `[AkkaSerializable]` wire mirrors for `SequencedMessage`/`Ack`/`Request`/`Resend`/`RegisterConsumer` + durable-queue types (in PR #8409, `ReliableDeliveryMessagePackSerializer.cs`; not merged)
- [ ] 2.2 Source-generated codec + thin `SerializerV2` wrapper, manifests `"a"`..`"i"` reused verbatim (in PR #8409; not merged)
- [ ] 2.3 Wrapped user payloads ride an `object`-typed property, the serializer boundary (already object-typed in PR #8409; not merged)
- [ ] 2.4 Register the V2 row read-only in `ClusterSerializers`: alias `reliable-delivery-v2`, id 76, empty `UseFor`; no `Cluster.conf` rows (rework in task 1.4)
- [ ] 2.5 Parity/round-trip/cross-read specs, legacy-regression specs, golden V2 bytes, golden legacy bytes, benchmark A/B with payload sizes (in PR #8409; golden bytes still to add)
- [ ] 2.6 Switch-on readiness: write-side optimization pass and a full-length benchmark job, signed off by the maintainer. The PR body carries `## Breaking changes` (task 8.1).

## 3. Subsystem 2: DistributedData (ids 12->52, 11->51) [headline hot-path]

- [ ] 3.1 DTO mirrors for the `ReplicatorMessage` set; one shared `UniqueAddress`/`VersionVector` wire fragment (G-5, design.md Decision 10); `TimeSpan` and collection formatters from #8715
- [ ] 3.2 Keep gzip on `Gossip`/`ORSet`/`ORMap` at wrapper level (maintainer decision, 2026-10-02: these objects can be large); `OtherMessage` -> an `object`-typed property (the serializer boundary); deterministic encoding, because nodes compare SHA-1 digests of `DataEnvelope` bytes
- [ ] 3.3 `ReplicatedData` CRDT mirrors + delta-op serializers; define the `GCounter` value encoding (legacy is platform-endian); generic CRDT types still resolve by name (G-7), gated on `AkkaFeatures.IsDynamicTypeLoadingSupported`
- [ ] 3.4 **Atomic safety prerequisite (#8716): pin `LmdbDurableStore` to the legacy serializer.** Resolve it by id or alias, independently of the global writer binding. Keep the existing headerless format. Integrate this and recovery proof in the Replicator port before either affected DData takeover can merge (latest decision, 2026-10-02).
- [ ] 3.5 Read-only rows `akka-data-replication-v2` (52) and `akka-replicated-data-v2` (51); golden V2 and legacy bytes, cross-read tests; durable-read-back proof: write a legacy LMDB database, enable V2 network bindings, recover and append, then disable V2 and recover every key again; verify the store writes legacy bytes throughout
- [ ] 3.6 Switch-on readiness for DData (after 3.4 is integrated and verified): mixed-binding spec, full benchmark run, `## Breaking changes` in the PR body

## 4. Subsystems 3-4: Cluster tools (9, 15, 14), Metrics (10) and Sharding (13)

- [ ] 4.1 PubSub (9->49): DTO mirrors, serializer, read-only row `akka-pubsub-v2`. First consumer of G-1 (`Address` in collections).
- [ ] 4.2 ClusterClient (15->55): DTO mirrors (reuse PubSub shapes), read-only row `akka-cluster-client-v2`; decide how `use-legacy-serialization` interacts with the switch
- [ ] 4.3 ClusterMetrics (10->50): DTO mirrors, read-only row `akka-cluster-metrics-v2`; needs `float` (G-3); don't copy the private `new Serialization(system)`
- [ ] 4.4 ClusterSingleton (14->54, low value): DTO mirrors for the 4 empty-payload handover messages, read-only row `akka-singleton-v2`, so the whole subsystem moves as a unit
- [ ] 4.5 Sharding as a unit (13->53): DTO mirrors for every manifest, including `ShardingEnvelope`, shard-home/handoff, stats and the persisted-state types; read-only row `akka-sharding-v2`
- [ ] 4.6 Sharding: spec proving remember-entities records recover by their stored serializer id (they are payloads inside `Persistent`, stamped per record). In ddata mode, `shard-*` keys live in LMDB through id 11 and retain the legacy storage serializer independently of DData network bindings (task 3.4).

## 5. Subsystem 5: Cluster core (id 5 -> 45) [last, correctness-critical]

- [ ] 5.1 Heartbeat/HeartbeatRsp DTO mirrors + serializer; measure payload size first (design.md Decision 9). The V2 wrapper has one manifest per heartbeat type; legacy manifests depend on `use-legacy-heartbeat-message`.
- [ ] 5.2 Gossip/GossipStatus DTO mirrors (`VectorClock`, `Reachability`, member set) + serializer, reusing the shared `UniqueAddress` fragment (task 3.1); read-only row `akka-cluster-v2`
- [ ] 5.3 Use the existing global switch for Cluster core; document all legacy binding pins. No per-module flag or new Setup equivalent is required.

## 6. Benchmark acceptance gate

- [ ] 6.1 Add a v2 arm to `ClusterMessageSerializerBenchmarks`, `DDataSerializationBenchmarks`, and the DData CRDT benchmarks (`MemoryDiagnoser` + payload-size column)
- [ ] 6.2 Extend the `RemotePingPong --serializer` harness with real subsystem message shapes; add a DData write-throughput / cluster-formation-time end-to-end check; include an `IBufferWriter` (Artery-path) arm, because the `ToBinary` A/B understates V2
- [ ] 6.3 Record per-subsystem protobuf-vs-v2 results (CPU, allocations, payload size per message type); results inform switch-on readiness and optimization targets. Subsystems migrate as a unit, with no per-message carve-outs (design.md Decision 9, as amended).
- [ ] 6.4 STOP for maintainer review of the numbers (full run, not ShortRun) before the switch is recommended for a subsystem

## 7. Rolling-upgrade + compatibility tests

- [ ] 7.1 Mixed-version spec: a node with both serializers registered reads V2; a node without V2 registered fails cleanly on a V2 id (documents why the switch waits until every node can read V2)
- [ ] 7.2 Run the full DData/Sharding/Cluster.Tools suites per module with the switch on and with it off (proves both paths and the user-pin path stay healthy)
- [ ] 7.3 MNTR interop test: mixed v1.6 nodes, some with the switch on and some off or pinned to legacy; cross-node interop holds

## 8. Docs + PR conventions

- [ ] 8.1 Each serializer PR carries `## Breaking changes`, `## Generator gaps found` and `## Generator changes made` sections in its body (plus `## Decisions needed` when a wire-format choice is open). Record breaking changes in `BREAKING_CHANGES_V1.6.md` in the same PR, as required by AGENTS.md.
- [ ] 8.2 Extend `GETTING_STARTED_V1.6.md` and the detailed `docs/articles/serialization/built-in-v2.md` operator runbook: when to turn the switch on (every node runs a V2-capable version), how to roll back, the binding-pin recipe, the per-subsystem alias table, and the downgrade warning once V2 durable writes exist
- [ ] 8.3 API-approval baselines updated for new serializer types; the module serializer table approval snapshot (`SerializerTableSpec`, PR #8711) shows each V2 row
- [ ] 8.4 Note in this change's docs (and cross-reference from `messagepack-sourcegen-validation`) that this change supersedes that change's "protobuf wrapper wire formats are not replaced by default" non-goal for the subsystems migrated here

## 9. Remote core, Persistence and Streams (added 2026-10-02; epic #8675)

- [ ] 9.1 Remote design note (#8627): wrapper + codec pattern for `Akka.dll` types, a reusable `Exception` formatter (G-8), and the Artery `SelectionEnvelope` parser learning id 46
- [ ] 9.2 MiscMessage (16->56, #8686), SystemMessage (22->62, #8687), MessageContainer (6->46, #8689) and DaemonMsgCreate (3->43, #8690): read-only rows `<legacy-alias>-v2`
- [ ] 9.3 Native `SerializerV2` with the same bytes under the same id, no new id and no MessagePack port: PersistenceMessage (7, #8691) and PersistenceSnapshot (8, #8692). Primitive (17, #8688) is already merged. Other hot Remote serializers (SystemMessage, MessageContainer, cluster heartbeats) may take this route later.
- [ ] 9.4 StreamRef protocol (30->70, #8693): read-only row `akka-stream-ref-v2`; the `SinkRefImpl`/`SourceRefImpl` bindings stay legacy (#8673)
