## ADDED Requirements

### Requirement: Explicit experimental transport selection

Artery SHALL offer an explicitly selected experimental QUIC adapter while retaining TCP as the default. QUIC SHALL fail visibly for unsupported platforms, missing native dependencies, unusable credentials or transport mismatch, without falling back to TCP or plaintext.

#### Scenario: Existing configuration
- **WHEN** an application keeps its current Artery TCP or TLS configuration
- **THEN** its transport selection, TCP wire bytes and TLS behavior SHALL remain unchanged

#### Scenario: Unsupported QUIC environment
- **WHEN** an application explicitly selects QUIC but listener or connection support is unavailable
- **THEN** startup SHALL report the missing capability and SHALL NOT bind a substitute transport

### Requirement: Shared Artery protocol

TCP and QUIC SHALL use the same envelope codec, payload serialization, handshake, UID/quarantine rules, system-message delivery state and inbound processing behavior. Transport adapters SHALL own native byte I/O and lifetimes, without duplicating protocol state machines.

#### Scenario: Same envelope on either transport
- **WHEN** equivalent messages are encoded with identical protocol settings
- **THEN** their Artery frame bytes SHALL be identical before transport encryption and packetization

#### Scenario: Future telemetry and compression work
- **WHEN** telemetry headers or actor-ref/manifest compression are integrated
- **THEN** their protocol behavior SHALL be implemented in the shared pipeline and exercised on both transports, including cross-stream table readiness and incarnation changes

### Requirement: Authenticated stream mapping

The initial QUIC topology SHALL use one connection per outbound peer direction with bounded long-lived unidirectional control, ordinary and optional large-message streams. It SHALL authenticate with TLS 1.3 and ALPN `akka-artery-quic-prototype-v1` before admitting Artery traffic, reuse existing certificate-policy semantics, and retain the Artery handshake before releasing user traffic.

#### Scenario: Mutual authentication failure
- **WHEN** a required client certificate is absent or either peer rejects the presented certificate
- **THEN** the connection SHALL fail without delivering actor messages, and the listener SHALL remain usable by a valid peer

#### Scenario: Stream arrives before control processing
- **WHEN** ordinary stream bytes arrive before the association handshake completes
- **THEN** user messages SHALL remain subject to shared handshake admission rules

#### Scenario: Stream framing
- **WHEN** a QUIC channel is opened
- **THEN** it SHALL carry the existing Artery role preamble and length-prefixed envelopes, supporting fragmented reads and rejecting malformed or oversized frames

### Requirement: Bounded resources and control progress

QUIC SHALL bound pending authentication, accepted connections, streams, managed buffering and admission deadlines, preserve existing queue overflow policy, and allow control processing independently of blocked data readers/writers. Native and connection-level limits SHALL be included in overload evaluation.

#### Scenario: Slow data receiver
- **WHEN** data queues reach their configured bounds while the peer still processes control traffic
- **THEN** buffering SHALL stay bounded, overflow SHALL follow the existing policy, and control/system-message progress SHALL meet the test's explicit deadline

#### Scenario: Excess incoming streams
- **WHEN** a peer exceeds the configured stream/admission limit or opens an unsupported channel
- **THEN** the adapter SHALL reject the excess without allocating unbounded tasks or buffers

### Requirement: Ordered ordinary-stream experiment

The prototype SHALL first establish a one-ordinary-stream baseline. A subsequent multiple-stream mode SHALL keep processing-lane counts and transport-stream counts independently identifiable, use stable recipient routing with FIFO writers, and preserve the existing sender-to-recipient ordering guarantee within a healthy connection generation.

#### Scenario: Multiple recipients and streams
- **WHEN** concurrent senders send numbered sequences with multiple processing lanes and ordinary QUIC streams
- **THEN** each sender-to-recipient sequence SHALL retain order and no message SHALL be striped round robin across streams

### Requirement: Connection failure and cancellation

An active channel failure/reset in the first prototype SHALL retire its directional connection, cancel sibling channel I/O and fence stale callbacks before replacement. Reconnect SHALL retain existing same-UID system-message reliability and changed-UID quarantine/reset semantics, without promising reliable replay of uncertain ordinary traffic. Shutdown SHALL remain bounded and release transport-owned resources.

Successful receive-processing completion SHALL include the inbound sink and all of that stream's lane consumers. Source cancellation/failure and downstream processing failure SHALL be observed independently, so blocked application work cannot prevent native connection retirement. This local completion boundary SHALL NOT be treated as a peer delivery receipt or actor business-processing completion.

#### Scenario: Downstream processing fails after FIN
- **WHEN** the native source has consumed a healthy FIN but downstream receive processing fails
- **THEN** the adapter SHALL retire that connection generation and cancel sibling channel I/O

#### Scenario: Source stops while processing is blocked
- **WHEN** the source is cancelled or fails while a downstream consumer remains blocked
- **THEN** native retirement SHALL NOT wait for that consumer, and its eventual completion SHALL remain observed

#### Scenario: Graph stops with queued lane work
- **WHEN** the inbound graph stops with accepted work remaining in an ordinary lane
- **THEN** successful consumer completion SHALL remain pending until every lane drains, including after upstream failure or downstream cancellation

#### Scenario: Same-UID reconnect with lost acknowledgement
- **WHEN** the connection fails with an unacknowledged system message and the peer retains its UID
- **THEN** shared reliable delivery SHALL retry according to its existing policy and prevent duplicate system-message delivery

#### Scenario: Cancelled connection completes late
- **WHEN** an old connect/read/write completion arrives after cancellation or connection replacement
- **THEN** it SHALL NOT admit old traffic, retire the replacement or retain transport-owned buffers

### Requirement: Equivalent and reproducible comparison

The experiment SHALL compare plaintext TCP, TLS TCP and QUIC using the same integrated Artery workload and protocol settings, with equivalent authentication for the encrypted arms. It SHALL record delivered goodput, latency distributions, control progress, CPU, managed/native memory and invalidating drops/duplicates/application resends. Real-network conclusions SHALL use separate processes on separate hosts and documented configurations.

#### Scenario: Clean performance row loses messages
- **WHEN** a clean comparison run drops, duplicates or application-replays messages, or fails a correctness assertion
- **THEN** that row SHALL be marked invalid rather than accepted as a transport performance win

#### Scenario: Fault experiment retransmits packets
- **WHEN** controlled packet loss causes QUIC's native reliable transport to retransmit
- **THEN** the report SHALL distinguish native packet recovery from application retries and disclose delivered outcomes and recovery latency

#### Scenario: Compression or telemetry differs
- **WHEN** the compared transports use different compression, tracing or serialization settings
- **THEN** the report SHALL NOT attribute the resulting difference solely to the transport

#### Scenario: No measured improvement
- **WHEN** QUIC provides no useful measured advantage or fails control-progress/resource gates
- **THEN** the report SHALL retain that negative finding without weakening correctness requirements or changing the default transport
