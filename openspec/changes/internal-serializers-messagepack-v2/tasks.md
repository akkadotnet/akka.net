## 1. Foundation (no flag infrastructure — design.md Decision 2 as amended 2026-07-18)

- [ ] 1.1 Document the operator binding-override recipe (design.md Decision 3) + per-subsystem marker-interface/serializer-name table (feeds 8.2)
- [ ] 1.2 Reserve serializer-id block 40-79: document `legacy + 40` mapping in code comments and fix the stale "Identifier values from 0 to 40 are reserved" comment in `akka.conf` `serialization-identifiers`
- [x] 1.3 First additive registration proves the pattern — done via ReliableDelivery PR #8409 (`Cluster.conf`: dual registration, binding untouched)
- [ ] 1.4 Update PR #8409's `Cluster.conf` TODO comment (references the withdrawn #8403 `write-bindings` mechanism) to the flip-the-binding plan — fold into the ReliableDelivery flip PR (2.6)

## 2. Subsystem 1 — ReliableDelivery (id 36 -> 76) [lowest risk, first]

- [x] 2.1 `[AkkaSerializable]` wire mirrors for `SequencedMessage`/`Ack`/`Request`/`Resend`/`RegisterConsumer` + durable-queue types — PR #8409 (`ReliableDeliveryMessagePackSerializer.cs`)
- [x] 2.2 Source-generated codec + thin `SerializerV2` wrapper (manifests `"a"`..`"i"` reused verbatim) — PR #8409
- [x] 2.3 `[AkkaEnvelopePayload]` for wrapped user payloads (V1 + V2 inner serializers verified) — PR #8409
- [x] 2.4 Register both ids additively in `Cluster.conf`, binding untouched — PR #8409
- [x] 2.5 Parity/round-trip/cross-read specs (82 scenarios) + legacy-regression (16) + benchmark A/B with payload sizes — PR #8409
- [ ] 2.6 Flip PR: `Cluster.conf` binding `IDeliverySerializable` -> V2 serializer (default), after write-side optimization pass + full-length benchmark job; includes ledger entry (8.1) + runbook row (8.2) + task 1.4's comment fix

## 3. Subsystem 2 — DistributedData (ids 12->52, 11->51) [headline hot-path]

- [ ] 3.1 DTO mirrors for the `ReplicatorMessage` set; hand-written `IAkkaMessagePackFormatter<T>` for `UniqueAddress`/`VersionVector` (design.md Decision 10)
- [ ] 3.2 Preserve/re-evaluate gzip on `Gossip`/`ORSet`/`ORMap`; `OtherMessage` -> `[AkkaEnvelopePayload]`
- [ ] 3.3 `ReplicatedData` CRDT mirrors + delta-op serializers
- [ ] 3.4 Register v2 ids additively; **exact-type `DurableDataEnvelope` pin to the legacy serializer** (MANDATORY — ships in the same PR as the binding flip; design.md Decision 11 LMDB structural finding)
- [ ] 3.5 Golden + cross-read tests, plus durable-read-back proof: write durable store pre-flip, flip the binding, restart, recover successfully via the pin
- [ ] 3.6 Flip PR: binding -> V2 (with 3.4's pin), ledger + runbook

## 4. Subsystems 3-4 — Cluster tools (9, 15, 10; optional 14) + Sharding routing subset (13)

- [ ] 4.1 PubSub (9->49): DTO mirrors, serializer, additive registration, then flip PR
- [ ] 4.2 ClusterClient (15->55): DTO mirrors (reuse PubSub shapes where applicable), additive registration, then flip PR
- [ ] 4.3 ClusterMetrics (10->50): DTO mirrors, additive registration, then flip PR
- [ ] 4.4 ClusterSingleton (14->54, optional/low-value): DTO mirrors for the 4 empty-payload handover messages, if pursued
- [ ] 4.5 Sharding routing subset (13->53): DTO mirrors for `ShardingEnvelope`, `GetShardHome`/`ShardHome`/`HostShard`, handoff set; additive registration, then flip PR
- [ ] 4.6 Sharding: spec proving remember-entities persisted state (`CoordinatorState`, `EntityState`, `EntitiesStarted`/`EntitiesStopped`) stays on the legacy protobuf serializer after the routing flip (design.md Decision 11)

## 5. Subsystem 5 — Cluster core (id 5 -> 45) [last, correctness-critical]

- [ ] 5.1 Heartbeat/HeartbeatRsp DTO mirrors + serializer; measure payload size first (design.md Decision 9)
- [ ] 5.2 Gossip/GossipStatus DTO mirrors (`VectorClock`, `Reachability`, member set) + serializer
- [ ] 5.3 Flip decision: may defer to a post-1.6.0 release if zero-config v1.5->v1.6 rolls are judged more valuable at the time (design.md Decision 6)

## 6. Benchmark acceptance gate

- [ ] 6.1 Add a v2 arm to `ClusterMessageSerializerBenchmarks`, `DDataSerializationBenchmarks`, and the DData CRDT benchmarks (`MemoryDiagnoser` + payload-size column)
- [ ] 6.2 Extend the `RemotePingPong --serializer` harness with real subsystem message shapes; add a DData write-throughput / cluster-formation-time end-to-end check; include an `IBufferWriter` (Artery-path) arm — the `ToBinary` A/B understates V2
- [ ] 6.3 Record per-subsystem protobuf-vs-v2 results (CPU, allocations, payload size per message type); results inform flip timing and optimization targets — subsystems migrate as a unit, no per-message carve-outs (design.md Decision 9, as amended)
- [ ] 6.4 STOP for maintainer review of numbers before any subsystem's shipped binding flips

## 7. Rolling-upgrade + compatibility tests

- [ ] 7.1 Mixed-config spec: node with both serializers registered reads v2; node without v2 registered fails cleanly on a v2 id (documents the roll recipe from design.md Decision 6)
- [ ] 7.2 Legacy-pinned config variant run of the full DData/Sharding/Cluster.Tools/Cluster test suites per flipped subsystem (proves the operator override path stays healthy)
- [ ] 7.3 MNTR interop test: mixed v1.6 nodes — some writing v2 (default), some pinned to legacy via binding override — cross-node interop holds

## 8. Docs + ledger

- [ ] 8.1 `BREAKING_CHANGES_V1.6.md` entry per subsystem flip: default wire-format change + the binding-override recipe for mixed pre-v1.6 rolls
- [ ] 8.2 Operator runbook: the binding-override recipe, per-subsystem override table, and the `sharding`/`distributed-data` durable exclusions
- [ ] 8.3 API-approval baselines updated for new serializer types
- [ ] 8.4 Note in this change's docs (and cross-reference from `messagepack-sourcegen-validation`) that this change supersedes that change's "protobuf wrapper wire formats are not replaced by default" non-goal for the specific subsystems migrated here, while Remote core internals and durable/persisted writes remain non-goals (see proposal.md)
