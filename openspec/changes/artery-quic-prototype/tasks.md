## 1. Native adapter and shared protocol

- [x] Add explicit experimental QUIC selection while retaining TCP defaults and existing wire framing.
- [x] Reuse typed Artery TLS setup and Akka.IO certificate/trust policies with native TLS 1.3 and ALPN.
- [x] Add bounded connection/stream admission, authentication deadlines, cancellation and native ownership.
- [x] Carry control, ordinary and optional large traffic through the shared Artery protocol stages.
- [x] Add separately bounded ordinary-stream counts and fixed lane-to-stream FIFO grouping.
- [x] Keep per-read scratch storage private and publish stable copies of actual received bytes.
- [x] Add opt-in ordinary QUIC I/O statistics; keep diagnostics disabled by default.
- [ ] Complete broader same-UID lost-ACK, quarantine, stale-completion and native-resource stress gates.

## 2. Comparison source

- [x] Extend RemotePingPong with explicit transport/credential selection while preserving legacy commands.
- [x] Support realistic serializer payloads, separate ping-pong/one-way modes and lane/stream sweeps.
- [x] Validate actual stream counts, outbound recipient occupancy and configured receive-path budgets.
- [x] Add sequential warmed comparison tooling that retains invalid attempts and distributions.
- [x] Add optional process CPU, allocation/GC and ordinary QUIC I/O diagnostic output.
- [ ] Add per-message sequence/duplicate accounting and controlled offered-load latency measurements.
- [ ] Complete native-resource and isolated two-host/impairment evaluation before those claims.

Benchmark execution is paused. Machine records, benchmark results and private research notes are
not part of this source/specification change.

## 3. Verification

- [x] Build the integrated Release source and run affected Artery native/ordering tests on a supported host.
- [x] Run public API approvals and scoped incremental configuration checks; do not count native skips as passes.
- [x] Validate benchmark CLI parsing without starting new benchmark runs.
- [x] Run Slopwatch, whitespace and strict OpenSpec validation.
- [x] Independently review source, buffer ownership, privacy exclusions and PR history.
- [ ] Add a surviving single-lane queued-shutdown assertion; the existing DeathWatch branch removes its only recipient.

## 4. Proposed pipe follow-up

- [ ] Review the proposed layering and explicit managed/native memory accounting.
- [ ] Implement a bounded directional byte bridge and persistent pumps without changing TCP in the first slice.
- [ ] Prove retained-byte safety, owner return, blocked-producer bounds/resume and control/healthy-stream progress.
- [ ] Await actual writer drain/FIN for every QUIC role before graceful shutdown completes.
- [ ] Re-measure unprofiled throughput and separate diagnostics before selecting the adapter or making promotion claims.
