## Why

Akka.NET 1.6 makes `SerializerV2` canonical and ships source-generated MessagePack serializers (`Akka.Serialization.V2`, MessagePack-CSharp 3.1.7) validated by `messagepack-sourcegen-validation` (Milestone 3). The internal cluster/replication/delivery/sharding message serializers are still hand-written protobuf (`SerializerWithStringManifest` over `Google.Protobuf`). These run on the hottest steady-state paths in a cluster — gossip, deltas, heartbeats, delivery flow-control, shard routing — and are allocation-heavy (`ToBinary → byte[]` per message, proto object graphs). Migrating them to the validated MessagePack V2 generator should cut CPU and allocations on those paths. Per the maintainer: *if we don't see a very noticeable improvement, we're doing something wrong* — measured improvement is the acceptance gate, not an afterthought.

The migration must be rolling-upgrade safe and reversible: a mixed cluster must never receive bytes it can't decode, and operators must be able to turn it on subsystem-by-subsystem.

## What Changes

- Add a global write-side feature flag `akka.actor.serialization.v2.enabled` plus per-subsystem overrides (`reliable-delivery`, `distributed-data`, `pub-sub`, `cluster-client`, `cluster-metrics`, `sharding`, `cluster`), implemented as a single central HOCON-driven binding-rewrite hook that runs during `Serialization` construction. Pure config surface — no `SerializationSetup` variant and no Akka.Hosting extension in this change (Hosting can wrap the HOCON flag later).
- For each migrated subsystem, add a **forked** native V2 (source-generated MessagePack) serializer with a new serializer id drawn from the reserved internal block **40-79** (mnemonic: `v2_id = legacy_id + 40`). The legacy protobuf serializer and its id are retained, never mutated in place.
- **Both** serializers (legacy protobuf id + new MessagePack id) are always registered on every node, regardless of flag state. Reads dispatch purely by numeric serializer id, so any v1.6 node decodes either format no matter what it writes; only the type→serializer write-side **binding** flips when a subsystem's flag is on.
- Reuse existing manifest tokens on each V2 serializer so intra-serializer dispatch mirrors its protobuf counterpart 1:1.
- Migration order (hot-path + low-risk first, durability and correctness-critical last): ReliableDelivery (36→76) → DistributedData (12→52, 11→51) → Cluster tools (PubSub 9→49, ClusterClient 15→55, Metrics 10→50; Singleton 14→54 optional/low-value) → Sharding routing subset (13→53) → Cluster core (5→45).
- Default is **off**. Rolling-upgrade safety is enforced by default-off + operator-discipline documentation ("all nodes on v1.6 before enabling", mirroring the `widen-system-uid-to-64bit` precedent) — **not** by a new framework `AppVersion` gate or a remoting-handshake capability field.
- Add a benchmark acceptance gate: extend `ClusterMessageSerializerBenchmarks`, `DDataSerializationBenchmarks`, the DData CRDT serializer benchmarks, and the `RemotePingPong --serializer` harness to A/B protobuf vs. MessagePack-v2 per subsystem, measuring CPU, allocations, and payload size on the same hardware, same run. Benchmark results govern *when* a subsystem's shipped default flips to on and where optimization effort goes; they do not split subsystems — each subsystem migrates as a unit, with no per-message-type protobuf/MessagePack hybrid (design.md Decision 9, as amended).
- Long-term direction (maintainer, 2026-07-18): migrate everything to MessagePack uniformly so the protobuf **write** paths can eventually be deleted and, once remote-core internals have migrated in their own later change and durable-store migration tooling exists, the `Google.Protobuf` dependency can ultimately be dropped in a future major version. Protobuf **read** support remains for the whole v1.6 cycle (rolling upgrades + durable data are read-forever).

### What Does Not Change

