## Why

Akka.NET's **address/system UID** — the value that distinguishes an `ActorSystem` incarnation at a host:port so a restarted system is recognized as new — is a 32-bit `int` (`AddressUid`). Pekko/Artery uses a 64-bit `long`, and the Artery TCP frame header carries a 64-bit origin UID. **A 64-bit system UID is therefore a hard prerequisite for `artery-tcp-remoting`.** A wider UID also lowers restart-incarnation collision odds and clears the long-standing `// TODO: change to uint32` in `MiscMessageSerializer`.

This change widens the address/system UID from `int` to `long` across Akka.Remote + Akka.Cluster as a **hard API break** (decision: v1.6 is already a breaking cycle; re-type in place rather than carry obsolete `int` members). The **ActorPath incarnation uid** (`#12345`) is already `long` and is explicitly out of scope.

## What Changes

- Widen `Akka.Remote.AddressUid.Uid` (public readonly field) and `AddressUidExtension.Uid(ActorSystem)` `int → long`.
- Widen `Akka.Cluster.UniqueAddress` — ctor `(Address, long uid)` + `long Uid` (the identity / sort / hash key for every `Member`, and the gossip vector-clock node name).
- Widen `Akka.Remote.QuarantinedEvent(Address, long uid)` + `long Uid`.
- Widen the quarantine API to `long? uid` (hard re-type): `IRemoteActorRefProvider.Quarantine`, `RemoteActorRefProvider.Quarantine`, `RemoteTransport.Quarantine`, `Remoting.Quarantine`, `RemoteWatcher.Quarantine`.
- Widen `RemoteWatcher.HeartbeatRsp(long addressUid)` + `long AddressUid` + `Dictionary<Address,long>`.
- Internal `int → long` sweep: `HandshakeInfo`, `AkkaProtocolTransport` (refuseUid), `EndpointRegistry`, `Endpoint.cs` (`HopelessAssociation` / `ReliableDeliverySupervisor` / `GotUid` / `EndpointWriter`), `EndpointManager` command messages, `ClusterDaemon`.
- **Wire:** widen `ClusterMessages.proto` `UniqueAddress.uid` `uint32 → uint64` (regenerate). Remove the C# narrowing casts where the wire is already 64-bit: `MiscMessageSerializer`, `ClusterMessageSerializer`, `AkkaPduCodec`, DData `SerializationSupport.UniqueAddressFromProto`.
- **RNG:** replace `ThreadLocalRandom.Current.Next()` (int-range) with a netstandard2.0-safe 64-bit generator, **value-gated to int-range by default** (rolling-upgrade safety); full 64-bit generation behind a config switch.

### What Does Not Change

- The ActorPath incarnation uid (`ActorPath.Uid`, `Failed.Uid`, `SystemMessageFormats.proto uid`) — already `long`.
- Wire formats already 64-bit — handshake (`WireFormats.proto fixed64`), RemoteWatcher heartbeat (`ContainerFormats.proto uint64`), DistributedData (`ReplicatorMessages.proto int64`) — only the C# narrowing casts are removed.
- Default value generation stays in `[0, int.MaxValue]` (rolling-upgrade safe); >32-bit generation is opt-in.

## Capabilities

### New Capabilities

- `system-uid-64bit`: 64-bit address/system UID across remoting + cluster, with rolling-upgrade-safe value gating.

## Impact

- **Akka.Remote / Akka.Cluster:** hard API break on ~10 public members (API-approval files updated).
- **Wire:** one proto field widened (`ClusterMessages.proto`), varint-compatible for values ≤ `uint32.MaxValue`.
- **Prerequisite for:** `artery-tcp-remoting`; schema-coordinated with `serializer-v2`.
- **BREAKING_CHANGES_V1.6.md:** entry added when the code lands (draft in design.md).
