## ADDED Requirements

### Requirement: Per-command Akka.IO TLS

The system SHALL allow each `Tcp.Connect` and `Tcp.Bind` command to opt in to TLS through an additive `Tls` init property while preserving existing constructors and plaintext behavior when the property is absent.

#### Scenario: TLS and plaintext coexist
- **WHEN** an actor system uses TLS settings for one TCP command and omits them from another
- **THEN** the configured connection SHALL use TLS and the other connection SHALL retain plaintext TCP behavior

### Requirement: Authentication precedes Connected

The system SHALL complete the TLS handshake before delivering `Tcp.Connected` for that connection.

#### Scenario: Successful outgoing handshake
- **WHEN** an outgoing TLS peer presents a certificate accepted by the configured policy
- **THEN** the connection actor SHALL deliver `Tcp.Connected` and await `Tcp.Register` before starting transport pumps

#### Scenario: Rejected outgoing peer
- **WHEN** outgoing TLS authentication fails
- **THEN** the commander SHALL receive `Tcp.CommandFailed` for the original `Tcp.Connect` with the handshake failure as its cause, and no `Tcp.Connected` SHALL be delivered

#### Scenario: Rejected inbound peer
- **WHEN** an accepted socket fails TLS authentication
- **THEN** its connection actor SHALL log the failure and stop without sending `Tcp.Connected`, while the listener continues accepting other sockets

### Requirement: Bounded and cancellable handshake

The system SHALL enforce the configured finite positive handshake timeout and cancel pending authentication when its connection actor stops.

#### Scenario: Handshake timeout
- **WHEN** a TLS peer does not complete authentication before `HandshakeTimeout`
- **THEN** the connection SHALL fail without reporting `Tcp.Connected` and its socket and stream SHALL be released

#### Scenario: Connection actor stops during handshake
- **WHEN** the connection actor stops while authentication is pending
- **THEN** it SHALL cancel the handshake and dispose its socket and stream, and a late task completion SHALL NOT report `Tcp.Connected`

### Requirement: Role-specific settings and peer trust policy

The system SHALL expose immutable role-specific TLS settings and create fresh native TLS authentication options internally for each connection. Client and listener settings SHALL be constructed using `ServerOnly` or `Mutual` factories with the certificate and peer policy required for that role. Peer trust SHALL use `TlsPeerPolicy.SystemTrust`, `PinnedCertificates`, or `CustomTrust` as its base decision, with `And` callbacks only narrowing acceptance.

#### Scenario: Server-only TLS
- **WHEN** a client uses `TlsClientSettings.ServerOnly` and a listener uses `TlsServerSettings.ServerOnly`
- **THEN** the client SHALL validate the server according to its policy and the listener SHALL permit clients without certificates

#### Scenario: Mutual TLS
- **WHEN** a client uses `TlsClientSettings.Mutual` and a listener uses `TlsServerSettings.Mutual`
- **THEN** the client SHALL present its configured certificate and the listener SHALL require and validate the client's certificate

#### Scenario: System trust ignores only hostname mismatch
- **WHEN** a peer is evaluated using `TlsPeerPolicy.SystemTrust`
- **THEN** OS certificate-chain errors SHALL reject the peer, while a name mismatch alone SHALL be left to an explicit hostname check

#### Scenario: Outgoing target host defaults from endpoint
- **WHEN** `WithTargetHost` is not set on an outgoing client
- **THEN** the remote connection endpoint SHALL supply the target host used for SNI and runtime hostname context

#### Scenario: Outgoing target host override
- **WHEN** an outgoing client sets `WithTargetHost`
- **THEN** the configured name SHALL override the endpoint-derived target for SNI and certificate name validation

#### Scenario: Hostname validation
- **WHEN** a policy includes `TlsCertificateValidation.ValidateHostname()`
- **THEN** the runtime name-mismatch result SHALL be checked; an explicit expected-host overload SHALL match that DNS or IP name

#### Scenario: Missing mutual TLS certificate is rejected before custom validation
- **WHEN** the server requires a client certificate but the client presents none
- **THEN** the handshake SHALL be rejected even if the configured custom validator would accept a presented certificate, and that validator SHALL NOT be called

#### Scenario: Custom validator
- **WHEN** a peer policy uses `CustomTrust(callback)`
- **THEN** the callback SHALL make the complete trust decision and receive the certificate, chain, remote peer, original policy errors, and connection logger

#### Scenario: Narrowing callbacks
- **WHEN** a peer policy has one or more checks added with `And`
- **THEN** the checks SHALL run only after base trust accepts and any false result SHALL reject the peer

#### Scenario: Pin trust
- **WHEN** a leaf thumbprint matches one configured with `PinnedCertificates`
- **THEN** the pin SHALL establish trust without implying CA-chain, expiry, revocation, or hostname validation

### Requirement: TLS half-close

The system SHALL flush pending writes and send TLS `close_notify` before sending TCP FIN when a connection's output side is gracefully shut down, while retaining the input side until the existing close behavior finishes.

#### Scenario: Confirmed close keeps reads available
- **WHEN** a registered TLS connection sends `Tcp.ConfirmedClose`
- **THEN** it SHALL flush and half-close its TLS write side, continue receiving peer application data, and finish when the existing confirmed-close conditions are met

#### Scenario: Full close after TLS half-close
- **WHEN** the connection finishes after a previous TLS half-close
- **THEN** the stream and socket SHALL be disposed without sending duplicate TLS shutdown notifications
