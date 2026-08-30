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

- [x] 2.1 Define stream IDs for control, ordinary, and future large-message streams
- [x] 2.2 Implement connection header: `AKKA` magic + 1-byte stream ID
- [x] 2.3 Implement 4-byte little-endian frame length encoder
- [x] 2.4 Implement frame parser over `ReadOnlySequence<byte>`
- [x] 2.5 Enforce maximum frame size (hard cap 0x00FFFFFF protects the 24-bit literal-offset tag space)
- [x] 2.6 Add framing tests for complete, partial, multiple, and oversized frames

## 3. Artery Envelope Codec

- [x] 3.1 Define MVP envelope header fields (32-byte fixed header; ◇ sub-decisions closed — see design.md)
- [x] 3.2 Encode protocol version and flags
- [x] 3.3 Encode origin UID
- [x] 3.4 Encode serializer ID and manifest literal
- [x] 3.5 Encode recipient actor ref literal or no-recipient marker
- [x] 3.6 Encode sender actor ref literal or no-sender marker
- [x] 3.7 Encode payload using `SerializerV2` (single-pass: manifest upfront, payload streamed via `IBufferWriter`, frame length back-patched)
- [x] 3.8 Decode envelope metadata without deserializing payload first
- [x] 3.9 Add envelope codec tests, including multi-segment input

## 4. Association State And Handshake

- [x] 4.1 Add association registry keyed by remote address
- [x] 4.2 Track remote UID and association incarnation
- [x] 4.3 Implement outbound handshake request
- [x] 4.4 Implement inbound handshake response
- [x] 4.5 Reject wrong target address during handshake
- [x] 4.6 Reset UID-scoped state on remote UID change
- [x] 4.7 Add handshake timeout and retry behavior
- [x] 4.8 Add quarantine state keyed by UID

## 5. Plaintext TCP Transport

- [x] 5.1 Implement TCP listener for inbound Artery connections (via `Akka.Streams.IO.Tcp` `Tcp().Bind`; `halfClose: true` required — accepted connections are read-only)
- [x] 5.2 Implement outbound TCP connection creation (via `Tcp().OutgoingConnection`)
- [x] 5.3 Attach inbound connection to stream by stream ID (G2: Ordinary accepted; control/large logged + dropped until G3/large work)
- [x] 5.4 Add ordinary stream send path
- [x] 5.5 Add ordinary stream receive path
- [x] 5.6 Dispatch decoded messages to recipients
- [x] 5.7 Add basic two-ActorSystem remoting test over Artery TCP

## 6. Control Stream

- [x] 6.1 Add outbound control queue
- [x] 6.2 Add inbound control stream processing
- [x] 6.3 Route handshake messages over control stream
- [x] 6.4 Add liveness/heartbeat control messages
- [x] 6.5 Add quarantine control message
- [x] 6.6 Ensure control stream cannot be starved by ordinary messages

## 7. Reliable System Messages

- [x] 7.1 Define system-message envelope
- [x] 7.2 Add sequence numbers for outbound system messages
- [x] 7.3 Add ACK/NACK control messages
- [x] 7.4 Add resend timer
- [x] 7.5 Add bounded system-message buffer
- [x] 7.6 Quarantine on system-message buffer overflow
- [x] 7.7 Quarantine or fail association after give-up timeout
- [x] 7.8 Add duplicate and out-of-order system-message tests
- [x] 7.9 Verify DeathWatch and remote deployment system-message behavior

## 8. Bounded Queues And Backpressure

