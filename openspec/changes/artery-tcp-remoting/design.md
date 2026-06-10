## Context

Scala Akka 2.6 Artery is more than a wire format. It includes association lifecycle, UID-scoped handshake state, control/user/large streams, reliable system-message delivery, bounded queues, compression tables, and lane partitioning.

Akka.NET classic remoting currently uses:

- `EndpointWriter` / `EndpointReader`
- `AkkaProtocolTransport`
- `AkkaPduCodec`
- protobuf `Payload` / `AckAndEnvelopeContainer`
- `AssociationHandle.Write(ByteString)`

That path should remain for compatibility, but it should not be the high-throughput Artery path.

## Goals / Non-Goals

**Goals:**

- Add new Artery-style remoting beside classic remoting.
- Use `SerializerV2` for payloads.
- Start with plaintext TCP.
- Implement handshake, UID tracking, association state, ordinary stream, control stream, and reliable system-message delivery.
- Preserve classic remoting independently.
- Build a protocol that can later host QUIC.

**Non-Goals:**

- Classic wire compatibility for Artery.
- Removing classic remoting.
- Implementing QUIC in 1.6.
- Making Akka.Streams TCP the Artery MVP hot path.
- Implementing compression before basic association and system-message correctness.

## Decisions

### 1. New RemoteTransport Implementation

Introduce `ArteryRemoting : RemoteTransport` rather than forcing Artery through `AkkaProtocolTransport`.

Rationale: Artery needs association state, stream separation, reliable system-message delivery, bounded queues, and compression table lifecycle that do not fit the classic `AssociationHandle.Write(ByteString)` abstraction.

### 2. Direct TCP/Pipelines Hot Path

Use dedicated TCP/socket or `System.IO.Pipelines` loops for the Artery MVP instead of materialized Akka.Streams TCP.

Rationale: current Akka.Streams TCP still pays actor/materializer overhead and Akka.IO copies pipe reads before delivery. Artery's transport hot path should own framing, queueing, batching, and backpressure directly.

### 3. Scala Artery TCP Framing As Baseline

Use the Scala Akka 2.6 TCP framing idea:

- connection header: magic bytes `AKKA` plus 1-byte stream ID,
- frame header: 4-byte little-endian frame length.

Rationale: this is simple, proven, and separates control/ordinary/large streams cleanly.

### 4. Artery Envelope Separate From SerializerV2

`SerializerV2` serializes payloads. The Artery envelope owns remoting metadata: protocol version, flags, origin UID, serializer ID, manifest, sender, recipient, control/system markers, and payload boundaries.

Rationale: the serializer contract and remoting protocol evolve separately.

### 5. Control Stream Before Lanes

Implement control stream and reliable system-message delivery before ordinary message lanes.

Rationale: correctness before throughput. System messages must not be starved by user traffic.

### 6. UID-Scoped State

Handshake, quarantine, compression tables, and reliable system-message state are scoped by remote address + UID/incarnation.

Rationale: stale state after remote ActorSystem restart can corrupt actor refs, manifests, and system-message delivery.

### 7. Bounded Queues

Outbound queues must be bounded. User traffic overflow should be deterministic; control/system overflow should trigger quarantine semantics.

Rationale: classic remoting's unbounded endpoint buffers are not acceptable for the new transport path.

## Risks / Trade-offs

**Ordering**: lanes can violate actor ordering if partitioning is wrong. Start with one ordinary stream, then add lane tests before enabling multiple lanes.

**Remote lifecycle compatibility**: quarantine, DeathWatch, and remote deployment expectations are subtle. Keep tests close to classic behavior.

**System-message reliability**: ACK/NACK and resend logic must be correct before performance tuning.

**Protocol churn**: sourcegen must validate V2 before envelope format is locked down.

**Backpressure behavior**: bounded queues may expose problems that classic remoting used to hide by buffering.
