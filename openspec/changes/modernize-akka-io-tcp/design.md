## Context

Akka.NET's Akka.IO TCP layer uses `SocketAsyncEventArgs` (SAEA) for socket I/O and `ByteString` as the data type for `Tcp.Write` and `Tcp.Received` messages. Both are incompatible with .NET's modern `System.Memory` / `System.IO.Pipelines` ecosystem:

- `ByteString` is a custom immutable byte wrapper. It cannot participate in `IBufferWriter<byte>` / `ReadOnlySequence<byte>` pipelines without copying.
- `SocketAsyncEventArgs` is callback-based and cannot work with `SslStream`, which requires a `Stream` to wrap.
- The Akka.Streams TCP layer (`TcpStages.cs`) wraps the Akka.IO TCP actors, so it inherits these limitations.

The TurboMQTT project (petabridge/TurboMqtt) has proven a `Stream` + `System.IO.Pipelines` pattern that solves both problems, with an `IStreamProvider` abstraction that makes TLS a transparent swap.

Current target frameworks are `netstandard2.0` + `net6.0`. The `netstandard2.0` target lacks `Stream.ReadAsync(Memory<byte>)` and `Socket.SendAsync(Memory<byte>)`, which are required for zero-copy I/O.

## Goals / Non-Goals

**Goals:**
- Replace `ByteString` with `ReadOnlyMemory<byte>` on `Tcp.Write.Data` and `Tcp.Received.Data`
- Replace `SocketAsyncEventArgs` internals in `TcpConnection` with `Stream` + `Pipe`
- Introduce `IStreamProvider` abstraction enabling transparent TCP/TLS switching
- Remove `ByteString` from the entire codebase (Akka.IO, Akka.Streams, Akka.Remote)
- Drop `netstandard2.0` to unlock `System.Memory` socket APIs
- Preserve the Akka.IO TCP actor messaging protocol exactly
- Preserve the actor supervision hierarchy exactly

**Non-Goals:**
- TLS implementation (separate `TlsStreamProvider` work)
- Replacing or bypassing DotNetty remoting (future Artery TCP work depends on this direction)
- SerializerV2 integration (separate atomic foundation workstream)
- Performance benchmarking (depends on SerializerV2 and Artery TCP)
- Changing Akka.IO.Udp (separate effort, similar pattern could apply later)
- Source generator for serialization (deferred)
- Modifying the Akka.Streams materializer architecture

## Decisions

### 1. Stream + Pipe over SocketAsyncEventArgs

**Decision:** Replace SAEA-based socket I/O with `NetworkStream` + `System.IO.Pipelines.Pipe`.

**Rationale:** SAEA cannot work with `SslStream` (required for TLS). On modern .NET (net6+), `NetworkStream.ReadAsync(Memory<byte>)` internally uses the same IOCP/epoll machinery as SAEA — the performance difference is negligible. The `Pipe` adds built-in backpressure (`pauseWriterThreshold` / `resumeWriterThreshold`) that SAEA doesn't provide.

**Alternative considered:** Keep SAEA for plaintext, add separate Stream+Pipe path for TLS. Rejected — two I/O paths means double the maintenance, testing, and optimization work.

**Reference implementation:** TurboMQTT `TcpTransportActor.cs` — `DoWriteToPipeAsync`, `ReadFromPipeAsync`, `DoWriteToSocketAsync` pattern.

### 2. IStreamProvider abstraction

**Decision:** Introduce `IStreamProvider` with `ConnectAsync(host, port, ct) → Stream` and `Close()`. `TcpStreamProvider` returns `NetworkStream`. Future `TlsStreamProvider` returns `SslStream`.

**Rationale:** Proven in TurboMQTT. TLS handshake happens inside `ConnectAsync` — by the time the `Stream` is returned, it's authenticated and ready for data. The TCP actor never knows whether it's encrypted or not.

