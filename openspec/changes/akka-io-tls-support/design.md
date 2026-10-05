## Context

Akka.IO already bridges sockets and streams through `TcpTransportConnection`, which can consume an existing `Stream`. TLS therefore belongs at the Akka.IO connection lifecycle: authenticate an `SslStream` before exposing `Tcp.Connected`, then give that authenticated stream to the existing transport when the handler registers. The handshake must not block the listener's accept loop or the actor mailbox.

This work is staged. Akka.IO owns reusable certificate and validation settings; Akka.Streams and Artery add their own entry points in later changes. DotNetty remains an independent transport and is not reconfigured through the new API.

## Goals / Non-Goals

**Goals:**
- Opt-in TLS for each Akka.IO outgoing connection or listener.
- Asynchronous, bounded client and server handshakes before `Tcp.Connected`.
- Preserve cancellation and socket/stream ownership when a connection actor stops before registration.
- Keep per-connection validation options independent and preserve plaintext behavior when TLS is not configured.
- Send TLS `close_notify` before TCP FIN during `Tcp.ConfirmedClose`, while keeping the read side open.

**Non-Goals:**
- `IStreamProvider` or replacement of the existing socket/pipe transport.
- Akka.Streams and Artery integration in this stage.
- DotNetty configuration migration or API changes.
- QUIC, certificate file/store loading, certificate hot reload, or automatic plaintext fallback.

## Decisions

### 1. TLS is selected per TCP command

**Decision:** Add init-only `TlsClientSettings? Tls` and `TlsServerSettings? Tls` properties to `Tcp.Connect` and `Tcp.Bind`. Keep existing constructors unchanged.

**Rationale:** A system can host TLS and plaintext connections concurrently, and existing callers remain source and binary compatible.

### 2. Authenticate before reporting Connected

**Decision:** Outgoing and per-accepted-connection actors run their TLS authentication tasks off the actor thread and process completion through their mailboxes. Only successful authentication calls the existing `CompleteConnect` path. A client failure completes the original command with `Tcp.CommandFailed`; an inbound failure is logged and stops only that child actor.

**Rationale:** This prevents unauthenticated bytes from reaching registered handlers, keeps actor responsiveness, and leaves the accept loop available while a peer stalls.

### 3. Keep explicit stream ownership until Register

**Decision:** The connection actor owns its authenticated stream while a handshake is pending and while awaiting `Tcp.Register`. `PostStop` cancels authentication and disposes pre-registration resources. `StartTransport` transfers the stream to the existing `TcpTransportConnection`, which owns it from then on.

**Rationale:** A task result can arrive after actor shutdown, and `Connected` does not yet create transport pumps. Ownership must cover both gaps without a late result leaking a live socket.

### 4. Preserve TCP half-close over TLS

**Decision:** After queued output has flushed, call `SslStream.ShutdownAsync` to send TLS `close_notify`, then send the TCP FIN. Keep the stream read side available until the existing close state machine completes. Full close performs TLS shutdown before canceling reads and disposing the stream.

**Rationale:** `Tcp.ConfirmedClose` must continue receiving data after the local write side closes. Current .NET runtime diagnostics showed a peer can observe TLS EOF and still send a response for TLS 1.2 and 1.3; Akka.IO integration tests verify this behavior.

## Risks / Trade-offs

- A TLS handshake may complete concurrently with actor shutdown. The actor retains stream ownership before starting the task and disposes it from `PostStop`, so a late mailbox result cannot transfer ownership after stop.
- TLS close behavior depends on `SslStream` runtime semantics. Exercise both TLS 1.2 and TLS 1.3 with real peers and preserve the existing confirmed-close and plaintext regression tests.
- The client hostname check is opt-in for compatibility. Documentation must recommend enabling `ValidateCertificateHostname` and setting `TargetHost` when the application requires server identity checking.
