## Why

Akka.IO TCP needs an opt-in TLS path that authenticates a peer before reporting a usable connection. This provides the foundation for encrypted Akka.Streams TCP and Artery TCP in later stages while keeping DotNetty's current TLS APIs independent.

## What Changes

- Add caller-owned `TlsClientSettings` and `TlsServerSettings` to Akka.IO.
- Construct role-specific settings through `ServerOnly` and `Mutual` factories, with immutable timeout, protocol, and outgoing target-host copy methods.
- Add `TlsPeerPolicy` for system trust, certificate pinning, custom trust, and narrowing checks; keep native TLS authentication options internal to the transport.
- Add optional `Tls` init properties to `Tcp.Connect` and `Tcp.Bind` without changing their constructors.
- Complete client and server handshakes asynchronously before sending `Tcp.Connected`; report outbound failures as `Tcp.CommandFailed` and stop failed inbound connection actors without blocking their listener.
- Reuse the existing stream-based transport connection for authenticated `SslStream` instances, including TLS `close_notify` during orderly half-close.

### What does NOT change

- Akka.IO commands without TLS settings retain their plaintext behavior.
- DotNetty TLS implementation and configuration remain independent.
- This stage does not add Akka.Streams APIs, Artery HOCON/setup wiring, QUIC, certificate loading helpers, hot reload, or plaintext fallback.
- This stage adds no TLS HOCON schema, configuration binder, or public native authentication-options API; applications provide their own `X509Certificate2` values.

## Capabilities

### New Capabilities

- `tcp-tls`: Per-connection Akka.IO TLS settings and authenticated client/server TCP lifecycles.

### Modified Capabilities

## Impact

- **Akka.IO** (`src/core/Akka/IO/`): TLS settings, additive command properties, pre-`Connected` handshakes and authenticated stream ownership.
- **Documentation and tests**: document and verify the Akka.IO TLS command path, handshake failures/timeouts, cancellation and half-close behavior.
- **Later stages**: Akka.Streams and Artery will consume these settings through their own explicit APIs. They will not depend on an `IStreamProvider` or DotNetty TLS types.
