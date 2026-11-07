## 1. Akka.IO TCP TLS

- [x] 1.1 Add role-specific client/server TLS settings, peer trust policy and fresh internal authentication-option factories.
- [x] 1.2 Add optional TLS settings to `Tcp.Connect` and `Tcp.Bind` without changing existing constructors.
- [x] 1.3 Authenticate outgoing and accepted connections asynchronously before `Tcp.Connected`; bound handshakes and cancel/dispose pending resources on actor stop.
- [x] 1.4 Reuse the registered stream transport and send TLS `close_notify` before TCP FIN during graceful half-close.
- [ ] 1.5 Verify successful and rejected handshakes, timeout/cancellation, listener availability, ownership, TLS 1.2/1.3 half-close and unchanged plaintext behavior.
- [ ] 1.6 Complete warnings-as-errors build, API compatibility approval and focused Slopwatch review.

## 2. TLS certificate-source and validation parity

- [ ] 2.1 Add PKCS#12 file loading with password/key-storage settings and certificate-store lookup by store name, location and thumbprint.
- [ ] 2.2 Add reusable chain, hostname, pin, subject, issuer, combine and chain-plus-custom validation helpers with documented composition order.
- [ ] 2.3 Verify certificate ownership, RSA/ECDSA private-key access, all helper allow/deny cases and real certificate-source handshakes.

## 3. Akka.Streams TCP TLS

- [ ] 3.1 Add explicit Streams bind/connect entry points and propagate settings into Akka.IO commands.
- [ ] 3.2 Preserve materialized values, backpressure, cancellation and existing plaintext behavior.
- [ ] 3.3 Verify TLS byte exchange, failures, half-close and accepted read-only connections.

## 4. Artery TCP TLS

- [ ] 4.1 Add Artery TLS HOCON and programmatic setup with documented precedence.
- [ ] 4.2 Route control, ordinary and large-message channels through Streams TLS while retaining association and lane behavior.
- [ ] 4.3 Verify actor communication, mutual TLS rejection, reconnect, quarantine and coordinated shutdown across two systems.
