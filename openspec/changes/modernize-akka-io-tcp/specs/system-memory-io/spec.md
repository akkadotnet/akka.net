## ADDED Requirements

### Requirement: Tcp.Write uses ReadOnlyMemory payload
The `Tcp.Write` command SHALL use `ReadOnlyMemory<byte>` for its `Data` property instead of `ByteString`.

#### Scenario: Send data over TCP connection
- **WHEN** an actor sends `Tcp.Write.Create(new ReadOnlyMemory<byte>(bytes))` to a TCP connection actor
- **THEN** the connection actor SHALL write the bytes to the underlying socket stream

#### Scenario: CompoundWrite with ReadOnlyMemory segments
- **WHEN** an actor sends a `Tcp.CompoundWrite` composed of multiple `Tcp.Write` commands
- **THEN** the connection actor SHALL write all segments to the socket, preserving order

### Requirement: Tcp.Received uses ReadOnlyMemory payload
The `Tcp.Received` event SHALL use `ReadOnlyMemory<byte>` for its `Data` property instead of `ByteString`.

#### Scenario: Receive data from TCP connection
- **WHEN** bytes arrive on a connected TCP socket
- **THEN** the connection actor SHALL emit `Tcp.Received` with a `ReadOnlyMemory<byte>` containing the received bytes

#### Scenario: Received data has independent lifetime
- **WHEN** an actor receives `Tcp.Received(data)` and stores `data` in its state
- **THEN** the data SHALL remain valid indefinitely (not tied to socket buffer lifecycle)

### Requirement: ByteString class removed
The `Akka.Util.ByteString` class SHALL be deleted from the codebase. All references SHALL be replaced with `ReadOnlyMemory<byte>`, `Memory<byte>`, or `byte[]` as appropriate.

#### Scenario: Akka.Streams elements use ReadOnlyMemory
- **WHEN** an Akka.Streams TCP flow emits or accepts data elements
- **THEN** the element type SHALL be `ReadOnlyMemory<byte>` instead of `ByteString`

#### Scenario: Akka.Remote payload handling uses ReadOnlyMemory
- **WHEN** `MessageSerializer` or `AkkaPduCodec` processes wire payloads
- **THEN** they SHALL work with `ReadOnlyMemory<byte>` or `ReadOnlySequence<byte>` instead of `ByteString`

### Requirement: Target frameworks updated
All Akka.NET library projects SHALL target `net10.0` only. The `netstandard2.0` and `net6.0` targets SHALL be removed. No version of .NET Framework supports netstandard2.1+, so .NET Framework compatibility is lost regardless of whether the target is netstandard2.1 or net10.0. Targeting net10.0 directly eliminates conditional compilation and polyfill complexity.

#### Scenario: Build with net10.0 APIs
- **WHEN** the solution is built
- **THEN** all projects SHALL compile against `net10.0` without polyfill packages for `Span<T>`, `Memory<T>`, or `IBufferWriter<T>`

#### Scenario: Socket APIs available without conditionals
- **WHEN** Akka.IO TCP code calls `Stream.ReadAsync(Memory<byte>)` or `Stream.WriteAsync(ReadOnlyMemory<byte>)`
- **THEN** these APIs SHALL be available without conditional compilation (single TFM)

### Requirement: Akka.IO TCP messaging protocol preserved
All Akka.IO TCP message types other than `Tcp.Write` and `Tcp.Received` payload types SHALL remain unchanged. The actor interaction patterns SHALL remain identical.

#### Scenario: Existing message types unchanged
- **WHEN** user code sends `Tcp.Bind`, `Tcp.Connect`, `Tcp.Register`, `Tcp.Close`, `Tcp.Abort`, `Tcp.ConfirmedClose`, `Tcp.SuspendReading`, `Tcp.ResumeReading`, or `Tcp.Unbind`
- **THEN** the message types, their fields, and the expected actor responses SHALL be identical to pre-migration behavior

#### Scenario: Actor hierarchy unchanged
- **WHEN** a `Tcp.Bind` or `Tcp.Connect` command is processed
- **THEN** the actor hierarchy (`TcpManager` → `TcpListener` → `TcpIncomingConnection`, `TcpManager` → `TcpOutgoingConnection`) SHALL be identical to pre-migration behavior
