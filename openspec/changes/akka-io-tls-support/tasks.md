## 1. TlsSettings Configuration

- [ ] 1.1 Create `TlsSettings` class in `src/core/Akka/IO/` that parses `akka.remote.dot-netty.tcp.ssl.*` HOCON into `SslClientAuthenticationOptions` and `SslServerAuthenticationOptions`
- [ ] 1.2 Implement file-based certificate loading (`ssl.certificate.path` + `ssl.certificate.password`)
- [ ] 1.3 Implement thumbprint-based certificate store lookup (`ssl.certificate.thumbprint` + `store-name` + `store-location`)
- [ ] 1.4 Implement validation callback composition: chain validation, hostname validation, suppress-validation
- [ ] 1.5 Implement `ssl.require-mutual-authentication` → `ClientCertificateRequired`
- [ ] 1.6 Implement handshake timeout setting (default: 10 seconds)

## 2. TlsStreamProvider (Client-Side)

- [ ] 2.1 Create `TlsStreamProvider : IStreamProvider` in `src/core/Akka/IO/`
- [ ] 2.2 Implement `ConnectAsync`: connect via `TcpStreamProvider` → wrap in `SslStream` → `AuthenticateAsClientAsync(SslClientAuthenticationOptions, ct)` → return authenticated stream
- [ ] 2.3 Implement `Close`: dispose `SslStream`, delegate to `TcpStreamProvider.Close()`
- [ ] 2.4 Handle handshake failure: dispose resources, propagate `AuthenticationException`
- [ ] 2.5 Handle cancellation: dispose resources, propagate `OperationCanceledException`

## 3. Server-Side TLS Handshake

- [ ] 3.1 Modify `TcpIncomingConnection` to accept optional `TlsSettings` parameter
- [ ] 3.2 When TLS enabled: wrap `NetworkStream` in `SslStream`, call `AuthenticateAsServerAsync` with timeout before entering `Connected` state
- [ ] 3.3 Handle handshake timeout: dispose `SslStream`, stop actor
- [ ] 3.4 Handle handshake failure: log error, dispose resources, stop actor without affecting `TcpListener`
- [ ] 3.5 Modify `TcpListener` to pass `TlsSettings` to `TcpIncomingConnection` when TLS is configured

## 4. Transport Integration

- [ ] 4.1 Modify transport configuration to check `enable-ssl` flag and select `TlsStreamProvider` vs `TcpStreamProvider`
- [ ] 4.2 Create `TlsSetup` class for programmatic TLS configuration via `ActorSystemSetup`
- [ ] 4.3 Ensure `TlsSetup` overrides HOCON when both are provided
- [ ] 4.4 Wire `TlsSettings` into `TcpManager` so it propagates to both outgoing and incoming connections

## 5. Testing

- [ ] 5.1 Create self-signed certificate generation utility for tests (following TurboMQTT `CreateSelfSignedCertificate` pattern)
- [ ] 5.2 Test: client TLS handshake succeeds with valid certificate
- [ ] 5.3 Test: client TLS handshake fails with invalid certificate
- [ ] 5.4 Test: server TLS handshake succeeds
- [ ] 5.5 Test: server TLS handshake timeout (client connects but never sends ClientHello)
- [ ] 5.6 Test: mutual TLS (both client and server present certificates)
- [ ] 5.7 Test: `suppress-validation = true` accepts self-signed certificates
- [ ] 5.8 Test: hostname validation rejects mismatched certificates
- [ ] 5.9 Test: end-to-end TLS remoting between two ActorSystems
- [ ] 5.10 Test: existing DotNetty TLS HOCON configuration parses correctly into `TlsSettings`
- [ ] 5.11 Test: `TlsSetup` programmatic configuration overrides HOCON
