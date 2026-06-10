# Akka.NET 1.6 Transport & Serialization Epic - Implementation Order

## Overview

Akka.NET 1.6 makes `SerializerV2` the canonical serialization abstraction and uses it as the payload contract for both existing compatibility paths and the new high-throughput remoting path.

The important sequencing decision is that `SerializerV2` cannot be implemented as a core-only API change. Once `Serialization.FindSerializerFor()` returns `SerializerV2`, classic Akka.Remote, Akka.Persistence, delivery, DistributedData, and other serializer call sites are affected immediately. Therefore the first active milestone is an atomic foundation: V2 core plus the classic remoting and persistence bridges needed to keep the repository green.

The second sequencing decision is that source-generated MessagePack serialization must validate the V2 API before Artery envelopes are introduced. Artery should not bake in assumptions about size hints, bytes-written reporting, manifests, async behavior, or V1 fallback until generated serializers have passed through `Serialization`, classic remoting, and persistence.

The third sequencing decision is to replace the previous `streams-tcp-transport` plan. The high-throughput path should be an Artery-style remoting stack beside classic remoting, not a retrofit through `EndpointWriter`, `AkkaProtocolTransport`, `AkkaPduCodec`, and `AssociationHandle.Write(ByteString)`.

## Performance Baseline

Captured on `dev` branch (commit 467cbb510), .NET 10.0, Release, ServerGC, Linux 6.8.0-106, 8 cores:

| Clients | Msgs/sec |
|---------|----------|
| 1 | 85,179 |
| 5 | 399,841 |
| 10 | 582,921 |
| 15 | 625,391 |
| 20 | 603,956 |
| 25 | 670,422 |
| 30 | 679,964 |

**Peak: ~680K msgs/sec.** The new Artery TCP transport must exceed this before it can replace DotNetty as the preferred remoting path.

## Milestones

### Milestone 1: `modernize-akka-io-tcp`

**Branch**: already merged via PR #8132
**OpenSpec change**: `openspec/changes/modernize-akka-io-tcp/`
**Status**: completed baseline work

**What it did**: Modernized Akka.IO TCP around `ReadOnlySequence<byte>`, `System.IO.Pipelines`, and `ITransportConnection`.

**Relevant outcome**: This work is useful for Artery and for user-facing Akka.Streams TCP, but the Artery MVP should not route its hot path through materialized Akka.Streams TCP.

---

### Milestone 2: `serializer-v2` - Atomic V2 Foundation

**Branch**: `feature/spec2-serializer-v2-foundation`
**OpenSpec change**: `openspec/changes/serializer-v2/`
**Tasks file**: `openspec/changes/serializer-v2/tasks.md`

**What it does**: Makes `SerializerV2` the canonical serialization abstraction and fixes every immediate compatibility bridge required by that API break.

**Must be atomic**:

- `SerializerV2` core API
- `SerializerV1Adapter`
- `Serialization.cs` internal storage and public return-type changes
- classic Akka.Remote V2 bridge preserving classic wire compatibility
- Akka.Persistence V2 bridge preserving stored event and snapshot compatibility
- compile fallout in delivery, DistributedData, tests, and other direct serializer call sites

**Completion criteria**:

- `SerializerV2` and `SerializerV1Adapter` exist in `src/core/Akka/Serialization/`.
- `Serialization.cs` stores V2 serializers internally and auto-wraps V1 serializers.
- `FindSerializerFor()` and `FindSerializerForType()` return `SerializerV2`.
- V1 serializers continue to work through `SerializerV1Adapter`.
- classic Akka.Remote sends and receives messages through V2 while preserving existing classic wire format.
- Akka.Persistence events and snapshots write/read through V2 while preserving existing stored data compatibility.
- Old journal events and snapshots remain readable.
- New V2 payloads can be persisted and recovered.
- Delivery, DistributedData, and other direct serializer call sites compile and preserve behavior.
- API approval tests are updated for the intentional 1.6 API break.
- `dotnet build -warnaserror` passes.
- Akka.Serialization, Akka.Remote, and Akka.Persistence tests pass.

**After completion**: Review with human. Archive via `openspec archive serializer-v2` only after the repository is green across serialization, remoting, and persistence.

---

### Milestone 3: `messagepack-sourcegen-validation`

