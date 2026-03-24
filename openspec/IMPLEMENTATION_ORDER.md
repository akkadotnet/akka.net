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

### Milestone 2: `serializer-v2` (Spec 4)

**Branch**: `feature/spec4-serializer-v2` (off Milestone 1's merged branch)
**OpenSpec change**: `openspec/changes/serializer-v2/`
**Tasks file**: `openspec/changes/serializer-v2/tasks.md`

**What it does**: Add SerializerV2 base class, SerializerV1Adapter, MessagePackSerializer, modify Serialization.cs infrastructure, mechanical port of simple internal Protobuf serializers.

**Note**: This was originally planned as parallel with Milestone 1 but is sequenced after it to avoid merge conflicts in Serialization.cs and MessageSerializer.cs. The ByteString removal in Milestone 1 also affects serializer code paths.

**Completion criteria**:
- SerializerV2, SerializerV1Adapter exist in `src/core/Akka/Serialization/`
- Akka.Serialization.V2 package exists with MessagePackSerializer, AkkaWriter, AkkaReader
- `Serialization.cs` uses SerializerV2 internally, auto-wraps V1
- `FindSerializerFor()` returns SerializerV2
- Hand-written MessagePack serializer round-trips through full pipeline
- All existing serialization tests pass (V1 auto-wrapped)
- Simple internal Protobuf serializers ported to V2 base (same IDs, same wire format)
- `dotnet test -c Release --framework net10.0` passes

**After completion**: Review with human. Archive via `openspec archive serializer-v2`.

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