- [x] 8.1 Add bounded ordinary outbound queue (landed with group 6/7: `Association._outboundChannel`, `Channel.CreateBounded`, capacity `Association.DefaultOutboundQueueCapacity` = 3072 — `AssociationRegistry.cs:86,106,144-149`; `TryEnqueueOutbound` — `AssociationRegistry.cs:202`)
- [x] 8.2 Add bounded control outbound queue (landed with group 6/7: `Association._controlChannel`, capacity `Association.DefaultControlQueueCapacity` = 256 — `AssociationRegistry.cs:96,107,150-155`; `TryEnqueueControl` — `AssociationRegistry.cs:209`)
- [x] 8.3 Define overflow behavior for user messages (landed with group 6/7: ordinary overflow → dead letters, log-once per association/uid — `ArteryRemoting.EnqueueOutbound`, `ArteryRemoting.cs:443-463`, using `Association.ShouldLogOrdinaryOverflowDrop` — `AssociationRegistry.cs:256-257`)
- [x] 8.4 Define overflow behavior for control/system messages (landed with group 6/7: control/system overflow → quarantine, re-entrancy-guarded — `ArteryRemoting.HandleControlOverflow`, `ArteryRemoting.cs:537-551`, called from `EnqueueControl`/`EnqueueSystemMessage`, `ArteryRemoting.cs:481-482,514-515`)
- [x] 8.5 Add slow receiver tests proving queues do not grow unbounded (unit-level bounded-queue proofs added to `AssociationRegistrySpec.cs`; e2e slow/unresponsive-peer proofs added in new `ArteryBackpressureSpec.cs` — both under `src/core/Akka.Remote.Tests/Artery/`)

## 9. Lifecycle And Compatibility Tests

