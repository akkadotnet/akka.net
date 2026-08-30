## ADDED Requirements

### Requirement: Client-side TLS via TlsStreamProvider
The system SHALL provide a `TlsStreamProvider : IStreamProvider` that wraps a plaintext `TcpStreamProvider` connection in `SslStream` and completes TLS authentication before returning the `Stream`.

#### Scenario: Successful client TLS handshake
- **WHEN** `TlsStreamProvider.ConnectAsync(host, port, ct)` is called with valid TLS settings
- **THEN** it SHALL connect via `TcpStreamProvider`, wrap the `NetworkStream` in `SslStream`, call `AuthenticateAsClientAsync` with the configured `SslClientAuthenticationOptions`, and return the authenticated `SslStream`

#### Scenario: Client TLS handshake failure
- **WHEN** `AuthenticateAsClientAsync` throws an `AuthenticationException` (e.g., certificate validation failure)
- **THEN** `ConnectAsync` SHALL dispose the `SslStream` and `NetworkStream`, and propagate the exception to the caller

#### Scenario: Client TLS handshake cancellation
- **WHEN** the `CancellationToken` is cancelled during `AuthenticateAsClientAsync`
- **THEN** `ConnectAsync` SHALL dispose resources and throw `OperationCanceledException`

### Requirement: Server-side TLS handshake in TcpIncomingConnection
When TLS is enabled, `TcpIncomingConnection` SHALL wrap the accepted socket's `NetworkStream` in `SslStream` and call `AuthenticateAsServerAsync` before entering the `Connected` state.

#### Scenario: Successful server TLS handshake
- **WHEN** a client connects and completes the TLS handshake within the configured timeout
- **THEN** `TcpIncomingConnection` SHALL transition to `Connected` state with the authenticated `SslStream` as its I/O stream

#### Scenario: Server TLS handshake timeout
- **WHEN** the TLS handshake does not complete within the configured timeout (default: 10 seconds)
- **THEN** `TcpIncomingConnection` SHALL dispose the `SslStream`, close the socket, and stop itself

#### Scenario: Server TLS handshake failure
- **WHEN** `AuthenticateAsServerAsync` throws an `AuthenticationException`
- **THEN** `TcpIncomingConnection` SHALL log the error, dispose resources, and stop itself without affecting the `TcpListener`

#### Scenario: Listener accept loop unaffected by slow handshake
- **WHEN** one client's TLS handshake is slow or stalled
- **THEN** `TcpListener` SHALL continue accepting other connections without blocking

### Requirement: Mutual TLS support
The system SHALL support mutual TLS (mTLS) where both client and server present certificates.

#### Scenario: Server requires client certificate
- **WHEN** `ssl.require-mutual-authentication = true` is configured
- **THEN** the server SHALL set `SslServerAuthenticationOptions.ClientCertificateRequired = true` and reject connections where the client does not present a valid certificate

#### Scenario: Client presents certificate
- **WHEN** `TlsStreamProvider` is configured with client certificates
- **THEN** `SslClientAuthenticationOptions.ClientCertificates` SHALL include the configured certificates

### Requirement: TLS configuration from existing HOCON
The system SHALL parse all existing `akka.remote.dot-netty.tcp.ssl.*` HOCON keys into `TlsSettings` without requiring any user configuration changes.

#### Scenario: File-based certificate configuration
- **WHEN** `ssl.certificate.path` and `ssl.certificate.password` are configured in HOCON
- **THEN** the system SHALL load the certificate from the file path with the specified password

#### Scenario: Thumbprint-based certificate configuration
- **WHEN** `ssl.certificate.use-thumbprint-over-file = true` with `ssl.certificate.thumbprint`, `ssl.certificate.store-name`, and `ssl.certificate.store-location` configured
- **THEN** the system SHALL look up the certificate from the Windows certificate store by thumbprint

#### Scenario: Enable SSL flag selects provider
- **WHEN** `enable-ssl = true` is set in the transport HOCON configuration
- **THEN** the transport SHALL use `TlsStreamProvider` for outgoing connections and enable server-side TLS for incoming connections

#### Scenario: Disable SSL uses plaintext
- **WHEN** `enable-ssl = false` (default)
- **THEN** the transport SHALL use `TcpStreamProvider` with no TLS

### Requirement: Certificate validation options
The system SHALL support configurable certificate validation matching the current DotNetty TLS behavior.

#### Scenario: Suppress validation for development
- **WHEN** `ssl.suppress-validation = true` is configured
- **THEN** the `RemoteCertificateValidationCallback` SHALL accept all certificates without chain or hostname validation

#### Scenario: Hostname validation enabled
- **WHEN** `ssl.validate-certificate-hostname = true` is configured
- **THEN** the validation callback SHALL verify the server certificate's Subject Alternative Name (SAN) or Common Name (CN) matches the target host

#### Scenario: Chain validation by default
- **WHEN** `ssl.suppress-validation = false` (default)
- **THEN** the validation callback SHALL verify the certificate chain is trusted by the system's certificate store

### Requirement: Programmatic TLS setup
The system SHALL provide a `TlsSetup` class that can be passed via `ActorSystemSetup` for programmatic TLS configuration, equivalent to the current `DotNettySslSetup`.

#### Scenario: Programmatic certificate configuration
- **WHEN** a `TlsSetup` instance with an `X509Certificate2` is provided via `ActorSystemSetup`
- **THEN** the transport SHALL use the provided certificate instead of HOCON-configured certificates

#### Scenario: Programmatic setup overrides HOCON
- **WHEN** both HOCON `ssl.*` keys and a `TlsSetup` are provided
- **THEN** the `TlsSetup` SHALL take precedence over HOCON configuration