- Legacy protobuf serializers, ids, and wire formats stay in the codebase and are read forever.
- Nested arbitrary **user** payloads keep their own serializers — only the wrapper (`Payload`/`OtherMessage`/`SequencedMessage`) encoding changes; the (serializerId, manifest, bytes) nesting contract is preserved.
- Durable/persisted state — DData LMDB storage and Sharding's remember-entities journal — is **not** flipped to v2 writes in this change; those keep protobuf writes and stay readable indefinitely (persistence rule from `messagepack-sourcegen-validation` design.md Decision 8.1).
- Default is **off** — a freshly-upgraded v1.6 node writes protobuf until the operator enables the flag.
- No new serialization dependency: MessagePack-CSharp via the existing `Akka.Serialization.V2` generator is the settled choice.
- Remote core internals (`MiscMessageSerializer`, `SystemMessageSerializer`, `PrimitiveSerializers`, `MessageContainerSerializer`, `DaemonMsgCreateSerializer`) are out of scope — deferred to a separate later change because they underpin persistence and the upcoming Artery envelopes.
- No framework-enforced `AppVersion` or capability-version handshake gate is added; rollout safety is operator-discipline plus documentation, matching the `widen-system-uid-to-64bit` precedent.

This change supersedes, for the specific subsystems and message shapes listed above, the "protobuf wrapper wire formats are not replaced by default" non-goal recorded in `messagepack-sourcegen-validation/design.md` (Goals / Non-Goals, line 30) and mirrored in that change's `proposal.md` ("What Does Not Change"). That non-goal held while the generator itself was being validated; this change is the follow-on migration built on top of the validated generator, still gated default-off. It does **not** touch Remote core internals or durable/persisted wire formats, which remain non-goals here too (see above).

## Capabilities

### New Capabilities

- `messagepack-internal-serializers`: write-side feature flag (global + per-subsystem), forked MessagePack V2 serializers for ReliableDelivery, DistributedData, Cluster Tools (PubSub/ClusterClient/Metrics/Singleton), Sharding's routing subset, and Cluster core, the reserved 40-79 serializer-id block, and the benchmark acceptance gate that governs each subsystem's default-on transition.

### Modified Capabilities

None. `openspec/specs/` has no existing entries for the touched subsystems (DistributedData, Delivery, Cluster Tools, Sharding, Cluster core serializers predate OpenSpec adoption for this repository), so there is no baseline requirement set to delta against. Subsystem-level code impact is captured under Impact below instead of as formal capability deltas.

## Impact

- New source-generated `*MessagePackSerializer` types + `[AkkaSerializable]` DTO mirrors in `Akka.Cluster` (ReliableDelivery, Cluster core), `Akka.DistributedData`, `Akka.Cluster.Tools`, and `Akka.Cluster.Sharding` (routing subset only).
- Hand-written `IAkkaMessagePackFormatter<T>` formatters for shared wire fragments (`UniqueAddress`, `VersionVector`) duplicated per assembly, extending the existing `AddressFormatter`/`ActorPathFormatter` precedent from `messagepack-sourcegen-validation` design.md Decision 11. No dependency on a cross-assembly shared-schema contract.
- New `serialization-identifiers` / `serializers` reference.conf entries per subsystem (both the legacy id and the new v2 id, unconditionally registered).
- New `akka.actor.serialization.v2.*` HOCON config block plus a central binding-rewrite hook added to `Serialization` construction (`src/core/Akka/Serialization/Serialization.cs`).
- API-approval baselines updated for new serializer types (largely `internal`).
- `BREAKING_CHANGES_V1.6.md` ledger entry documenting the new rolling-upgrade rule ("all nodes on v1.6 before enabling `akka.actor.serialization.v2.enabled`"); this is an operational constraint, not a wire or API break while the flag defaults to off.
- New/extended benchmarks: `ClusterMessageSerializerBenchmarks`, `DDataSerializationBenchmarks`, DData CRDT serializer benchmarks, `RemotePingPong --serializer` harness.
- Operator-facing documentation: runbook for enabling the flag cluster-wide, subsystem-by-subsystem.
