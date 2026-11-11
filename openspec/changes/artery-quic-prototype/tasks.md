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

## 4. Parallel-lane performance follow-up

The design's [current direction](design.md#parallel-lane-performance-direction--2026-11-11)
supersedes making a pipe rewrite the required next step. Earlier pipe prototypes remain research
controls; they are not selected production source. The retained baseline is `70a2ed1d6`.

- [x] Reconcile retained source, historical pipe/resource findings and standalone native-worker limits into the existing design.
- [ ] Establish the contemporary fixed-eight-processing-lane S=1/2/4/8 encrypted control using the existing comparison driver and actual path-count gates.
- [ ] Move linked cancellation state to channel lifetime in one isolated source change; preserve native ownership and canceled/faulted completion status.
- [ ] Execute delayed-write cancellation/reset/reconnect and exactly-once release checks, then affected native Artery/API/incremental/Slopwatch gates and independent review.
- [ ] Compare frozen candidate/control at eight streams with one-stream regression, separate allocation/CPU/write-shape diagnostics, matched paced RTT and leased-capacity gates; retain null results.
- [ ] Only if evidence selects it, isolate producer batching from native coalescing size; preserve prompt idle/control dispatch and fixed aggregate budgets.
- [ ] Reconsider pipes/pumps only against the accepted predecessor with tail-latency, retained-capacity and ownership evidence; preserve stable inbound chunks.
- [ ] Keep the broader peer-consumed shutdown, ACK/quarantine and resource-stress gaps visible; native send completion is not peer delivery.

This update contains planning/specification only. Proposed source changes, tests and new performance
comparisons have not been executed. Benchmark numbers and private machine records remain outside
this source/specification change. Current default transport and stream counts are unchanged.
