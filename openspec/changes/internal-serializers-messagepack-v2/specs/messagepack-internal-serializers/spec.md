## ADDED Requirements

### Requirement: Write-side flag controls serializer selection only

The system SHALL provide a write-side-only feature flag (`akka.actor.serialization.v2.enabled` plus per-subsystem overrides) that selects which registered serializer a migrated subsystem writes with, and SHALL always register both the legacy protobuf serializer and the new MessagePack V2 serializer for a migrated subsystem regardless of the flag's value.

#### Scenario: Flag disabled writes legacy protobuf

- **WHEN** a subsystem's effective flag is `off` (the default)
- **THEN** the system SHALL write that subsystem's messages using the legacy protobuf serializer

#### Scenario: Flag enabled writes MessagePack V2

- **WHEN** a subsystem's effective flag is `on`
- **THEN** the system SHALL write that subsystem's messages using the forked MessagePack V2 serializer

#### Scenario: Both formats remain readable regardless of flag state

- **WHEN** a node receives a message serialized with either the legacy protobuf serializer id or the MessagePack V2 serializer id for a migrated subsystem
- **THEN** the node SHALL deserialize the message successfully, independent of that subsystem's flag value

### Requirement: Deterministic binding override

The system SHALL apply the write-side flag by deterministically overwriting the exact `serialization-bindings` entry for a subsystem's marker interface, and SHALL NOT add a competing overlapping binding.

#### Scenario: Central hook overwrites exact binding key

- **WHEN** a subsystem's flag is effectively `on` during `Serialization` construction
- **THEN** the system SHALL overwrite the marker interface's existing binding entry with the MessagePack V2 serializer name using an exact-key, last-write-wins replacement

#### Scenario: No new overlapping binding is introduced

- **WHEN** the flag-driven binding override runs
- **THEN** the system SHALL NOT register an additional `serialization-bindings` entry that overlaps with the subsystem's existing marker-interface binding

### Requirement: Reserved internal serializer-id block

The system SHALL assign each forked MessagePack V2 serializer a serializer id in the reserved range 40-79, computed as the corresponding legacy serializer's id plus 40.

#### Scenario: Forked serializer id follows the legacy-plus-40 mapping

- **WHEN** a built-in subsystem serializer is forked to a MessagePack V2 serializer
- **THEN** the new serializer's id SHALL equal the legacy serializer's id plus 40
- **AND** the new serializer's id SHALL fall within the 40-79 range

### Requirement: Durable and persisted writes excluded from this migration

The system SHALL keep DistributedData's durable (LMDB) store writes and Cluster Sharding's remember-entities persisted state on the legacy protobuf serializer regardless of the corresponding subsystem flag.

#### Scenario: DData durable store ignores the distributed-data flag

- **WHEN** the `distributed-data` flag is `on` and a value is written to the durable (LMDB) store
- **THEN** the system SHALL serialize that durable write using the legacy protobuf serializer

#### Scenario: Sharding remember-entities state ignores the sharding flag

- **WHEN** the `sharding` flag is `on` and remember-entities state (`CoordinatorState`, `EntityState`, `EntitiesStarted`, `EntitiesStopped`) is persisted
- **THEN** the system SHALL serialize that persisted state using the legacy protobuf serializer

### Requirement: Envelope and nested payloads are preserved as serializer boundaries

Forked MessagePack V2 wrapper serializers SHALL preserve a wrapped payload's own serializer id, manifest, and serialized bytes rather than re-encoding the payload structurally.

#### Scenario: Wrapped user payload round-trips without re-encoding

- **WHEN** a forked V2 wrapper serializer (for example a delivery `SequencedMessage` or a DistributedData `OtherMessage`/`DataEnvelope`) writes a wrapped application payload
- **THEN** it SHALL store the payload's serializer id, manifest, and opaque serialized bytes
- **AND** it SHALL recover the original payload through normal Akka deserialization using that stored id, manifest, and bytes

### Requirement: Benchmark acceptance gate governs default-on transitions

The system's migrated subsystems SHALL be evaluated with matched protobuf-vs-MessagePack-V2 benchmarks reporting CPU cost, allocations, and payload size before any subsystem's shipped default flag value changes from `off`.

#### Scenario: Subsystem benchmark reports all three gate metrics

- **WHEN** a migrated subsystem's benchmark suite runs
- **THEN** it SHALL report serialize+deserialize CPU cost, allocated bytes, and payload size for both the legacy protobuf serializer and the forked MessagePack V2 serializer

#### Scenario: Subsystems migrate as a unit

- **WHEN** a subsystem's effective flag is `on`
- **THEN** every message type handled by that subsystem's serializer SHALL be written with the forked MessagePack V2 serializer, with no per-message-type carve-out to the legacy protobuf binding
- **AND** any measured payload-size or CPU regression on individual message types SHALL be recorded in the benchmark results and addressed through serializer optimization, informing when the subsystem's shipped default changes rather than which messages migrate

### Requirement: Rolling-upgrade safety through default-off and operator documentation

The system SHALL ship every migrated subsystem's flag defaulted to `off`, and SHALL document that all cluster nodes must be running a version with the MessagePack V2 serializer registered before any node enables a subsystem's flag.

#### Scenario: Freshly upgraded node writes legacy protobuf until an operator opts in

- **WHEN** a node is upgraded to a version containing this change and no configuration override is applied
- **THEN** the node SHALL continue writing every migrated subsystem's messages using the legacy protobuf serializer

#### Scenario: A node without the V2 serializer registered cannot decode a V2 id

- **WHEN** a node that does not have a migrated subsystem's MessagePack V2 serializer registered receives a message with that serializer's id
- **THEN** deserialization SHALL fail with a "cannot find serializer with id" error, which is the documented precondition for requiring all nodes to be upgraded before enabling any subsystem flag