- [x] 9.1 Verify classic remoting still works when Artery is disabled (`ArteryConfigSpec.Should_SelectClassicRemoting_When_ArteryNotEnabled` / `Should_UseAkkaTcpScheme_When_ClassicRemotingIsActive`, pre-existing, unaffected by group 9)
- [x] 9.2 Verify Artery remoting starts and stops cleanly (`ArteryReconnectSpec.Should_CleanStartStop_ThreeSequentialCycles` -- 3 sequential create/terminate cycles on port 0)
- [x] 9.3 Verify remote association restart with new UID resets state (`ArteryReconnectSpec.Should_Reassociate_After_Peer_Restarts_With_New_Uid`: B restarts on the same port/name with a new uid; A re-associates automatically (group 9 reconnect), new incarnation + uid visible via the registry, post-restart ordinary sends reach the new incarnation, the pre-kill Watch's `Terminated` arrives via RemoteWatcher's failure detector, and a stale-uid `Quarantine` call for the superseded uid is correctly ignored, per `AssociationState.Quarantine`'s pre-existing G2 semantics). Implemented the reconnect mechanism itself (`ArteryRemoting.ScheduleOutboundRestart`, `Association.HasOutboundEverRestarted`/`HasControlEverRestarted`/`ShouldRestartOutbound`/`ShouldRestartControl`, `MaterializeOnceGate.Reset`/`HasEverRestarted`), moved `SystemMessageDeliveryStage`'s unacked buffer/seqNo to an Association-owned `SystemMessageDeliveryState` (invariant 3: survives restart), added `OutboundHandshakeStage.ForceReqOnStart` + `Association.HandshakeGeneration` (a restarted stream must always re-handshake, never trust stale "already associated" state), and fixed a general (non-Artery-specific) `Akka.IO.TcpConnection`/`TcpTransportConnection` bug where a write-pump I/O failure on an otherwise-idle connection was never proactively detected (see `WritePumpFailed`/`MonitorWritePumpAsync`). Also added `SystemMessageDeliveryStageSpec.Should_Survive_Unacked_Buffer_Across_Simulated_Stream_Restart` and `AssociationRestartSpec` (pure unit tests for the restart-decision/gate-reset/shutdown-guard logic).
- [x] 9.4 Verify quarantine events are published correctly (already covered: `ArteryBackpressureSpec` asserts `QuarantinedEvent` on the quarantining side; `ArteryControlStreamSpec.Should_Notify_Peer_On_Quarantine` asserts `ThisActorSystemQuarantinedEvent` on the quarantined side -- no gaps found, not extended)
- [x] 9.5 Verify cluster formation with Artery TCP if cluster scope is included in MVP (`Akka.Cluster.Tests.ArteryClusterFormationSpec.Should_Form_Cluster_Over_Artery_Via_Seed_Nodes` -- new spec, lives in `Akka.Cluster.Tests` since `Akka.Remote.Tests` does not reference `Akka.Cluster`; two nodes, `akka://` scheme seed node via the production seed-nodes bootstrap path, both reach `MemberStatus.Up` via `ClusterEvent.MemberUp` subscriptions, clean `CoordinatedShutdown`. No production changes were needed -- the `akka://` scheme and seed-node join path worked correctly against Artery out of the box)

Group 9 also covers design.md's "Association outbound-stream lifecycle: reconnect" section in full:
outbound-stream restart with unlimited retries at a fixed `advanced.outbound-restart-backoff`
(default 1s, new `ArterySettings`/`Remote.conf` key), the quarantined-association restart asymmetry
(control always restarts, ordinary does not while the current uid is quarantined), and the pinned
pre-restart ordinary-message semantics (buffered-but-undelivered envelopes survive and are
delivered in order -- see design.md's updated "Group 9 correctness suite" paragraph and
`ArteryReconnectSpec.Should_Redeliver_Queued_Ordinary_Messages_After_Reconnect`).

## 10. Deferred Follow-Ups

- [ ] 10.1 Add ordinary message lanes after ordering tests are designed (hub-based fan-out per design rule 3; G5-entry re-baseline decides stock `PartitionHub` vs fixed-size hub port vs bounded actor lanes)
- [x] 10.2 Add large-message stream (`akka.remote.artery.large-message-destinations` + `advanced.maximum-large-frame-size`/`large-buffer-pool-size`/`outbound-large-message-queue-size`, Pekko-faithful key names/defaults; feature is enabled only when `large-message-destinations` is non-empty). Destination matching reuses the EXISTING `Akka.Util.WildcardIndex`/`WildcardTree` port (already used by `Deployer`) rather than a new matcher -- exact path, single `*`, and trailing `**` all match Pekko's semantics. `Association` gained a third bounded channel/gate/kill switch (`_largeChannel`/`_largeGate`/`_largeOutboundKillSwitch`, always allocated -- see `Association.DefaultLargeQueueCapacity`'s remarks for why an always-allocated-but-unused channel is behavior-identical when the feature is off); `ArteryRemoting.EnqueueOutbound` routes to it with control > large > ordinary precedence (system/control messages structurally never reach this method, so they can never route to large regardless of path match); overflow is a soft drop (`Dropped` published to the event stream), mirroring the ordinary queue's #8346 fix, never a quarantine. The large outbound connection reuses `MaterializeOutboundStream` (parameterized by `ArteryStreamId.Large`) with its own dedicated `ArrayPool<byte>` (`ArrayPool<byte>.Create(MaximumLargeFrameSize, LargeBufferPoolSize)`). Inbound: `ArteryInboundProcessingStage` now accepts the `Large` preamble (previously logged + dropped) and defers constructing its `ArteryFrameParser` until the preamble reveals which frame-size limit applies. Tests: `ArteryLargeMessageStreamSpec.cs` (settings parsing/validation, destination-matcher exact/wildcard/double-wildcard/non-match, default-off gate, system-message-never-large regression, and an end-to-end loopback delivering a payload larger than the ordinary `maximum-frame-size` while a non-matching actor's ordinary traffic is unaffected). No ordering guarantee between the large stream and ordinary traffic to the same actor (separate connections) -- documented on `ArterySettings.LargeMessageDestinations` and in `Remote.conf`. No BREAKING_CHANGES_V1.6.md entry: default-off, so existing behavior is unchanged.
- [ ] 10.3 Add actor ref compression
- [ ] 10.4 Add manifest compression
- [ ] 10.5 Add TLS wrapping
- [ ] 10.6 Prepare QUIC transport adapter for 1.7
