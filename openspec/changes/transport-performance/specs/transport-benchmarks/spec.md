## ADDED Requirements

### Requirement: DotNetty baseline preserved

A DotNetty performance baseline SHALL be available for comparison before Artery TCP is evaluated.

#### Scenario: Baseline recorded
- **WHEN** the baseline is referenced
- **THEN** it SHALL include messages/sec, latency percentiles, allocation rate, hardware, OS, .NET version, and GC mode

### Requirement: Artery TCP exceeds DotNetty throughput

Artery TCP SHALL exceed the recorded DotNetty RemotePingPong throughput baseline on comparable hardware.

#### Scenario: Throughput comparison
- **WHEN** RemotePingPong is run against Artery TCP
- **THEN** peak messages/sec SHALL exceed the DotNetty baseline

#### Scenario: Allocation comparison
- **WHEN** allocation rate is measured for Artery TCP
- **THEN** it SHALL be lower than the classic DotNetty remoting path for representative workloads

### Requirement: Envelope codec benchmarked

The Artery binary envelope codec SHALL be benchmarked against the classic protobuf PDU codec.

#### Scenario: Codec throughput comparison
- **WHEN** codec-only benchmarks are run
- **THEN** Artery envelope encode/decode throughput SHALL exceed classic protobuf PDU throughput

#### Scenario: Codec allocation comparison
- **WHEN** codec-only allocations are measured
- **THEN** Artery envelope encode/decode SHALL allocate fewer bytes than the classic protobuf PDU path

### Requirement: SerializerV2 sourcegen benchmarked

Generated MessagePack V2 serializers SHALL be benchmarked against V1 adapter fallback.

#### Scenario: Generated serializer faster than fallback
- **WHEN** representative messages are serialized and deserialized
- **THEN** generated MessagePack V2 serializers SHALL be faster and allocate less than V1 adapter fallback

### Requirement: Bounded queue behavior validated

Artery TCP outbound queues SHALL remain bounded under load.

#### Scenario: Slow receiver
- **WHEN** a sender outpaces a receiver
- **THEN** outbound queues SHALL not grow without bound

#### Scenario: Control traffic not starved
- **WHEN** ordinary traffic is saturated
- **THEN** control and system-message traffic SHALL still make progress or fail according to documented policy

### Requirement: Batching configurable and measured

Artery TCP SHALL support benchmarked batching policies.

#### Scenario: Throughput profile
- **WHEN** throughput-oriented batching is enabled
- **THEN** throughput SHALL improve without breaking queue or control-stream correctness

#### Scenario: Low-latency profile
- **WHEN** low-latency batching is configured
- **THEN** flush behavior SHALL prioritize lower latency over maximum throughput