**Alternative considered:** TLS as a BidiFlow stage in Akka.Streams. Still viable as a composable option, but `IStreamProvider` is simpler for the common case and doesn't require Streams-level composition.

### 3. Copy on read, pool on write

**Decision:** Read path copies from `Pipe` buffer to a `MemoryPool<byte>.Shared.Rent()` buffer before emitting `Tcp.Received`. Write path accepts pooled `ReadOnlyMemory<byte>` buffers with bounded lifecycle (disposed after send).

**Rationale:** The actor model has no mechanism to signal when a consumer is done with a message's data. `Tcp.Received` data could be stored in actor state indefinitely, forwarded to other actors, or parsed into strings. The read buffer cannot be returned to a pool because its lifetime is unbounded. Writes have bounded lifecycle — the buffer is consumed by `Stream.WriteAsync` and can be released immediately after the call completes.

### 4. Drop netstandard2.0 + net6.0 → net10.0 only

**Decision:** Remove `netstandard2.0` and `net6.0` from all target framework specifications. All library projects target `net10.0` only.

**Rationale:** `netstandard2.0` lacks `Stream.ReadAsync(Memory<byte>)`, `Socket.SendAsync(Memory<byte>)`, `Span<T>` APIs, and `System.IO.Pipelines` support. The spec originally proposed `netstandard2.1` as the replacement, but no version of .NET Framework supports `netstandard2.1` — so .NET Framework compatibility is lost either way. `netstandard2.1` as a target buys almost nothing over `net10.0` (only .NET Core 3.x and Mono/Xamarin, which are EOL). Targeting `net10.0` (current LTS) directly eliminates all conditional compilation, polyfill packages, and multi-TFM complexity. Tests already target `net10.0`.

### 5. Hard-delete ByteString

**Decision:** Delete `Akka.Util.ByteString` entirely rather than providing shims or adapters.

**Rationale:** ByteString is used pervasively but almost exclusively as a payload carrier in `Tcp.Write`, `Tcp.Received`, and Akka.Streams elements. A shim would perpetuate the incompatibility with System.Memory. Clean deletion forces all consumers to migrate at once, which is appropriate for a major version change.

### 6. Background task pattern for I/O loops

**Decision:** Use three background `Task`s coordinated through the actor mailbox (self-tell on completion/error), matching the TurboMQTT `TcpTransportActor` pattern.

- `ReadFromStreamTask`: `stream.ReadAsync()` → `pipe.Writer.Advance()` → `pipe.Writer.FlushAsync()`
- `ReadFromPipeTask`: `pipe.Reader.ReadAsync()` → `MemoryPool.Rent()` → copy → emit `Tcp.Received` via actor Tell
- `WriteToStreamTask`: dequeue write commands → `stream.WriteAsync()` → ACK via actor Tell

**Rationale:** The TurboMQTT codebase resolved several race conditions with this exact pattern (fire-and-forget tasks, CTS coordination, shutdown sequencing). The fixes are documented in Memorizer. Reuse the proven coordination pattern including `Interlocked.CompareExchange` for CTS cancellation guards, `Task.WhenAll` + `ContinueWith` self-tell for background task lifecycle tracking, and ordered self-tell before caller-tell to prevent mailbox ordering races.

### 7. Accepted socket handoff for incoming connections

**Decision:** `TcpListener` accepts a `Socket` via `Socket.AcceptAsync()`, wraps it in `new NetworkStream(socket, ownsSocket: true)`, and passes the `Stream` to `TcpIncomingConnection` which uses the same `IStreamProvider`-less Pipe pattern (it already has a connected Stream).

**Rationale:** `IStreamProvider.ConnectAsync()` is a client-side abstraction (DNS resolution, connection initiation). For accepted server-side connections, the socket is already connected. The `Stream` wrapping happens at the accept point, then `TcpIncomingConnection` uses the same Pipe-based I/O loop as outgoing connections.

### 8. Ownership-carrying Tcp.Write (dispose at the pipe-copy point)

