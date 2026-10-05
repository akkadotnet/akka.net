## Context

Akka.IO already bridges sockets and streams through `TcpTransportConnection`, which owns the stream and the I/O pumps. TLS therefore belongs in that transport: it authenticates an `SslStream` before reporting generic readiness, then starts the existing pumps when the handler registers. The handshake must not block the listener's accept loop or the actor mailbox.

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

**Decision:** Outgoing and per-accepted-connection actors initialize a transport and process its generic readiness result through their mailboxes. The TCP transport runs TLS authentication away from the actor thread. Only successful readiness calls the existing `CompleteConnect` path. A client failure completes the original command with `Tcp.CommandFailed`; an inbound failure is logged and stops only that child actor.

**Rationale:** This prevents unauthenticated bytes from reaching registered handlers, keeps actor responsiveness, and leaves the accept loop available while a peer stalls.

### 3. Keep transport ownership until Register

**Decision:** `TcpTransportConnection` owns its stream, handshake cancellation, and socket throughout initialization and while awaiting `Tcp.Register`. `PostStop` aborts the transport, which cancels authentication and disposes pre-registration resources. The actor sees only the generic transport readiness result; registration activates the transport's deferred pumps.

**Rationale:** A task result can arrive after actor shutdown, and `Connected` does not yet create transport pumps. Ownership must cover both gaps without a late result leaking a live socket.

### 4. Preserve TCP half-close over TLS

**Decision:** After queued output has flushed, call `SslStream.ShutdownAsync` to send TLS `close_notify`, then send the TCP FIN. Keep the stream read side available until the existing close state machine completes. Full close performs TLS shutdown before canceling reads and disposing the stream.

**Rationale:** `Tcp.ConfirmedClose` must continue receiving data after the local write side closes. Current .NET runtime diagnostics showed a peer can observe TLS EOF and still send a response for TLS 1.2 and 1.3; Akka.IO integration tests verify this behavior.

## Risks / Trade-offs

- A TLS handshake may complete concurrently with actor shutdown. The transport serializes stream publication against abort and disposes any stream created after abort; a late generic mailbox result cannot report `Tcp.Connected` after stop.
- TLS close behavior depends on `SslStream` runtime semantics. Exercise both TLS 1.2 and TLS 1.3 with real peers and preserve the existing confirmed-close and plaintext regression tests.
- The client hostname check is opt-in for compatibility. Documentation must recommend enabling `ValidateCertificateHostname` and setting `TargetHost` when the application requires server identity checking.
