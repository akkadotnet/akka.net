## ADDED Requirements

### Requirement: IStreamProvider abstraction
Akka.IO TCP SHALL use an `IStreamProvider` interface for creating connected `Stream` instances. The interface SHALL define `ConnectAsync(string host, int port, CancellationToken ct) → Task<Stream>` and `Close()`.

#### Scenario: TcpStreamProvider creates plaintext connection
- **WHEN** `TcpStreamProvider.ConnectAsync(host, port, ct)` is called
- **THEN** it SHALL return a `NetworkStream` wrapping a connected `Socket`

#### Scenario: IStreamProvider injected into outgoing connections
- **WHEN** `TcpOutgoingConnection` is created for a `Tcp.Connect` command
- **THEN** it SHALL receive an `IStreamProvider` and use it to obtain the connected `Stream`

#### Scenario: Accepted connections receive Stream directly
- **WHEN** `TcpListener` accepts an incoming connection via `Socket.AcceptAsync()`
- **THEN** it SHALL wrap the accepted socket in `new NetworkStream(socket, ownsSocket: true)` and pass the `Stream` to `TcpIncomingConnection`

### Requirement: Pipe-based read loop
The TCP connection actor SHALL use a `System.IO.Pipelines.Pipe` for buffering data read from the socket stream. A background task SHALL read from the `Stream` into the `Pipe.Writer`, and a second task SHALL read from the `Pipe.Reader` and emit `Tcp.Received` messages.

#### Scenario: Data flows from stream through pipe to actor
- **WHEN** bytes arrive on the socket
- **THEN** the read-from-stream task SHALL call `stream.ReadAsync(pipe.Writer.GetMemory())`, advance the writer, and flush
- **THEN** the read-from-pipe task SHALL read from `pipe.Reader`, copy to a pooled buffer via `MemoryPool<byte>.Shared.Rent()`, and deliver `Tcp.Received` to the registered handler actor

#### Scenario: Pipe backpressure throttles reads
- **WHEN** the `Pipe.Writer` buffered data exceeds `pauseWriterThreshold`
- **THEN** `pipe.Writer.FlushAsync()` SHALL pause until the reader consumes enough data to drop below `resumeWriterThreshold`
- **THEN** `stream.ReadAsync()` SHALL be suspended during the pause (natural TCP backpressure)

#### Scenario: Graceful stream EOF
- **WHEN** `stream.ReadAsync()` returns 0 bytes (peer closed send side)
- **THEN** the read task SHALL complete the `Pipe.Writer` and the actor SHALL emit `Tcp.PeerClosed` to the handler

#### Scenario: Stream read error
- **WHEN** `stream.ReadAsync()` throws an `IOException` or `SocketException`
- **THEN** the actor SHALL emit `Tcp.ErrorClosed` to the handler with the exception message

### Requirement: Stream-based write loop
A background write task SHALL consume write commands and call `stream.WriteAsync(ReadOnlyMemory<byte>)` to send data.

#### Scenario: Single write command
- **WHEN** a `Tcp.Write(data, ack)` is received by the connection actor
- **THEN** the write task SHALL call `stream.WriteAsync(data)` and deliver the ACK event to the sender after completion

#### Scenario: Write batching
- **WHEN** multiple `Tcp.Write` commands are pending
- **THEN** the write task SHALL process them in order, with the option to coalesce into larger writes for efficiency

#### Scenario: Write failure
- **WHEN** `stream.WriteAsync()` throws an exception
- **THEN** the actor SHALL emit `Tcp.ErrorClosed` and initiate connection teardown

### Requirement: SuspendReading and ResumeReading flow control
The pull-mode and suspend/resume reading commands SHALL control whether the read-from-pipe task delivers `Tcp.Received` messages to the handler.

#### Scenario: SuspendReading stops delivery
- **WHEN** the handler sends `Tcp.SuspendReading`
- **THEN** the read-from-pipe task SHALL stop emitting `Tcp.Received` messages (data continues buffering in the Pipe up to backpressure threshold)

#### Scenario: ResumeReading resumes delivery
- **WHEN** the handler sends `Tcp.ResumeReading` after a `SuspendReading`
- **THEN** the read-from-pipe task SHALL resume emitting buffered and new `Tcp.Received` messages

#### Scenario: Pull mode requires explicit ResumeReading
- **WHEN** a connection is registered with `pullMode: true`
- **THEN** each `Tcp.Received` delivery SHALL require a preceding `ResumeReading` from the handler

### Requirement: Background task lifecycle coordination
All background I/O tasks SHALL be tracked and coordinated through the actor mailbox using self-tell patterns.

#### Scenario: Clean shutdown on Close
- **WHEN** `Tcp.Close` is received
- **THEN** the actor SHALL complete the write channel, wait for pending writes to flush, cancel the read CTS, wait for all background tasks via `Task.WhenAll`, complete the Pipe, close the Stream and StreamProvider, and emit `Tcp.Closed`

#### Scenario: Abort terminates immediately
- **WHEN** `Tcp.Abort` is received
- **THEN** the actor SHALL cancel the CTS immediately (no flush), wait for background tasks to complete, close the Stream, and emit `Tcp.Aborted`

#### Scenario: CTS cancellation is idempotent
- **WHEN** multiple code paths attempt to cancel the `CancellationTokenSource`
- **THEN** only the first cancellation SHALL proceed (guarded by `Interlocked.CompareExchange`)

#### Scenario: Self-tell before caller-tell ordering
- **WHEN** a background task completes and needs to notify both the actor and a caller
- **THEN** the actor self-tell SHALL be sent before the caller reply to prevent mailbox ordering races

### Requirement: Read buffer uses MemoryPool with copy semantics
Data read from the Pipe SHALL be copied into a `MemoryPool<byte>.Shared.Rent()` buffer before being delivered as `Tcp.Received`. The pooled buffer's ownership transfers to the message recipient.

#### Scenario: Read data is independent of pipe buffer
- **WHEN** the read-from-pipe task reads a segment from `pipe.Reader`
- **THEN** it SHALL rent a buffer from `MemoryPool<byte>.Shared`, copy the pipe segment into it, advance the pipe reader past the consumed data, and wrap the result as `ReadOnlyMemory<byte>` for `Tcp.Received`

#### Scenario: Pipe buffer is immediately reusable
- **WHEN** `pipe.Reader.AdvanceTo(buffer.End)` is called after copying
- **THEN** the pipe's internal buffer space SHALL be available for the next `stream.ReadAsync()` call