**Motivation:** The `ReadOnlySequence<byte>` write surface (Decisions 1–5) enables pooled callers, but ownership of a pooled buffer is inexpressible on the wire type — nothing says who is responsible for returning it, or when. Two incidents demonstrate this is a real, not theoretical, gap:

- **(a) `TcpConnection`'s pre-registration write queue retained caller buffers by reference.** A `Tcp.Write` arriving before `Register` was queued for up to `RegisterTimeout` actor turns with no copy taken in the turn that received it — a pooled/reusable-buffer caller could mutate its buffer while the bytes were still sitting unread in the queue. This was real corruption, fixed defensively by copying at enqueue in PR #8323. That copy **remains** in place under this design as the fallback for *borrowed* (owner-less) writes — it is not removed.
- **(b) Artery's zero-copy encode stage (`ArteryEncodeStage`) has to *guess* when the pipe copy has happened** from upstream ack/pull signals, because there is no explicit ownership-transfer point on `Tcp.Write` to hook. A static audit concluded it was safe to dispose a pushed frame's pooled buffer on the stage's own very next `OnPull` (reasoning that the TCP write stage only pulls after a `WriteAck`, which is only sent after `TcpConnection.EnqueueWrite` has synchronously copied the frame into the output pipe). An empirical poison-pool stress test (real `ActorSystem`s, 300 back-to-back messages, pool arrays scribbled on return) **proved the audit wrong**: the pull this stage receives is not 1:1 with "the previous frame's bytes have left the buffer" under sustained load — 1–4 of 300 messages corrupted on every run. The shipped workaround holds **two** buffer generations alive (disposing two pulls back, not one) to survive the observed one-generation pull-ahead. Lifetime inference at a distance from the actual copy is fragile by construction; this decision replaces inference with an explicit contract.

**Decision:** `Tcp.Write` (and the Akka.Streams TCP write path) can optionally carry an `IMemoryOwner<byte>` alongside its `ReadOnlySequence<byte>` payload. Passing an owner transfers ownership to the connection on send — the caller MUST NOT touch the buffer again once sent. `TcpConnection` disposes the owner at the exact point the payload has been copied into the output pipe (`TcpTransportConnection.WriteAsync` returning — same actor turn, open/registered path), and on every non-success path as well:

