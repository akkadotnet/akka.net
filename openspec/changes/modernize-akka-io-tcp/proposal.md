## Why

Akka.NET's TCP I/O layer uses two legacy primitives that block the 1.6 transport and serialization goals: `ByteString` (incompatible with `System.Memory`) and `SocketAsyncEventArgs` (incompatible with `SslStream` for TLS). DotNetty's `ByteBuf` is also incompatible with `System.Memory`, making it a dead end for the future high-throughput path. Modernizing the Akka.IO TCP internals to use `System.Memory` types and `Stream` + `System.IO.Pipelines` is the prerequisite for TLS support, future Artery TCP work, and integrating SerializerV2's `IBufferWriter<byte>` / `ReadOnlySequence<byte>` contract. The TurboMQTT project has already proven this pattern works for both TCP and TLS via the `IStreamProvider` abstraction.

## What Changes

- **BREAKING**: `Tcp.Write.Data` changes from `ByteString` to `ReadOnlyMemory<byte>`
- **BREAKING**: `Tcp.Received.Data` changes from `ByteString` to `ReadOnlyMemory<byte>`
- **BREAKING**: `ByteString` class hard-deleted from `Akka.Util` and all Akka.Streams usage replaced with `ReadOnlyMemory<byte>` / `Memory<byte>`
- **BREAKING**: Drop `netstandard2.0` and `net6.0` targets. All library projects target `net10.0` only. Required for `Stream.ReadAsync(Memory<byte>)` and `Socket.SendAsync(Memory<byte>)`. Drops .NET Framework 4.8 and older .NET support. No version of .NET Framework supports netstandard2.1+, so .NET Framework compat is lost regardless.
- Replace `SocketAsyncEventArgs`-based I/O in `TcpConnection.cs` with `Stream` + `System.IO.Pipelines` (`Pipe`)
- Add `IStreamProvider` abstraction to `TcpOutgoingConnection` and `TcpIncomingConnection` (proven in TurboMQTT)
- `TcpStreamProvider` returns `NetworkStream` for plaintext connections
- `Pipe` handles read buffering with configurable backpressure thresholds
- Background tasks for read-from-stream, read-from-pipe, write-to-stream (TurboMQTT pattern)
- Copy on read retained (unbounded message lifetime in actor model) using `MemoryPool<byte>.Shared.Rent()`
- Write path uses pooled buffers with bounded lifecycle (disposed after send completes)

### What does NOT change

- The Akka.IO TCP actor messaging protocol: `Bind`, `Connect`, `Connected`, `Register`, `Close`, `Abort`, `PeerClosed`, `CommandFailed`, `SuspendReading`, `ResumeReading` — all unchanged
- The actor hierarchy: `TcpManager` → `TcpListener` → `TcpIncomingConnection`, `TcpManager` → `TcpOutgoingConnection`
- The Akka.Streams TCP public API (`Tcp.Bind()`, `Tcp.OutgoingConnection()`) — signatures update for `ReadOnlyMemory<byte>` but patterns unchanged

## Capabilities

### New Capabilities

- `system-memory-io`: Replace `ByteString` with `System.Memory` types (`ReadOnlyMemory<byte>`, `Memory<byte>`) across Akka.IO and Akka.Streams. Covers message payload types, stream element types, and all internal buffer management.
- `stream-pipe-transport`: Replace `SocketAsyncEventArgs`-based socket I/O in Akka.IO TCP actors with `Stream` + `System.IO.Pipelines` pattern. Covers the `IStreamProvider` abstraction, `Pipe`-based read/write loops, backpressure, and `MemoryPool` buffer lifecycle.

### Modified Capabilities

## Impact

- **Akka core** (`src/core/Akka/`): `ByteString` deletion, `Tcp.cs` message types, `TcpConnection.cs` full internal rewrite, `TcpOutgoingConnection.cs`, `TcpIncomingConnection.cs`, `TcpListener.cs`
- **Akka.Streams** (`src/core/Akka.Streams/`): All `ByteString` references in DSL, stages, and TCP integration (`TcpStages.cs`)
- **Akka.Remote** (`src/core/Akka.Remote/`): `MessageSerializer.cs`, `AkkaPduCodec.cs`, all transport code that touches `ByteString`
- **Akka.Cluster** and contrib: Any code referencing `ByteString` for wire payloads
- **Build system**: `Directory.Build.props` TFM changes (`NetStandardLibVersion`, `NetLibVersion`), possible removal of `Polyfill` package for netstandard2.0
- **NuGet dependencies**: Add `System.IO.Pipelines` for netstandard2.1 target
- **Test suites**: All tests referencing `ByteString` need updating. All Akka.IO TCP tests need to pass with new internals.
- **Downstream enablement**: TLS via `TlsStreamProvider`, the `SerializerV2` foundation, and the future Artery TCP remoting path
