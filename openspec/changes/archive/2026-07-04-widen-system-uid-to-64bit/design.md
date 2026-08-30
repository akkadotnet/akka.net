## Context

Verified against Akka.NET `dev` (scoping task #39). Two unrelated `uid` concepts exist; **only the address/system UID is in scope.**

- **Address / system UID (IN SCOPE)** — identifies an `ActorSystem` incarnation at a host:port. Origin `AddressUid.Uid` (`int`), flows into `UniqueAddress` → cluster `Member` identity + gossip vector-clock, the remoting handshake, `RemoteWatcher`, and quarantine.
- **ActorPath incarnation uid (OUT OF SCOPE — already `long`)** — the `#12345` path suffix. `ActorPath.Uid`, `Failed.Uid`, `SystemMessageFormats.proto uid` are already 64-bit. Do NOT touch (the grep hits in `ActorCell*`, `ChildStats`, `ActorSelection` are all this one).

## Goals / Non-Goals

**Goals:** widen the address/system UID `int → long` end-to-end; keep a rolling upgrade safe from v1.5; make it a clean prerequisite for Artery.
**Non-Goals:** touching the ActorPath uid; interop between v1.5 and v1.6 once >32-bit uids are generated; changing gossip/failure-detector semantics.

## Decisions

### 1. Hard API break (re-type in place) — DECIDED
Re-type the ~10 public members in place rather than add `long` overloads + `[Obsolete]` the `int` ones. Rationale: v1.6 is already a breaking cycle; the `UniqueAddress.Uid` / `AddressUid.Uid` **fields/props cannot be overloaded**, so extend-only would leave permanent obsolete clutter for no compat benefit (the wire break requires a restart to *enable* anyway). API-approval files are updated accordingly.

### 2. Widen the type everywhere, gate the value range
Widen the CLR type to `long` throughout, but keep default UID **generation** in `[0, int.MaxValue]` (leave `ThreadLocalRandom.Next()`-equivalent as the default). Because protobuf `uint32`/`uint64` share the varint wire type, a not-yet-upgraded v1.5 node reads the widened `ClusterMessages.proto` field fine for any value ≤ `uint32.MaxValue`. **A rolling upgrade is therefore safe.** Full 64-bit generation is opt-in behind a config switch and must only be enabled once the whole cluster + all remote peers are on v1.6 (a cold restart regenerates uids anyway — which is exactly the Artery adoption path).

### 3. Minimal wire change
Only `ClusterMessages.proto` `UniqueAddress.uid` (`uint32`) must widen to `uint64`. The handshake (`WireFormats.proto fixed64`), RemoteWatcher heartbeat (`ContainerFormats.proto uint64`), and DistributedData (`ReplicatorMessages.proto int64`) are **already 64-bit on the wire** — only remove the C# narrowing casts. Vector-clock node names embed the uid as a decimal string and are never parsed back to `int`, so they stay compatible.

### 4. Own change, sequenced FIRST
This lands as its own OpenSpec change before `artery-tcp-remoting`, schema-coordinated with `serializer-v2` (the v2/MessagePack path must also emit/read the uid as 64-bit). It does not block on serializer-v2, but constrains its schema.

## Inventory (from #39)

**Public API — hard re-type (API-approval files move):** `UniqueAddress(Address,int)`/`Uid` (`Member.cs`); `AddressUid.Uid` field + `AddressUidExtension.Uid()` (`AddressUidExtension.cs`); `QuarantinedEvent(Address,int)`/`Uid` (`RemotingLifecycleEvent.cs`); `IRemoteActorRefProvider.Quarantine` / `RemoteActorRefProvider.Quarantine` / `RemoteTransport.Quarantine` / `Remoting.Quarantine` / `RemoteWatcher.Quarantine(Address,int?)`; `RemoteWatcher.HeartbeatRsp(int)` / `AddressUid`.

**Internal sweep:** `HandshakeInfo` + `AkkaProtocolTransport` refuseUid; `EndpointRegistry`; `Endpoint.cs` (`HopelessAssociation`/`ReliableDeliverySupervisor`/`GotUid`/`EndpointWriter`); `EndpointManager` (`Pass`/`Quarantined`/`Quarantine`/`ResendState`); `RemoteWatcher._addressUids`; `ClusterDaemon` (`Quarantined`).

**Serializer cast sites (drop narrowing):** `ClusterMessageSerializer` (`(uint)` / `(int)`), `MiscMessageSerializer` (`(ulong)` / `(int)`), `AkkaPduCodec` (`(ulong)` / `(int)`), DData `SerializationSupport.UniqueAddressFromProto` (`(int)`; proto already `int64`).

**RNG:** `ThreadLocalRandom.Current.Next()` yields int-range only; `Random.NextInt64()` is net6+ (absent on netstandard2.0/net48) → needs a custom 64-bit generator, used only when full-range generation is enabled.

## Draft `BREAKING_CHANGES_V1.6.md` entry (add when code lands)

```
| Planned | widen-system-uid-to-64bit | Akka.Remote / Akka.Cluster | API | System/address UID widened int32 -> int64 across AddressUid.Uid, AddressUidExtension.Uid(), UniqueAddress(Address,int)/.Uid, QuarantinedEvent(Address,int)/.Uid, RemoteWatcher.HeartbeatRsp(int)/.AddressUid, and all Quarantine(Address, int? uid) members (IRemoteActorRefProvider / RemoteTransport / RemoteWatcher). Hard re-type (v1.6). Prerequisite for Artery. | Recompile against the long members; replace int uid locals with long. |
| Planned | widen-system-uid-to-64bit | Akka.Cluster | Wire | Cluster gossip UniqueAddress.uid widened uint32 -> uint64 (ClusterMessages.proto). Same varint wire type -> binary-compatible for uids <= uint32.MaxValue. | Rolling upgrade safe provided uid generation stays in 32-bit range; enable >32-bit generation only after the whole cluster + all remote peers are on v1.6. |
```

## Risks / Trade-offs

- **Silent truncation** if a v1.6 node emits a >32-bit uid while a pre-v1.6 node is present → the value gating (Decision 2) prevents this; the config switch must document the "all nodes v1.6 first" precondition.
- **API-approval churn** across Remote + Cluster (expected; hard break).
- **RNG portability** — the 64-bit generator must work on netstandard2.0/net48.
- **serializer-v2 coordination** — both the classic protobuf serializers and the v2 path must carry the wider uid; schema must agree.