- **Pre-registration queueing** keeps the owner alive until the deferred `FlushPendingRegistrationWrites` → `EnqueueWrite` copy runs, then disposes it (the #8323 copy-at-enqueue fallback still applies to *borrowed* writes queued this way; an *owned* write queued pre-registration is disposed once its own deferred copy completes, not eagerly).
- **Queue-full rejection / `Tcp.CommandFailed`** disposes the owner before signaling failure to the sender — the buffer never reached the pipe, so nothing downstream can be reading it.
- **`PostStop` / connection drain** disposes every owner still held by queued (pending-registration or pending-write) commands.

Borrowed (owner-less) writes are unaffected and keep today's semantics, including the #8323 pre-registration copy. `NoAck` + owned is safe by construction: disposal is driven by the copy having happened, not by acknowledgment.

**Alternative considered:** Keep the static lifetime-inference approach (dispose on next pull/ack, as `ArteryEncodeStage` did originally) and harden it further (e.g., a longer generation lag, or a stronger audited contract). Rejected — the poison-pool test already falsified the "one generation is enough" audit once; inference at a distance from the actual copy has no principled bound on how many generations of lag are enough, only empirically-discovered ones. Disposal belongs at the place the copy happens, not wherever a consumer happens to infer it must have happened by now.

**Explicitly NOT in scope:** eliminating the pipe-staging copy itself. Benchmarked N=3 on the 9900X (branch `bench/write-path-copy-costs`, commit `5b081dc7e`): the pipe copy costs ~197ns / 0B alloc at 256B and ~561ns at 4KB, versus ~987ns / ~843ns + 120B for a bounded-channel direct-write handoff reference implementation — the copy **wins** below the 4KB–64KB crossover. Zero-copy-to-socket is deferred to the future large-message-stream work, with the burden of proof on a byte-aware handoff (cf. `experiment/akka-io-spsc-output`). This decision is about **lifetime semantics** — who disposes what, and when — not about removing the copy or improving throughput.

**Consumers:** `ArteryEncodeStage` swaps its two-generation disposal lag for direct owner-passthrough on `Tcp.Write`, deleting the pull/ack inference entirely. Any other pooled Akka.IO caller (present or future) gets the same explicit transfer-of-ownership contract instead of having to reinvent generation-lag bookkeeping.

**Constraint:** This must land before v1.6 ships — the write surface goes extend-only once v1.6 is out, so the ownership-carrying overload needs to exist at the same time as the rest of the `ReadOnlySequence<byte>` write surface, not bolted on afterward.

**Mechanism refinement (2026-07-07) — ownership rides in the `ReadOnlySequence<byte>` segment, not a `Tcp.Write` field.** Tracing the actual Artery outbound graph (`ArteryRemoting.MaterializeOutboundStream`) settled how the owner travels from producer to disposal point, and it changes the API shape from what the Decision prose above (and the first drafts of tasks 9.2/9.5/9.6) assumed:

- The graph is `… → OutboundHandshakeStage → ArteryEncodeStage → Source.Single(preamble).Concat(frames) → killSwitch → [KillSwitches.Single] → WatchTermination → Tcp.OutgoingConnection → Sink.Ignore`. The owner is **minted** at `ArteryEncodeStage` (`PooledPayloadWriter.Detach()`) and must **die** at the pipe copy inside `Tcp.OutgoingConnection` (`TcpConnection`). Everything between is pass-through: the preamble is a one-time `Concat`, **not** a per-frame reframe, and no stage rebuffers the bytes. The buffer encode mints is exactly the buffer the socket write consumes — one ownership span, no intermediate seam.
- A field on `Tcp.Write` only solves the *message-carrying* leg. The owner still has to ride encode → coalescing, and the element on that wire is `ReadOnlySequence<byte>`, which has no field for it — so segment-carried ownership is required *regardless*. Once it exists, a separate `Tcp.Write` owner field is redundant.
- **Mechanism:** an owner-aware `ReadOnlySequenceSegment<byte>` (defined in the `Akka` assembly, alongside `PooledPayloadWriter`, so `Akka.IO`, `Akka.Streams`, and `Akka.Remote` can all see it). `ArteryEncodeStage` pushes a single-segment `ReadOnlySequence<byte>` whose segment holds the `IMemoryOwner<byte>`; it flows transparently through every pass-through stage. The coalescing stage in `TcpStages.cs` (`WriteBufferSegment`, already a `ReadOnlySequenceSegment<byte>`) chains those segments **zero-copy** — so `Tcp.Write.Data` transparently contains all N frames' owners with **no `Tcp.Write` API change**. `TcpConnection` walks `Data`'s segments after the pipe copy and disposes each owner-carrying one; same walk on every non-success path.
- **N owners per write (the original singular-owner prose does not cover this):** coalescing flushes N frames as one `Tcp.Write`, so disposal is per-segment over the chained sequence, not a single owner. It falls out of the segment chain for free — a write carries as many owners as it has owner-carrying segments (0 for a borrowed/preamble write, N for a coalesced one).
- **Ownership is single-disposer, and disposal is single-threaded — no synchronization on the segment.** Each owner is disposed by exactly one party: whichever currently holds the segment. The `Tell` that hands a `Tcp.Write` to `TcpConnection` is both the ownership-transfer point and the memory barrier (mailbox enqueue/dequeue) that publishes the segment's fields to the actor thread, so the producing stream stage (interpreter thread) and the consuming actor (dispatcher thread) never touch the same owner, and never on two threads at once — a plain field, no `Interlocked`/`Volatile`. Even the teardown paths operate on **disjoint** owners: on abort the coalescing stage disposes only what is still in its buffer (never handed off), while `TcpConnection` disposes only what it was already `Tell`ed — an owner is in exactly one of those sets. The owner's own `Dispose()` being idempotent (`RentedMemoryOwner` nulls its array) is kept only as a cheap same-thread guard against a double reach (e.g. a reject path then drain), not as a cross-party mechanism. The hand-enforced invariant: never dispose an owner whose bytes could still be read — dispose only at the pipe copy in steady state, and on abort paths only where the write is provably dropped.

This is an API-surface *reduction* versus the prose above: **no public `Tcp.Write`/`SimpleWriteCommand` owner overload is added** — the owner is internal, carried in the segment. Extend-only is still satisfied; the only public-facing change is behavioral (`TcpConnection` now disposes owner-carrying segments it finds in `Write.Data`), and borrowed (owner-less) writes are byte-for-byte unaffected.

**Delivery split (2026-07-07) — two squash-merged PRs on a real coupling boundary** (a PR squashes to one `dev` commit, so an independently-revertable unit ⇒ its own PR):

- **PR1 — Akka.IO foundation (§9.1–§9.4 + the segment type):** the owner-aware segment + `TcpConnection` disposal matrix + path-by-path synthetic poison-pool tests. Purely additive — no consumer wired up, existing behavior unchanged — so it merges safely alone, and it isolates the shared-`TcpConnection` disposal change (data-corruption-critical for *every* Akka.IO caller) as its own revertable/bisectable `dev` commit.
- **PR2 — consumer adoption (§9.5–§9.6, lands with the Artery write-coalescing work):** `ArteryEncodeStage` owner-passthrough (delete the 2-gen lag) + `TcpStages.cs` coalescing switched to zero-copy segment-chaining. These two are **atomic** — coalescing that zero-copy-chains buffers whose encode stage still self-disposes on the old cadence corrupts, so they cannot be split across PRs.

## Risks / Trade-offs

**[Massive compilation breakage from ByteString deletion]** → Methodical approach: change TFMs first, then delete ByteString and fix compilation errors module by module (Akka.IO → Akka.Streams → Akka.Remote → Cluster → Contrib). Use compiler errors as the migration guide.

**[Write batching regression]** → Current SAEA uses `BufferList` for scatter-gather (multiple buffers in one syscall). Stream.WriteAsync takes a single buffer. Mitigation: the Pipe naturally consolidates small writes. If needed, implement explicit batching in the write task (coalesce pending writes before calling `Stream.WriteAsync`). Benchmark to verify.

**[Race conditions in background task coordination]** → Apply all lessons from TurboMQTT fixes: ordered self-tell before caller-tell, `Interlocked.CompareExchange` for CTS, `Task.WhenAll` tracking, proper PipeWriter completion on all exit paths.

**[MemoryPool fragmentation under high load]** → `MemoryPool<byte>.Shared` uses `ArrayPool` internally, which handles fragmentation well. Monitor in benchmarks. If needed, custom pool with fixed-size slabs.

**[Akka.Streams TCP bridging complexity]** → `TcpStages.cs` currently bridges actor messages ↔ stream elements using `ByteString`. Changing to `ReadOnlyMemory<byte>` is a type swap in the stage handlers — the bridging pattern doesn't change.

**[Ownership-carrying Tcp.Write disposal matrix is easy to get wrong]** → `TcpConnection` has several non-success exit paths for a queued write (queue-full rejection, `CommandFailed`, pre-registration deferral, `PostStop`/drain) in addition to the open-path copy point. Missing a path either leaks the owner (never disposed) or disposes it too early (corrupts an in-flight write) — exactly the failure mode the Artery poison-pool test caught once already. Mitigation: enumerate every path explicitly as its own task (see tasks.md §9) and cover each with a poison-pool style corruption test, not just the open-path happy case.
