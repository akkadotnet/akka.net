## ADDED Requirements

### Requirement: Address/system UID is 64-bit

The address/system UID SHALL be a 64-bit signed integer (`long`) across Akka.Remote and Akka.Cluster.

#### Scenario: UID type across the surface
- **WHEN** an `ActorSystem` UID is generated, carried in `UniqueAddress`, exchanged in the remoting handshake, cached by `RemoteWatcher`, or used in quarantine
- **THEN** it SHALL be represented as `long` end-to-end (no 32-bit narrowing)

#### Scenario: ActorPath incarnation uid unchanged
- **WHEN** an actor path incarnation uid (`#nnnnn`) is used
- **THEN** it SHALL remain `long` and unchanged (it is out of scope for this change)

### Requirement: Rolling-upgrade safety via value gating

Default UID generation SHALL stay within `[0, int.MaxValue]` so a pre-v1.6 node reads the widened gossip field without truncation.

#### Scenario: Mixed-version cluster during rollout
- **WHEN** a v1.6 node gossips a `UniqueAddress` to a not-yet-upgraded v1.5 node and generated uids are in 32-bit range
- **THEN** the v1.5 node SHALL read the uid without truncation and the cluster SHALL form normally

#### Scenario: Enabling full 64-bit generation
- **WHEN** full 64-bit UID generation is enabled via configuration
- **THEN** it SHALL be documented as requiring every cluster node and remote peer to already run v1.6 (or a cold restart)

### Requirement: Minimal wire change

Only the cluster gossip `UniqueAddress.uid` field SHALL change on the wire; already-64-bit wire fields SHALL be unchanged.

#### Scenario: Gossip field widened compatibly
- **WHEN** `ClusterMessages.proto` `UniqueAddress.uid` is widened `uint32 → uint64`
- **THEN** it SHALL retain the protobuf varint wire type so values ≤ `uint32.MaxValue` remain binary-compatible with pre-v1.6 readers

#### Scenario: Already-64-bit wire fields
- **WHEN** the remoting handshake (`fixed64`), RemoteWatcher heartbeat (`uint64`), or DistributedData (`int64`) uid is serialized
- **THEN** the wire format SHALL be unchanged and only the C# narrowing casts SHALL be removed

## MODIFIED Requirements

### Requirement: Quarantine carries a 64-bit UID

The quarantine API SHALL accept a 64-bit optional UID.

#### Scenario: Quarantine by UID
- **WHEN** `Quarantine(Address, long? uid)` is invoked on `IRemoteActorRefProvider`, `RemoteTransport`, or `RemoteWatcher`
- **THEN** the association SHALL be quarantined by the 64-bit UID, and `QuarantinedEvent` SHALL expose the UID as `long`
