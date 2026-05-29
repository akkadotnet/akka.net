# Akka.NET 1.6 Transport & Serialization Epic — Implementation Order

## Overview

Five OpenSpec changes implement the Akka.NET 1.6 transport and serialization overhaul. This file defines the order in which they must be implemented, their dependencies, and the completion criteria for each milestone.

Each milestone is implemented on its own branch off of `feature/openspec-init`. After a milestone is complete and reviewed, its OpenSpec change is archived via `openspec archive`.

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

**Peak: ~680K msgs/sec.** The new transport (after Milestone 3) must exceed this.

## Milestones

### Milestone 1: `modernize-akka-io-tcp` (Spec 1)

**Branch**: `feature/spec1-modernize-akka-io-tcp` (off `feature/openspec-init`)
**OpenSpec change**: `openspec/changes/modernize-akka-io-tcp/`
**Tasks file**: `openspec/changes/modernize-akka-io-tcp/tasks.md`

**What it does**: Replace ByteString with System.Memory, replace SocketAsyncEventArgs with Stream + Pipe, add IStreamProvider abstraction.

**Implementation strategy**: Dark period approach. Delete ByteString, use compiler errors as todo list, fix module by module until `dotnet build` succeeds. Then replace SAEA with Stream+Pipe and get tests passing.

**Completion criteria**:
- `dotnet build -warnaserror` passes on net10.0
- `dotnet test -c Release --framework net10.0` — all Akka.IO TCP tests pass
- `dotnet test -c Release --framework net10.0` — all Akka.Streams TCP tests pass
- `dotnet test -c Release --framework net10.0` — all Akka.Remote tests pass (with DotNetty still present but ByteString removed)
- No ByteString references remain in the codebase
- IStreamProvider + TcpStreamProvider exist and are used by TcpOutgoingConnection/TcpIncomingConnection

**After completion**: Review with human. Archive via `openspec archive modernize-akka-io-tcp`.

---

### Milestone 2: `serializer-v2` (Spec 4) — foundation only

