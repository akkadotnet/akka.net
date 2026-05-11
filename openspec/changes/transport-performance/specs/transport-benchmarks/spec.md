## ADDED Requirements

### Requirement: DotNetty baseline established
A DotNetty performance baseline SHALL be captured using the RemotePingPong benchmark on the `dev` branch before any transport changes, documenting messages/second, P50/P99 latency, and allocation rate.

#### Scenario: Baseline capture
- **WHEN** the RemotePingPong benchmark is run against the current DotNetty transport on reference hardware
- **THEN** the results SHALL be recorded with hardware specs, .NET version, OS, and benchmark configuration

### Requirement: Integrated outbound write-loop spike benchmarked first
Before the full transport rewrite is completed, the system SHALL benchmark an integrated outbound write-loop spike against today's split remoting write path.

#### Scenario: Spike compares split vs integrated outbound paths
- **WHEN** the bounded spike benchmark is run
- **THEN** it SHALL compare the current remoting write path to a transport-owned writer loop that operates on send-shaped work items and builds the outbound frame in one pass

#### Scenario: Spike informs transport contract change
- **WHEN** the spike results are reviewed
- **THEN** they SHALL be used to decide whether the transport write contract should change to support the integrated outbound loop

### Requirement: First end-to-end comparison uses wire-compatible redesign
The first full transport comparison SHALL benchmark the redesigned outbound regime on the current remoting wire format before compatibility wrappers or alternate wire formats are considered.

#### Scenario: Performance-first end-to-end comparison
- **WHEN** the new transport is first compared to DotNetty
- **THEN** the measured implementation SHALL use the redesigned outbound path, preserve the current wire format, and avoid hot-path compatibility shims

### Requirement: New transport exceeds DotNetty throughput
The Akka.Streams-based transport with SerializerV2 SHALL achieve higher messages/second than the DotNetty baseline on the RemotePingPong benchmark.

#### Scenario: Throughput comparison
- **WHEN** the RemotePingPong benchmark is run against the new transport on the same hardware
- **THEN** messages/second SHALL exceed the DotNetty baseline

#### Scenario: Allocation rate comparison
- **WHEN** allocation rate (bytes/op) is measured for both transports
- **THEN** the new transport SHALL allocate fewer bytes per operation than DotNetty

### Requirement: Flush batching configurable
The write path SHALL support configurable flush batching to coalesce multiple writes before calling `stream.FlushAsync()`.

#### Scenario: Batched flush reduces syscalls
- **WHEN** multiple `Write` commands are pending within a batch window
- **THEN** they SHALL be written to the stream and flushed together in a single `FlushAsync()` call

#### Scenario: Flush batching configurable via HOCON
- **WHEN** `akka.remote.dot-netty.tcp.batching.flush-count` or `batching.flush-interval` is configured
- **THEN** the transport SHALL batch writes according to the configured thresholds

#### Scenario: Flush batching disabled
- **WHEN** flush batching is disabled (configured to flush after every write)
- **THEN** each `WriteAsync` SHALL be followed immediately by `FlushAsync`

### Requirement: Pipe backpressure thresholds tunable
The `Pipe` `pauseWriterThreshold` and `resumeWriterThreshold` SHALL be configurable and benchmarked at various settings.

#### Scenario: Threshold configuration
- **WHEN** `batching.pause-writer-threshold` and `batching.resume-writer-threshold` are configured in HOCON
- **THEN** the `Pipe` SHALL use these values for backpressure control

### Requirement: Continuous benchmark tracking
Each optimization commit SHALL include before/after benchmark numbers to track incremental progress and detect regressions.

#### Scenario: Optimization validated
- **WHEN** an optimization is implemented (flush batching, pool tuning, dispatch improvement)
- **THEN** the RemotePingPong benchmark SHALL be run and results compared to the previous iteration

#### Scenario: Regression detected
- **WHEN** a change causes messages/second to drop below the previous iteration
- **THEN** the change SHALL be investigated and either fixed or reverted
