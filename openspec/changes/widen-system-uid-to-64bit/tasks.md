## 1. Core type + generation

- [ ] 1.1 Widen `AddressUid.Uid` (field) + `AddressUidExtension.Uid(ActorSystem)` `int → long`
- [ ] 1.2 Add a netstandard2.0-safe 64-bit RNG; default generation stays in `[0, int.MaxValue]`
- [ ] 1.3 Config switch to enable full 64-bit uid generation (default off; documented "all nodes v1.6 first" precondition)

## 2. Cluster

- [ ] 2.1 `UniqueAddress(Address, long uid)` + `long Uid`; fix identity/sort/hash (`Member`)
- [ ] 2.2 Verify gossip vector-clock node name (`VclockName` = address + "-" + uid decimal string) — behavior unchanged
- [ ] 2.3 `ClusterDaemon` `Quarantined(new UniqueAddress(addr, long uid))`

## 3. Remoting state machine (internal `int → long`)

- [ ] 3.1 `HandshakeInfo` ctor + `Uid`; `AkkaProtocolTransport` refuseUid path
- [ ] 3.2 `EndpointRegistry` (register/quarantine/refuseUid)
- [ ] 3.3 `Endpoint.cs`: `HopelessAssociation`, `ReliableDeliverySupervisor`, `GotUid`, `EndpointWriter` uid fields/params
- [ ] 3.4 `EndpointManager` messages: `Pass`, `Quarantined`, `Quarantine`, `ResendState`

## 4. RemoteWatcher

- [ ] 4.1 `HeartbeatRsp(long addressUid)` + `long AddressUid`; `_addressUids: Dictionary<Address,long>`; `ReceiveHeartbeatRsp`
- [ ] 4.2 `Quarantine(Address, long? addressUid)`

## 5. Quarantine API (hard re-type)

- [ ] 5.1 `IRemoteActorRefProvider.Quarantine(Address, long? uid)`
- [ ] 5.2 `RemoteActorRefProvider.Quarantine`, `RemoteTransport.Quarantine`, `Remoting.Quarantine`
- [ ] 5.3 `QuarantinedEvent(Address, long uid)` + `long Uid`

## 6. Wire + serializers

- [ ] 6.1 `ClusterMessages.proto` `UniqueAddress.uid` `uint32 → uint64`; regenerate
- [ ] 6.2 Remove narrowing casts: `ClusterMessageSerializer`, `MiscMessageSerializer`, `AkkaPduCodec`, DData `SerializationSupport.UniqueAddressFromProto`
- [ ] 6.3 Coordinate `serializer-v2` schema to emit/read 64-bit uid

## 7. API approval + build

- [ ] 7.1 Update `Akka.API.Tests` approved files (`ApproveRemote.*`, `ApproveCluster.*`) for the hard break
- [ ] 7.2 `dotnet build -warnaserror` clean
- [ ] 7.3 Akka.Remote + Akka.Cluster + DistributedData tests green

## 8. Compatibility + docs

- [ ] 8.1 Rolling-upgrade test: v1.5 ↔ v1.6 gossip with int-range uids — no truncation, cluster forms
- [ ] 8.2 Full-range generation test (all-v1.6) — >32-bit uids round-trip
- [ ] 8.3 Add `BREAKING_CHANGES_V1.6.md` entry (API + Wire rows)
