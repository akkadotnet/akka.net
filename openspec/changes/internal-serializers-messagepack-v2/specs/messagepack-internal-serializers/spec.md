## ADDED Requirements

### Requirement: V2 serializers register as read-only rows in the modules' C# serializer tables

The system SHALL register each forked MessagePack V2 serializer in its module's C# serializer table (`<Module>Serializers`) under the alias `<legacy-alias>-v2` with an empty `UseFor`, so that every node can read V2 bytes as soon as it runs a release that contains the row. Built-in serializers SHALL NOT be registered in HOCON (`.conf`) files.

#### Scenario: A shipped V2 row is readable and does not change writes

- **WHEN** a release ships a module's V2 row and the global V2 switch is off
- **THEN** the node SHALL deserialize bytes carrying the V2 serializer id
- **AND** the node SHALL still write that module's messages with the legacy serializer

#### Scenario: The V2 id resolves with dynamic type loading off

- **WHEN** an `ActorSystem` starts with dynamic type loading disabled
- **THEN** the V2 row SHALL register and resolve by id without error

### Requirement: Migrated subsystems write MessagePack when the global V2 switch is on, with legacy readers registered forever

The system SHALL provide one global V2 switch, off by default. When a user turns the switch on, each module's V2 row SHALL take over its legacy row's type bindings. The system SHALL always register both the legacy serializer and the V2 serializer (each under its own stable id) regardless of the switch.

#### Scenario: Switch off writes legacy

- **WHEN** the switch is off and no user binding is present
- **THEN** the system SHALL write every built-in subsystem's messages with its legacy serializer

#### Scenario: Switch on writes MessagePack V2

- **WHEN** the switch is on and no user binding is present
- **THEN** the system SHALL write each migrated subsystem's messages with its V2 serializer

#### Scenario: Both formats remain readable regardless of the switch

- **WHEN** a node receives a message serialized with either the legacy serializer id or the V2 serializer id for a migrated subsystem
- **THEN** the node SHALL deserialize the message successfully, independent of the switch

### Requirement: User bindings override the switch

The system SHALL honor a user `serialization-bindings` entry (by alias) or a `SerializationSetup` binding over the serializer table defaults and the switch, so an operator can pin a subsystem's marker interface to the legacy serializer. The system SHALL NOT introduce any per-subsystem flag or registry for selecting a subsystem's write format.

#### Scenario: Binding override restores legacy writes

- **WHEN** the switch is on and an operator binds a migrated subsystem's marker interface to the legacy serializer alias in `application.conf`
- **THEN** the system SHALL write that subsystem's messages using the legacy serializer
- **AND** reads of both wire formats SHALL continue to succeed

### Requirement: Reserved internal serializer-id block

The system SHALL assign each forked MessagePack V2 serializer a serializer id in the reserved range 40-79, computed as the corresponding legacy serializer's id plus 40. A serializer that keeps the legacy wire format (see the native serializers requirement) SHALL keep its legacy id and SHALL NOT receive a V2 id.

#### Scenario: Forked serializer id follows the legacy-plus-40 mapping

- **WHEN** a built-in subsystem serializer is forked to a MessagePack V2 serializer
- **THEN** the new serializer's id SHALL equal the legacy serializer's id plus 40
- **AND** the new serializer's id SHALL fall within the 40-79 range

### Requirement: Byte-identical formats use native serializers under the same id

The system SHALL implement a serializer whose bytes are already minimal, or whose bytes durable stores read back without a stored id, as a hand-written native `SerializerV2` that writes the same bytes under the same id. This applies to Primitive (17), PersistenceMessage (7) and PersistenceSnapshot (8).

#### Scenario: Native serializer output equals the legacy output

- **WHEN** a native `SerializerV2` serializes a message of a type its legacy serializer handles
- **THEN** the bytes SHALL equal the legacy serializer's bytes for that message
- **AND** the serializer SHALL read all bytes the legacy serializer wrote, including multi-segment sequences

