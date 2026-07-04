## 1. Core type + generation

- [x] 1.1 Widen `AddressUid.Uid` (field) + `AddressUidExtension.Uid(ActorSystem)` `int → long`
- [x] 1.2 Add a netstandard2.0-safe 64-bit RNG; default generation stays in `[0, int.MaxValue]`
- [x] 1.3 Config switch to enable full 64-bit uid generation (default off; documented "all nodes v1.6 first" precondition) — `akka.remote.use-64bit-system-uids`

## 2. Cluster

- [x] 2.1 `UniqueAddress(Address, long uid)` + `long Uid`; fix identity/sort/hash (`Member`)
- [x] 2.2 Verify gossip vector-clock node name (`VclockName` = address + "-" + uid decimal string) — behavior unchanged (string-only, `ClusterDaemon.VclockName`; hash also value-identical for int-range uids)
- [x] 2.3 `ClusterDaemon` `Quarantined(new UniqueAddress(addr, long uid))`

## 3. Remoting state machine (internal `int → long`)

- [x] 3.1 `HandshakeInfo` ctor + `Uid`; `AkkaProtocolTransport` refuseUid path
- [x] 3.2 `EndpointRegistry` (register/quarantine/refuseUid)
- [x] 3.3 `Endpoint.cs`: `HopelessAssociation`, `ReliableDeliverySupervisor`, `GotUid`, `EndpointWriter` uid fields/params
- [x] 3.4 `EndpointManager` messages: `Pass`, `Quarantined`, `Quarantine`, `ResendState`

## 4. RemoteWatcher

- [x] 4.1 `HeartbeatRsp(long addressUid)` + `long AddressUid`; `_addressUids: Dictionary<Address,long>`; `ReceiveHeartbeatRsp`
- [x] 4.2 `Quarantine(Address, long? addressUid)`

## 5. Quarantine API (hard re-type)

- [x] 5.1 `IRemoteActorRefProvider.Quarantine(Address, long? uid)`
- [x] 5.2 `RemoteActorRefProvider.Quarantine`, `RemoteTransport.Quarantine`, `Remoting.Quarantine`
- [x] 5.3 `QuarantinedEvent(Address, long uid)` + `long Uid`

## 6. Wire + serializers

- [x] 6.1 `ClusterMessages.proto` `UniqueAddress.uid` `uint32 → uint64`; regenerate (Grpc.Tools at build time)
- [x] 6.2 Remove narrowing casts: `ClusterMessageSerializer`, `MiscMessageSerializer`, `AkkaPduCodec`, DData `SerializationSupport.UniqueAddressFromProto`
- [x] 6.3 Coordinate `serializer-v2` schema to emit/read 64-bit uid (Decision 11 in serializer-v2 design.md)

## 7. API approval + build

- [x] 7.1 Update `Akka.API.Tests` approved files (`ApproveRemote.*`, `ApproveCluster.*`) for the hard break
- [x] 7.2 `dotnet build -warnaserror` clean
- [x] 7.3 Akka.Remote + Akka.Cluster + DistributedData tests green (391 / 376 / 190 passed, 0 failed, net10.0 Release)

## 8. Compatibility + docs

- [x] 8.1 Rolling-upgrade test: v1.5 ↔ v1.6 gossip with int-range uids — no truncation, cluster forms (wire-level simulation via uint32/uint64 varint cross-parse in `UniqueAddressWireCompatSpec`; a live mixed-version cluster cannot be exercised in-repo)
- [x] 8.2 Full-range generation test (all-v1.6) — >32-bit uids round-trip (gossip, heartbeat, handshake PDU, DData; generation range tests in `AddressUidExtensionSpecs`)
- [x] 8.3 Add `BREAKING_CHANGES_V1.6.md` entry (API + Wire rows)
