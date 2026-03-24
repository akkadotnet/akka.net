## Why

Akka.Remote requires TLS support for production deployments. The current implementation relies on DotNetty's `TlsHandler`, which is being removed (Spec 3). With Spec 1 (`modernize-akka-io-tcp`) introducing `IStreamProvider` and replacing `SocketAsyncEventArgs` with `Stream` + `Pipe`, TLS becomes a simple `IStreamProvider` implementation — `TlsStreamProvider` wraps `SslStream` around `NetworkStream`, handshake happens inside `ConnectAsync`, and the TCP connection actor never knows it's encrypted. This was previously impossible because SAEA couldn't work with `SslStream`.

## What Changes

- Add `TlsStreamProvider : IStreamProvider` for client-side TLS (wraps `SslStream`, handshake inside `ConnectAsync`)
- Add server-side TLS handshake in `TcpIncomingConnection` startup path (wrap accepted socket in `SslStream`, call `AuthenticateAsServerAsync` before entering `Connected` state)
- Add `TlsSettings` configuration class that parses existing DotNetty TLS HOCON (`akka.remote.dot-netty.tcp.ssl.*`) into `SslClientAuthenticationOptions` / `SslServerAuthenticationOptions`
- `enable-ssl = true` in HOCON selects `TlsStreamProvider` over `TcpStreamProvider`
- All existing DotNetty TLS configuration keys continue to work without modification

### What does NOT change

- The Akka.IO TCP actor messaging protocol (no new message types for TLS)
- The actor hierarchy
- The Stream + Pipe I/O internals (TLS is transparent — `SslStream` is just a `Stream`)
- No BidiFlow or Akka.Streams-level TLS abstraction (YAGNI — TLS belongs at the socket abstraction level, not as a stream transformation)

## Capabilities

### New Capabilities

- `tls-stream-provider`: TLS support via `IStreamProvider` abstraction at the Akka.IO level. Covers client-side `TlsStreamProvider`, server-side TLS handshake in `TcpIncomingConnection`, configuration parsing from existing HOCON, and certificate management (file-based, thumbprint/store-based, programmatic).

### Modified Capabilities

## Impact

- **Akka.IO** (`src/core/Akka/IO/`): New `TlsStreamProvider.cs`, `TlsSettings.cs`. Minor changes to `TcpIncomingConnection.cs` for server-side handshake. `TcpManager` or transport config selects provider based on `enable-ssl`.
- **Akka.Remote** (`src/core/Akka.Remote/`): Transport configuration maps existing DotNetty TLS HOCON to new `TlsSettings`. `DotNettySslSetup` programmatic API needs equivalent.
- **Configuration**: All `akka.remote.dot-netty.tcp.ssl.*` keys continue to work. No user config changes required.
- **Dependencies**: `System.Net.Security` (built-in on netstandard2.1 / net6.0, no new NuGet packages)
- **Test suites**: TLS integration tests (client/server with self-signed certs), mutual TLS tests, certificate validation tests, handshake timeout/failure tests
