## Context

Akka.IO already bridges sockets and streams through `TcpTransportConnection`, which owns the stream and the I/O pumps. TLS therefore belongs in that transport: `InitializeAsync` authenticates an `SslStream` before reporting readiness, then `Start` activates the existing pumps when the handler registers. The handshake must not block the listener's accept loop or the actor mailbox.

This work is staged. Akka.IO owns reusable certificate and validation settings; Akka.Streams and Artery add their own entry points in later changes. DotNetty remains an independent transport and is not reconfigured through the new API.

## Goals / Non-Goals

**Goals:**
- Opt-in TLS for each Akka.IO outgoing connection or listener.
- Make client-only, client-mutual, listener-only, and listener-mutual settings explicit through role-specific factories.
- Separate the trust decision from optional checks that can only narrow acceptance.
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

### 2. Settings express the TLS role and are immutable

**Decision:** Construct outgoing `TlsClientSettings` with `ServerOnly(serverValidation)` or `Mutual(clientCertificate, serverValidation)`. Construct listener `TlsServerSettings` with `ServerOnly(serverCertificate)` or `Mutual(serverCertificate, clientValidation)`. Use immutable `WithHandshakeTimeout`, `WithProtocols`, and client-only `WithTargetHost` methods for overrides. Do not expose mutable validation, hostname, mutual-authentication, or nullable custom-validator flags.

**Rationale:** The constructor inputs make each direction's local certificate and peer policy explicit. A listener has no outbound target-host setting, and server-only client settings do not require a client certificate.

### 3. Peer trust has one base decision and optional narrowing checks

**Decision:** `TlsPeerPolicy.SystemTrust()` uses OS chain trust and ignores only name-mismatch errors; `PinnedCertificates(...)` treats a matching leaf thumbprint as trust; `CustomTrust(callback)` supplies the complete trust decision. `And(...)` checks run only after the base trust accepts and can only reject additional peers. Preserve all seven common validation helpers. Reject missing required peer certificates before callbacks; propagate callback exceptions as handshake failures.

**Rationale:** Trust anchors, leaf pins, and application checks have different meanings. Making the base trust choice explicit avoids accidentally treating a subject or issuer match as a trust anchor.

### 4. Target host is an optional outgoing override

**Decision:** By default, derive the outgoing TLS target/SNI name from the remote connection endpoint. `WithTargetHost` overrides that name when the endpoint address differs from the intended DNS identity. `ValidateHostname()` checks the runtime name-mismatch result; the overload with an expected host checks that explicit DNS or IP name. Listener policies never infer a client identity from its socket address.

**Rationale:** Ordinary DNS connections already provide the target name. Keeping the override on client settings supports IP/alias endpoints without adding unused state to listeners.

### 5. Authenticate before reporting Connected

**Decision:** Outgoing and per-accepted-connection actors initialize a transport and process its generic readiness result through their mailboxes. The TCP transport runs TLS authentication away from the actor thread. Only successful readiness calls the existing `CompleteConnect` path. A client failure completes the original command with `Tcp.CommandFailed`; an inbound failure is logged and stops only that child actor.

**Rationale:** This prevents unauthenticated bytes from reaching registered handlers, keeps actor responsiveness, and leaves the accept loop available while a peer stalls.

### 6. Keep transport ownership until Register

**Decision:** `ITransportConnection` exposes `InitializeAsync(CancellationToken)` and `Start()`. `TcpTransportConnection` owns its stream, handshake cancellation, and socket throughout initialization and while awaiting `Tcp.Register`. `PostStop` aborts the transport, which cancels authentication and disposes pre-registration resources. The actor sees only the generic initialization result; registration starts the transport's deferred pumps. Direct users of the beta `TcpTransportConnection` API must now call `InitializeAsync` and then `Start`.

**Rationale:** A task result can arrive after actor shutdown, and `Connected` does not yet create transport pumps. Ownership must cover both gaps without a late result leaking a live socket.

### 7. Preserve TCP half-close over TLS

**Decision:** After queued output has flushed, call `SslStream.ShutdownAsync` to send TLS `close_notify`, then send the TCP FIN. Keep the stream read side available until the existing close state machine completes. Full close performs TLS shutdown before canceling reads and disposing the stream.

**Rationale:** `Tcp.ConfirmedClose` must continue receiving data after the local write side closes. Current .NET runtime diagnostics showed a peer can observe TLS EOF and still send a response for TLS 1.2 and 1.3; Akka.IO integration tests verify this behavior.

## Risks / Trade-offs

- A TLS handshake may complete concurrently with actor shutdown. The transport serializes stream publication against abort and disposes any stream created after abort; a late generic mailbox result cannot report `Tcp.Connected` after stop.
- TLS close behavior depends on `SslStream` runtime semantics. Exercise both TLS 1.2 and TLS 1.3 with real peers and preserve the existing confirmed-close and plaintext regression tests.
- The client hostname check is opt-in for compatibility. Documentation recommends composing `ValidateHostname()` when server-name checking is required; `WithTargetHost` is needed only when the endpoint address does not provide the intended DNS identity.