**Branch**: `feature/spec4-serializer-v2` (off Milestone 1's merged branch)
**OpenSpec change**: `openspec/changes/serializer-v2/`
**Tasks file**: `openspec/changes/serializer-v2/tasks.md`

**What it does**: Establish the `SerializerV2` foundation — base class, V1 adapter, `Serialization.cs` / `MessageSerializer.cs` infrastructure changes, and V2 ports of `ByteArraySerializer` and `PrimitiveSerializers` as the reference implementation. Add a standalone transport-envelope benchmark that simulates `EndpointWriter`'s round trip on the V2 API and compares V2-direct against the V1-bridge baseline.

**Note**: This was originally planned as parallel with Milestone 1 but is sequenced after it to avoid merge conflicts in `Serialization.cs` and `MessageSerializer.cs`. The ByteString removal in Milestone 1 also affects serializer code paths.

**Scope changed (2026-05-10)**: MessagePack codec, `AkkaWriter` / `AkkaReader`, `[AkkaSerializable]` / `[AkkaField]` / `[AkkaSerializer]` attributes, the `Akka.Serialization.V2` NuGet package, the Roslyn source generator, and the mechanical port of remaining Protobuf-based internal serializers (`ClusterMessageSerializer`, `SystemMessageSerializer`, the four `WrappedPayloadSupport` serializers) all moved out of this milestone and into a future change (`serializer-v2-codegen`). Reason: the runtime codec API stands or falls with the source generator that makes it ergonomic; locking in surface area before codegen requirements are in hand would force redesign churn.

**Completion criteria**:

- `SerializerV2`, `SerializerV1Adapter` exist in `src/core/Akka/Serialization/`
- `Serialization.cs` uses `SerializerV2` internally, auto-wraps V1 on registration
- `FindSerializerFor()` returns `SerializerV2`
- `MessageSerializer.cs` (Akka.Remote) uses V2 dispatch (calls `Manifest()` directly)
- `ByteArraySerializer` and `PrimitiveSerializers` ported to `SerializerV2` (same IDs, byte-identical wire format, all primitive paths covered: string / int32 / int64 / byte[])
- Transport-envelope benchmark exists in `src/benchmark/` and reports V2-direct vs V1-bridge allocations and throughput across the reference serializer paths
- All existing serialization tests pass (V1 auto-wrapped)
- All Akka.Persistence tests pass (V1-persisted data still readable)
- `dotnet test -c Release --framework net10.0` passes

**After completion**: Review with human, including benchmark results. Archive via `openspec archive serializer-v2`.

---

### Milestone 3: `akka-io-tls-support` (Spec 2)

**Branch**: `feature/spec2-akka-io-tls` (off Milestone 2's merged branch)
**OpenSpec change**: `openspec/changes/akka-io-tls-support/`
**Tasks file**: `openspec/changes/akka-io-tls-support/tasks.md`

**What it does**: Add TlsStreamProvider, server-side TLS handshake, TlsSettings config, TlsSetup programmatic API.

**Completion criteria**:
- TlsStreamProvider wraps SslStream, handshake in ConnectAsync
- Server-side TLS handshake in TcpIncomingConnection with timeout
- All existing DotNetty TLS HOCON keys parse into TlsSettings
- TlsSetup programmatic config works and overrides HOCON
- TLS integration tests pass (self-signed certs, mutual TLS, validation)
- `dotnet test -c Release --framework net10.0` passes

**After completion**: Review with human. Archive via `openspec archive akka-io-tls-support`.

---

### Milestone 4: `streams-tcp-transport` (Spec 3)

**Branch**: `feature/spec3-streams-transport` (off Milestone 3's merged branch)
**OpenSpec change**: `openspec/changes/streams-tcp-transport/`
**Tasks file**: `openspec/changes/streams-tcp-transport/tasks.md`

**What it does**: Replace DotNetty with Akka.Streams TCP transport. FrameBufferWriter for integrated framing + serialization. Binary PDU codec. Delete DotNetty entirely.

**Completion criteria**:
- StreamsTcpTransport implements Transport abstraction
- FrameBufferWriter enables single-buffer frame construction
- BinaryPduCodec replaces Protobuf AkkaPduCodec
- All `akka.remote.dot-netty.tcp.*` HOCON config works unchanged
- DotNetty directory and NuGet deps deleted
- Two ActorSystems communicate via new transport
- Cluster formation works
- All non-DotNetty-specific Akka.Remote specs pass
- `dotnet test -c Release --framework net10.0` passes

**After completion**: Review with human. Archive via `openspec archive streams-tcp-transport`.

---

### Milestone 5: `transport-performance` (Spec 5)

**Branch**: `feature/spec5-performance` (off Milestone 4's merged branch)
**OpenSpec change**: `openspec/changes/transport-performance/`
**Tasks file**: `openspec/changes/transport-performance/tasks.md`

**What it does**: Run RemotePingPong, implement flush batching and optimizations, exceed DotNetty baseline.

**Completion criteria**:
- RemotePingPong on new transport exceeds 680K msgs/sec peak
- Flush batching implemented and tuned
- Pipe thresholds benchmarked and configured
- Results documented

**After completion**: Review with human. Archive via `openspec archive transport-performance`.

---

## Unscheduled / Future Changes

### `serializer-v2-codegen` (spawned 2026-05-10 from Milestone 2 scope split)

**OpenSpec change**: `openspec/changes/serializer-v2-codegen/`

**What it will do**: Pick up the user-facing codec story that was deferred out of Milestone 2 — `MessagePackSerializer : SerializerV2`, sealed `AkkaWriter` / `AkkaReader`, `[AkkaSerializable]` / `[AkkaField]` / `[AkkaSerializer]` attributes, the `Akka.Serialization.V2` NuGet package, the Roslyn source generator, and the mechanical port of remaining Protobuf-based internal serializers.

**Sequencing**: Not on the critical path for the 1.6 transport epic. Can be scheduled independently once Milestone 2 is archived and the V2 API has been validated by its benchmark and downstream consumption from Spec 3.

## Orchestration

Each milestone is executed by an OpenProse orchestrator that:

1. Reads the OpenSpec `tasks.md` for the current milestone
2. Creates a new branch for the milestone
3. Delegates tasks to worker agents (Sonnet gophers for mechanical changes, Opus for design decisions)
4. After each task group: attempts `dotnet build`, captures errors, iterates
5. After all tasks: runs `dotnet test -c Release --framework net10.0`, fixes failures
6. Commits progress incrementally
7. Reports completion status to human for review

Only one milestone is executed per orchestrator run. Human reviews before proceeding to the next.
