# Protocol State Machine Map

## Current Authoritative Implementation

`ProtocolStateActor` remains the authoritative implementation for classic Remote protocol transitions. The focused tests in `AkkaProtocolSpec` now pin the current behavior for handshake readiness, heartbeat handling, disassociation, quarantined UID refusal, quarantined disassociate propagation, and listener registration buffering before any stream-friendly state model replaces actor FSM hot-path handling.

## Pure Transition Candidates

These `ProtocolStateActor` decisions can be represented as pure state transitions if side effects are supplied by an adapter:

- `Closed + HandleMsg + OutboundUnassociated`: write local `Associate`, start heartbeat, transition to `WaitHandshake + OutboundUnderlyingAssociated`, or retry if the write fails.
- `WaitHandshake + InboundPayload(Associate) + OutboundUnderlyingAssociated`: reject matching `refuseUid` with `DisassociateInfo.Quarantined`, otherwise heartbeat the failure detector, cancel handshake timeout, and transition to `Open + AssociatedWaitHandler`.
- `WaitHandshake + InboundPayload(Associate) + InboundUnassociated`: reply with local `Associate`, heartbeat the failure detector, start heartbeat timer, cancel handshake timeout, and transition to `Open + AssociatedWaitHandler`.
- `WaitHandshake + InboundPayload(Disassociate)`: stop without sending a disassociate loopback and preserve the incoming `DisassociateInfo` as the failure reason.
- `WaitHandshake + unexpected PDU`: send `DisassociateInfo.Unknown` and stop.
- `Open + InboundPayload(Heartbeat)`: heartbeat the failure detector and stay open without notifying the listener.
- `Open + InboundPayload(Payload) + AssociatedWaitHandler`: heartbeat the failure detector and enqueue payload bytes until a listener is registered.
- `Open + InboundPayload(Payload) + ListenerReady`: heartbeat the failure detector and notify the listener with the unwrapped payload bytes.
- `Open + InboundPayload(Disassociate)`: stop and preserve the incoming `DisassociateInfo` for listener notification.
- `Open + DisassociateUnderlying`: write a disassociate PDU with the supplied reason and stop.
- `Open + HandleListenerRegistered + AssociatedWaitHandler`: flush queued payloads FIFO and transition to `ListenerReady`.
- `HeartbeatTimer`: if the failure detector is available, write heartbeat and stay; otherwise write `DisassociateInfo.Unknown` and stop with timeout reason.
- `HandshakeTimer`: write `DisassociateInfo.Unknown` and stop with timeout reason while handshaking.

## Side Effects To Isolate

A pure model should not directly own these effects:

- PDU writes: `Associate`, `Heartbeat`, `Disassociate`, and payload unwrap decisions.
- Timer lifecycle: heartbeat timer, handshake timer, and retry timer scheduling/cancellation.
- Failure detector calls: `HeartBeat()` and `IsAvailable` checks.
- Listener effects: inbound association notification, handle listener registration, queued payload delivery, and final `Disassociated` notification.
- Promise effects: outbound association completion and failure propagation.
- Wrapped handle lifecycle: read handler installation and underlying `Disassociate` reason text.
- Logging and transport error publication.

## Listener Registration Contract

The current listener contract must be preserved by any stream-oriented replacement:

- A completed protocol handshake exposes an `AkkaProtocolHandle` immediately, but inbound payload delivery waits for `ReadHandlerSource` to complete.
- Payloads received before listener registration are buffered in `AssociatedWaitHandler.Queue` and delivered FIFO once `HandleListenerRegistered` is processed.
- Heartbeats received before listener registration update liveness but are not delivered to the listener.
- If the association closes before listener registration, `OnTermination` attaches a continuation to the listener task and sends a single `Disassociated` notification when the listener eventually registers.
- If the listener is already ready, disassociation is delivered immediately as `Disassociated` with the preserved `DisassociateInfo` when available.

## Extraction Boundary

The first safe extraction boundary is a small internal transition model that accepts the current state data, decoded PDU or control event, and a side-effect sink. `ProtocolStateActor` can then remain as the mailbox/timer/promise adapter while equivalence tests prove that the model emits the same transition and effect sequence as the current FSM.
