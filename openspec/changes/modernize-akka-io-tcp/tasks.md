## 1. Target Framework Migration

- [x] 1.1 Update `Directory.Build.props`: replace `NetStandardLibVersion` value with `net10.0`, remove `NetLibVersion` (or set both to `net10.0`). All library projects will target `net10.0` only.
- [x] 1.2 Remove `Polyfill` package references from all csproj files (no longer needed — net10.0 has all APIs natively)
- [x] 1.3 Remove all netstandard-conditional `<ItemGroup>` blocks from csproj files (no more multi-TFM conditionals)
- [x] 1.4 Update any csproj files that reference `$(NetStandardLibVersion)` or `$(NetLibVersion)` to use a single `net10.0` target
- [x] 1.5 Verify solution builds with `dotnet build` — fix any immediate breaks from TFM change alone

## 2. ByteString Deletion and Akka.IO Message Type Changes

- [x] 2.1 Change `Tcp.Write.Data` from `ByteString` to `ReadOnlyMemory<byte>` in `src/core/Akka/IO/Tcp.cs`
- [x] 2.2 Change `Tcp.Received.Data` from `ByteString` to `ReadOnlyMemory<byte>` in `src/core/Akka/IO/Tcp.cs`
- [x] 2.3 Update `Tcp.CompoundWrite` and `Tcp.SimpleWriteCommand` to use `ReadOnlyMemory<byte>`
- [x] 2.4 Update all `Write.Create()` factory method overloads
- [x] 2.5 Delete `src/core/Akka/Util/ByteString.cs`
- [x] 2.6 Fix all compilation errors in `src/core/Akka/IO/` resulting from ByteString removal
- [x] 2.7 Fix all compilation errors in `src/core/Akka/` (non-IO) resulting from ByteString removal

## 3. IStreamProvider and TcpStreamProvider

- [x] 3.1 Create `IStreamProvider` interface in `src/core/Akka/IO/` with `ConnectAsync` and `Close` methods
- [x] 3.2 Create `TcpStreamProvider` implementation returning `NetworkStream` from connected `Socket`
- [x] 3.3 Update `TcpOutgoingConnection` to accept `IStreamProvider` and use it for connection establishment
- [x] 3.4 Update `TcpListener` to wrap accepted sockets in `NetworkStream` and pass `Stream` to `TcpIncomingConnection`
- [x] 3.5 Update `TcpIncomingConnection` to accept a `Stream` parameter

## 4. TcpConnection Internal Rewrite (Stream + Pipe)

- [x] 4.1 Add `System.IO.Pipelines` package reference to `Akka.csproj` — not needed, `System.IO.Pipelines` is included in the net10.0 shared framework
- [x] 4.2 Replace SAEA receive infrastructure with Pipe-based read loop in `TcpConnection.cs`: background task reads from `Stream` into `Pipe.Writer`
- [x] 4.3 Implement read-from-pipe task: reads from `Pipe.Reader`, copies to `MemoryPool<byte>.Shared.Rent()` buffer, emits `Tcp.Received` via actor Tell
- [x] 4.4 Replace SAEA send infrastructure with Stream-based write loop: dequeue pending writes, call `stream.WriteAsync(ReadOnlyMemory<byte>)`, deliver ACKs
- [x] 4.5 Implement `SuspendReading` / `ResumeReading` flow control on the read-from-pipe task
- [x] 4.6 Implement pull-mode support (each `Tcp.Received` requires preceding `ResumeReading`)
- [x] 4.7 Implement background task lifecycle coordination: `Task.WhenAll` tracking, self-tell on completion, `Interlocked.CompareExchange` CTS guard
- [x] 4.8 Implement `Tcp.Close` shutdown sequence: complete write channel → flush → cancel read CTS → await tasks → close Stream → emit `Tcp.Closed`
- [x] 4.9 Implement `Tcp.Abort` shutdown: immediate CTS cancel → await tasks → close Stream → emit `Tcp.Aborted`
- [x] 4.10 Implement `Tcp.ConfirmedClose` half-close: send FIN, await peer FIN, emit `Tcp.ConfirmedClosed`
- [x] 4.11 Implement graceful EOF handling: `stream.ReadAsync()` returns 0 → complete PipeWriter → emit `Tcp.PeerClosed`
- [x] 4.12 Implement error handling: I/O exceptions → emit `Tcp.ErrorClosed` with cause message
- [x] 4.13 Remove all `SocketAsyncEventArgs` usage from TcpConnection — `Buffers/` directory and `SocketEventArgsPool.cs` retained (still used by UDP)

## 5. Akka.Streams ByteString Migration

- [x] 5.1 Replace all `ByteString` references in `src/core/Akka.Streams/Dsl/` with `ReadOnlyMemory<byte>`
- [x] 5.2 Update `TcpStages.cs` (`IncomingConnectionStage`, `OutgoingConnectionStage`, `ConnectionSourceStage`) to use `ReadOnlyMemory<byte>` elements
- [x] 5.3 Update `StreamConverters`, `IOSources`, `IOSinks` and any other Streams I/O stages for ByteString removal
- [x] 5.4 Update Akka.Streams Framing stages (length-field framing, delimiter framing) for `ReadOnlyMemory<byte>`
- [x] 5.5 Fix all remaining compilation errors in `src/core/Akka.Streams/`

## 6. Akka.Remote and Cluster ByteString Migration

- [x] 6.1 Update `MessageSerializer.cs` to work with `ReadOnlyMemory<byte>` instead of `ByteString`
- [x] 6.2 Update `AkkaPduCodec.cs` for `ReadOnlyMemory<byte>`
- [x] 6.3 Fix all compilation errors in `src/core/Akka.Remote/`
- [x] 6.4 Fix all compilation errors in `src/core/Akka.Cluster/`
- [x] 6.5 Fix all compilation errors in `src/contrib/` (Cluster.Sharding, Cluster.Tools, DistributedData, etc.)

## 7. Test Suite Migration

- [ ] 7.1 Update all Akka.IO TCP tests for `ReadOnlyMemory<byte>` message types
- [ ] 7.2 Update all Akka.Streams TCP tests for `ReadOnlyMemory<byte>` elements
- [ ] 7.3 Update Akka.Remote transport tests
- [ ] 7.4 Verify all Akka.Remote specs that don't directly reference DotNetty APIs pass
- [ ] 7.5 Run full test suite: `dotnet test -c Release`

## 8. Validation

- [ ] 8.1 Verify `dotnet build -warnaserror` passes across all target frameworks
- [ ] 8.2 Verify Akka.IO TCP pull-mode works correctly (used by Akka.Streams TCP bridging)
- [ ] 8.3 Verify Akka.Streams `Tcp.Bind()` and `Tcp.OutgoingConnection()` work end-to-end
- [ ] 8.4 Verify Akka.Remote remoting works (messages sent between two ActorSystems)
- [ ] 8.5 Run `dotnet test -c Release src/core/Akka.API.Tests` — update API approval baselines for breaking changes
