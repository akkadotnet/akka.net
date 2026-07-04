## 0. Transport Substrate Validation Gate (do FIRST — see design Decision 2)

BenchmarkDotNet harness (naked, baseline-first, `MemoryDiagnoser` on), socket bypassed (push pre-serialized fixed-size `ByteString`s through the graph), deserialize as a tunable CPU knob (hash M bytes). Three head-to-head configs:

- [x] 0.1 **Config 1 — actor-only baseline**: hand-written producer → decode → N-lanes → sink `Tell` chain (mailbox hops, no interpreter). The floor; mirrors DotNetty's structure
- [x] 0.2 **Config 2 — single-island streams** (fused, NO lanes): drain-many source → framing → decode → deserialize → `Sink.ActorRef`. Yields the raw interpreter tax AND the **serial-island ceiling — the single most important number: must exceed ~680K with comfortable margin** (bounds a single-connection target)
- [x] 0.3 **Config 3 — lane streams**: `… → Partition(N) → [.Async → deserialize]×N → sink`, sweep N ∈ {1,2,4,8,16}; confirm lane-scaling linearity
- [x] 0.3b **Config 4 — hybrid island + actor lanes** (added after smoke runs measured the `.Async()` boundary at ~500ns/msg, non-recoverable via deeper boundary buffers): fused island (framing+decode) → `Sink.ForEach` `Tell` to N lane actors → recipients; tests whether the cheaper mailbox hop restores lane scaling
- [x] 0.4 Run configs 2/3 with stock `Source.Queue` vs. the custom drain-many source to quantify the ingress-hop penalty
- [x] 0.5 Measure: sustained msgs/sec, CPU-ns/msg vs cores, allocations/msg (~1 boxing/msg per boundary; steady-state near-zero otherwise), the config-2 serial ceiling, lane-scaling linearity, and reproduce the ~+30% tax vs config-1
- [x] 0.6 **Gate decision**: proceed with `Akka.Streams.IO.Tcp` only if (i) the single decode/partition island clears 680K with margin AND (ii) lanes recover the +30% within the core budget at realistic deserialize cost. Else → materializer/stage surgery, or a `System.IO.Pipelines` substrate fallback (Artery protocol/stages unchanged). No number of lanes fixes a serial-island ceiling

## 1. Configuration And Entry Point

- [ ] 1.1 Add Artery remoting configuration section
- [ ] 1.2 Add config switch for classic vs Artery remoting
- [ ] 1.3 Introduce `ArteryRemoting : RemoteTransport`
- [ ] 1.4 Wire `RemoteActorRefProvider` to select remoting implementation from config
- [ ] 1.5 Preserve classic remoting default until Artery is explicitly enabled

## 2. TCP Framing Foundation

- [ ] 2.1 Define stream IDs for control, ordinary, and future large-message streams
- [ ] 2.2 Implement connection header: `AKKA` magic + 1-byte stream ID
- [ ] 2.3 Implement 4-byte little-endian frame length encoder
- [ ] 2.4 Implement frame parser over `ReadOnlySequence<byte>`
- [ ] 2.5 Enforce maximum frame size
- [ ] 2.6 Add framing tests for complete, partial, multiple, and oversized frames

## 3. Artery Envelope Codec

- [ ] 3.1 Define MVP envelope header fields
- [ ] 3.2 Encode protocol version and flags
- [ ] 3.3 Encode origin UID
- [ ] 3.4 Encode serializer ID and manifest literal
- [ ] 3.5 Encode recipient actor ref literal or no-recipient marker
- [ ] 3.6 Encode sender actor ref literal or no-sender marker
- [ ] 3.7 Encode payload using `SerializerV2`
- [ ] 3.8 Decode envelope metadata without deserializing payload first
- [ ] 3.9 Add envelope codec tests, including multi-segment input

## 4. Association State And Handshake

- [ ] 4.1 Add association registry keyed by remote address
- [ ] 4.2 Track remote UID and association incarnation
- [ ] 4.3 Implement outbound handshake request
- [ ] 4.4 Implement inbound handshake response
- [ ] 4.5 Reject wrong target address during handshake
- [ ] 4.6 Reset UID-scoped state on remote UID change
- [ ] 4.7 Add handshake timeout and retry behavior
- [ ] 4.8 Add quarantine state keyed by UID

## 5. Plaintext TCP Transport

- [ ] 5.1 Implement TCP listener for inbound Artery connections (via `Akka.Streams.IO.Tcp` `Tcp().Bind`)
- [ ] 5.2 Implement outbound TCP connection creation (via `Tcp().OutgoingConnection`)
- [ ] 5.3 Attach inbound connection to stream by stream ID
- [ ] 5.4 Add ordinary stream send path
- [ ] 5.5 Add ordinary stream receive path
- [ ] 5.6 Dispatch decoded messages to recipients
- [ ] 5.7 Add basic two-ActorSystem remoting test over Artery TCP

## 6. Control Stream

- [ ] 6.1 Add outbound control queue
- [ ] 6.2 Add inbound control stream processing
- [ ] 6.3 Route handshake messages over control stream
- [ ] 6.4 Add liveness/heartbeat control messages
- [ ] 6.5 Add quarantine control message
- [ ] 6.6 Ensure control stream cannot be starved by ordinary messages

## 7. Reliable System Messages

- [ ] 7.1 Define system-message envelope
- [ ] 7.2 Add sequence numbers for outbound system messages
- [ ] 7.3 Add ACK/NACK control messages
- [ ] 7.4 Add resend timer
- [ ] 7.5 Add bounded system-message buffer
- [ ] 7.6 Quarantine on system-message buffer overflow
- [ ] 7.7 Quarantine or fail association after give-up timeout
- [ ] 7.8 Add duplicate and out-of-order system-message tests
- [ ] 7.9 Verify DeathWatch and remote deployment system-message behavior

## 8. Bounded Queues And Backpressure

- [ ] 8.1 Add bounded ordinary outbound queue
- [ ] 8.2 Add bounded control outbound queue
- [ ] 8.3 Define overflow behavior for user messages
- [ ] 8.4 Define overflow behavior for control/system messages
- [ ] 8.5 Add slow receiver tests proving queues do not grow unbounded

## 9. Lifecycle And Compatibility Tests

- [ ] 9.1 Verify classic remoting still works when Artery is disabled
- [ ] 9.2 Verify Artery remoting starts and stops cleanly
- [ ] 9.3 Verify remote association restart with new UID resets state
- [ ] 9.4 Verify quarantine events are published correctly
- [ ] 9.5 Verify cluster formation with Artery TCP if cluster scope is included in MVP

## 10. Deferred Follow-Ups

- [ ] 10.1 Add ordinary message lanes after ordering tests are designed (hub-based fan-out per design rule 3; G5-entry re-baseline decides stock `PartitionHub` vs fixed-size hub port vs bounded actor lanes)
- [ ] 10.2 Add large-message stream
- [ ] 10.3 Add actor ref compression
- [ ] 10.4 Add manifest compression
- [ ] 10.5 Add TLS wrapping
- [ ] 10.6 Prepare QUIC transport adapter for 1.7