**Branch**: `feature/spec3-messagepack-sourcegen-validation`
**OpenSpec change**: `openspec/changes/messagepack-sourcegen-validation/`
**Tasks file**: `openspec/changes/messagepack-sourcegen-validation/tasks.md`

**What it does**: Implements the source-generated MessagePack serializer path and uses it to validate the real `SerializerV2` API before Artery envelopes depend on it.

**Completion criteria**:

- `Akka.Serialization.V2` package exists.
- Generated serializers use direct MessagePack reader/writer cursors.
- Source generator emits serializers for `[AkkaSerializable]` / `[AkkaField]` types.
- Generated serializers round-trip through `Serialization.cs`.
- Generated serializers send over classic remoting.
- Generated serializers persist and recover events.
- Generated serializers save and load snapshots.
- V1 and generated V2 serializers coexist.
- `SizeHint`, unknown-size behavior, manifest handling, bytes-written/result semantics, and oversized payload behavior are validated.

**After completion**: Review with human. Archive via `openspec archive messagepack-sourcegen-validation`.

---

### Milestone 4: `artery-tcp-remoting`

**Branch**: `feature/spec4-artery-tcp-remoting`
**OpenSpec change**: `openspec/changes/artery-tcp-remoting/`
**Tasks file**: `openspec/changes/artery-tcp-remoting/tasks.md`

**What it does**: Adds a new Artery-style TCP remoting stack beside classic remoting, using the validated `SerializerV2` payload contract.

**Implementation strategy**:

- Do not route the hot path through classic `EndpointWriter` / `AkkaProtocolTransport` / `AkkaPduCodec`.
- Start with plaintext TCP and one ordinary stream.
- Add control stream and reliable system-message delivery before lanes and compression.
- Preserve classic remoting as a compatibility path.
- Defer QUIC to Akka.NET 1.7.

**Completion criteria**:

- `ArteryRemoting : RemoteTransport` exists and is selected by configuration.
- Artery TCP listener and outbound association setup work.
- TCP framing uses `AKKA` magic + 1-byte stream id connection header and 4-byte little-endian frame length.
- Handshake includes address and UID and is tied to association incarnation state.
- Basic remote actor messaging works over Artery TCP.
- Control stream exists for handshake, liveness, quarantine, and system-message ACK/NACK traffic.
- Reliable system-message delivery preserves remoting correctness.
- Quarantine semantics are UID-scoped and compatible with existing remote lifecycle expectations.
- Classic remoting still works independently.

**After completion**: Review with human. Archive via `openspec archive artery-tcp-remoting`.

---

### Milestone 5: `transport-performance`

**Branch**: `feature/spec5-artery-performance`
**OpenSpec change**: `openspec/changes/transport-performance/`
**Tasks file**: `openspec/changes/transport-performance/tasks.md`

**What it does**: Measures and tunes the Artery TCP stack against the DotNetty baseline.

**Completion criteria**:

- RemotePingPong on Artery TCP exceeds the 680K msgs/sec DotNetty baseline.
- Artery envelope codec materially outperforms the classic protobuf PDU path in allocation and throughput microbenchmarks.
- Generated MessagePack serializers show lower allocation and better throughput than V1 adapter fallback.
- Bounded queue and backpressure tests prove memory cannot grow unbounded under slow receivers.
- Latency impact of batching is measured and configurable.
- Results are documented.

**After completion**: Review with human. Archive via `openspec archive transport-performance`.

---

### Milestone 6: `akka-io-tls-support`

**Branch**: `feature/spec6-akka-io-tls`
**OpenSpec change**: `openspec/changes/akka-io-tls-support/`
**Tasks file**: `openspec/changes/akka-io-tls-support/tasks.md`

**What it does**: Adds TLS support for the modernized TCP infrastructure and eventually Artery TCP.

**Sequencing note**: TLS is important, but plaintext Artery TCP should prove the envelope, association, system-message, and performance model first. TLS can then wrap the same connection model.

## Orchestration

Each milestone is executed by an OpenSpec-oriented orchestrator that:

1. Reads the OpenSpec `tasks.md` for the current milestone.
2. Creates a new branch for the milestone.
3. Delegates mechanical call-site work to focused workers when useful.
4. Runs focused builds and tests after each task group.
5. Captures compile fallout instead of hiding it behind compatibility shims.
6. Runs the milestone completion test suite.
7. Reports completion status to human for review.

Only one milestone should be executed per implementation run. Human review happens before proceeding to the next milestone.
