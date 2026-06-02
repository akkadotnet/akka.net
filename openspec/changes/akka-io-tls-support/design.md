## Context

`modernize-akka-io-tcp` introduces `IStreamProvider`, an abstraction that returns a connected `Stream` from connection parameters. `TcpStreamProvider` returns a plain `NetworkStream`. The TCP connection actor reads/writes the `Stream` via `System.IO.Pipelines.Pipe` and never interacts with the socket directly. This design makes TLS a provider swap: `TlsStreamProvider` wraps the `NetworkStream` in `SslStream` and completes the TLS handshake inside `ConnectAsync`, returning an authenticated `Stream`.

The current DotNetty transport supports TLS via `DotNetty.Handlers.Tls.TlsHandler` which itself wraps `SslStream`. The HOCON configuration covers: file-based certificates (`ssl.certificate.path` + `password`), Windows certificate store lookup (`ssl.certificate.thumbprint` + `store-name` + `store-location`), mutual authentication, hostname validation, and validation suppression for development. All of these must continue to work unchanged.

Reference implementation: TurboMQTT `TlsStreamProvider.cs` (~60 lines) and `FakeMqttTlsTcpServer.cs` (server-side TLS handshake pattern).

## Goals / Non-Goals

**Goals:**
- TLS for outgoing connections via `TlsStreamProvider : IStreamProvider`
- TLS for incoming connections via server-side handshake in `TcpIncomingConnection`
- All existing DotNetty TLS HOCON configuration works without modification
- Programmatic TLS configuration equivalent to `DotNettySslSetup`
- Handshake timeout support (configurable, prevents malicious clients from hanging connections)
- Certificate validation: chain validation, hostname validation, suppression for dev
- Mutual TLS (client certificate required)

**Non-Goals:**
- TLS as an Akka.Streams BidiFlow stage (YAGNI — TLS is a socket-level concern)
- TLS for UDP (separate protocol — DTLS, out of scope)
- Custom TLS protocol implementations (we use .NET's `SslStream` exclusively)
- Certificate auto-renewal or ACME integration
- TLS session resumption optimization (rely on .NET runtime defaults)

## Decisions

### 1. Client-side TLS via TlsStreamProvider

**Decision:** Create `TlsStreamProvider : IStreamProvider` that wraps `TcpStreamProvider`, creates `SslStream`, and completes `AuthenticateAsClientAsync` inside `ConnectAsync`.

**Rationale:** Direct copy of proven TurboMQTT pattern. The TCP connection actor receives an authenticated `Stream` and never knows TLS is involved. Zero changes to the connection actor code path.

**Alternative considered:** TLS BidiFlow stage in Akka.Streams. Rejected — requires complex push/pull bridging, fake Stream pairs, handshake state machine. Hundreds of lines vs ~60 lines. TLS is an encrypted pipe, not a data transformation.

### 2. Server-side TLS handshake in TcpIncomingConnection

**Decision:** After `TcpListener` accepts a socket and creates `TcpIncomingConnection`, the incoming connection actor wraps the `NetworkStream` in `SslStream` and calls `AuthenticateAsServerAsync` before entering the `Connected` state. If handshake fails or times out, the actor stops itself.

**Rationale:** Keeps the listener's accept loop non-blocking. A slow or malicious TLS handshake doesn't prevent other connections from being accepted. Each connection handles its own handshake independently. If it fails, the per-connection actor dies — the listener is unaffected.

**Alternative considered:** Handshake in `TcpListener` before creating child actor. Rejected — blocks the accept loop during handshake, creates DoS vector.

### 3. Configuration reuses existing HOCON keys

**Decision:** Parse `akka.remote.dot-netty.tcp.ssl.*` keys into a new `TlsSettings` class that produces `SslClientAuthenticationOptions` and `SslServerAuthenticationOptions`.

**Rationale:** Zero user configuration changes. The HOCON key names stay the same even though the underlying transport moves away from DotNetty-specific TLS machinery. Users migrating to Akka.NET 1.6 don't need to rewrite their TLS config.

Config mapping:
- `ssl.certificate.path` + `ssl.certificate.password` → `new X509Certificate2(path, password)`
- `ssl.certificate.use-thumbprint-over-file` + `ssl.certificate.thumbprint` + `ssl.certificate.store-name` + `ssl.certificate.store-location` → `X509Store` lookup
- `ssl.require-mutual-authentication` → `SslServerAuthenticationOptions.ClientCertificateRequired`
- `ssl.suppress-validation` → `RemoteCertificateValidationCallback` that returns `true`
- `ssl.validate-certificate-hostname` → Custom callback checking SAN/CN
- `enable-ssl` → selects `TlsStreamProvider` vs `TcpStreamProvider`

### 4. Programmatic TLS setup via setup class

**Decision:** Provide a `TlsSetup` class (equivalent to current `DotNettySslSetup`) that can be passed via `ActorSystemSetup` for programmatic certificate configuration.

**Rationale:** Some users construct certificates in code (e.g., from Azure Key Vault, AWS Secrets Manager). They need a non-HOCON path. The current `DotNettySslSetup` provides this — the replacement must too.

### 5. Handshake timeout

**Decision:** Server-side TLS handshake has a configurable timeout (default: 10 seconds). If `AuthenticateAsServerAsync` doesn't complete within the timeout, the connection actor disposes the `SslStream` and stops itself.

**Rationale:** Without a timeout, a malicious client can connect and never send the ClientHello, consuming a connection actor indefinitely. The TurboMQTT implementation uses `CancellationTokenSource` with timeout linked to the auth call.

## Risks / Trade-offs

**[TLS handshake blocks connection actor startup]** → The handshake runs in the actor's pre-start / initialization phase using `RunTask` (async). The actor is not processing messages during handshake, which is correct — no data should flow before authentication completes.

**[Certificate hot-reload not supported]** → Certificates are loaded once at transport startup. To rotate certificates, restart the transport. This matches DotNetty's current behavior. Hot-reload could be added later via `IStreamProvider` factory that reloads certs.

**[Self-signed certificate testing]** → Provide test utilities for generating self-signed certs (like TurboMQTT's `CreateSelfSignedCertificate` pattern) so TLS tests don't require external certificate infrastructure.
