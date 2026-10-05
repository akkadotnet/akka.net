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

### Requirement: Certificate policy and mutual authentication

The system SHALL create fresh TLS authentication options for each connection and apply the configured certificate, chain, hostname, mutual-authentication and custom-validator settings.

#### Scenario: Client validates server identity
- **WHEN** client hostname validation is enabled and `TargetHost` is configured
- **THEN** the server certificate SHALL be checked against that target host

#### Scenario: Server-only TLS
- **WHEN** mutual authentication is disabled
- **THEN** the TLS connection SHALL not require a client certificate

#### Scenario: Mutual TLS
- **WHEN** mutual authentication is enabled
- **THEN** the client SHALL present its configured certificate and the server SHALL require and validate the client's certificate

#### Scenario: Missing mutual TLS certificate is rejected before custom validation
- **WHEN** the server requires a client certificate but the client presents none
- **THEN** the handshake SHALL be rejected even if the configured custom validator would accept a presented certificate, and that validator SHALL NOT be called

#### Scenario: Chain suppression does not suppress hostname errors
- **WHEN** chain validation is suppressed and hostname validation is enabled
- **THEN** chain errors SHALL be ignored while a hostname mismatch SHALL still reject the peer

#### Scenario: Hostname suppression is independent of chain validation
- **WHEN** hostname validation is disabled and chain validation is not suppressed
- **THEN** a hostname mismatch alone SHALL not reject a peer whose certificate chain is otherwise trusted

#### Scenario: Custom validator
- **WHEN** a custom certificate validator is configured
- **THEN** it SHALL decide whether a presented peer certificate is accepted and receive the certificate, chain, peer context, policy errors and connection logger

### Requirement: TLS half-close

The system SHALL flush pending writes and send TLS `close_notify` before sending TCP FIN when a connection's output side is gracefully shut down, while retaining the input side until the existing close behavior finishes.

#### Scenario: Confirmed close keeps reads available
- **WHEN** a registered TLS connection sends `Tcp.ConfirmedClose`
- **THEN** it SHALL flush and half-close its TLS write side, continue receiving peer application data, and finish when the existing confirmed-close conditions are met

#### Scenario: Full close after TLS half-close
- **WHEN** the connection finishes after a previous TLS half-close
- **THEN** the stream and socket SHALL be disposed without sending duplicate TLS shutdown notifications