### Requirement: Durable records are self-describing and recover by stored format

Every durable record the migration touches SHALL carry a signal of the serializer that wrote it, and recovery SHALL dispatch on that signal rather than on the current write-side binding, so that records written before and after the switch is turned on both recover correctly. Persistence outer records that existing plugins store without a serializer id SHALL keep their legacy bytes and id.

#### Scenario: Legacy headerless LMDB records recover after the DData switch-over

- **WHEN** DistributedData's bindings have moved to the V2 serializer and the durable (LMDB) store contains records written before this change (no format header)
- **THEN** the store SHALL recover each headerless record as the legacy protobuf `DurableDataEnvelope`
- **AND** records written after the change SHALL carry a `(serializerId, manifest)` header and recover by that stored id

#### Scenario: Stamped persistence payloads recover old and new entries by stored id

- **WHEN** a journal or snapshot record stamps the serializer id of its payload (for example Akka.Delivery's `EventSourcedProducerQueue` events, or Sharding remember-entities events) and holds a mix of legacy and V2 payloads
- **THEN** recovery SHALL deserialize each payload using its own stored serializer id and manifest, independent of the current binding

### Requirement: Envelope and nested payloads are preserved as serializer boundaries

Forked MessagePack V2 wrapper serializers SHALL preserve a wrapped payload's own serializer id, manifest, and serialized bytes rather than re-encoding the payload structurally. A property typed `object` is the payload boundary.

#### Scenario: Wrapped user payload round-trips without re-encoding

- **WHEN** a forked V2 wrapper serializer (for example a delivery `SequencedMessage` or a DistributedData `OtherMessage`/`DataEnvelope`) writes a wrapped application payload
- **THEN** it SHALL store the payload's serializer id, manifest, and opaque serialized bytes
- **AND** it SHALL recover the original payload through normal Akka deserialization using that stored id, manifest, and bytes

### Requirement: Benchmark acceptance gate governs switch-on readiness

The system's migrated subsystems SHALL be evaluated with matched legacy-vs-MessagePack-V2 benchmarks reporting CPU cost, allocations, and payload size, and the maintainer SHALL sign off on a full (not ShortRun) result before the switch is recommended for that subsystem.

#### Scenario: Subsystem benchmark reports all three gate metrics

- **WHEN** a migrated subsystem's benchmark suite runs
- **THEN** it SHALL report serialize+deserialize CPU cost, allocated bytes, and payload size for both the legacy serializer and the forked MessagePack V2 serializer

#### Scenario: Subsystems migrate as a unit

- **WHEN** the switch moves a subsystem's bindings to its V2 row
- **THEN** every message type handled by that subsystem's serializer SHALL be written with the V2 serializer, with no per-message-type carve-out to the legacy binding
- **AND** any measured payload-size or CPU regression on individual message types SHALL be recorded in the benchmark results and addressed through serializer optimization, informing switch-on readiness rather than which messages migrate

### Requirement: Rolling-upgrade safety through read-forever registration and switch-on guidance

The system SHALL keep every v1.6 node able to read both wire formats at all times, and SHALL document that the switch be turned on only after every node in the cluster runs a version that can read V2. The system SHALL NOT enforce that guidance in code.

#### Scenario: Mixed v1.6 clusters interoperate without configuration

- **WHEN** some v1.6 nodes write MessagePack V2 for a subsystem and other v1.6 nodes write the legacy format for the same subsystem (for example mid-roll of a switch change, or a node pinned to legacy)
- **THEN** all nodes SHALL deserialize all of that subsystem's messages successfully

#### Scenario: A node without the V2 serializer registered cannot decode a V2 id

- **WHEN** a node that does not have a migrated subsystem's V2 serializer registered receives a message with that serializer's id
- **THEN** deserialization SHALL fail with a "cannot find serializer with id" error, which is why the switch waits until every node can read V2
