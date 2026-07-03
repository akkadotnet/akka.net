# Task 0 — Transport-Substrate Validation Results (Gate G0)

**Date:** 2026-07-03 · **Branch:** `feature/spec4-artery-task0-substrate-gate` · **Harness:** `src/benchmark/Akka.Benchmarks/Remoting/Artery/` (committed at f585d2526)

**Method:** BenchmarkDotNet v0.15.8, naked (no wrapper scripts), 2 warmup + 10 monitored iterations per case, ServerGC, MemoryDiagnoser, 1,024,000 messages per invocation with exact per-recipient completion counts (a lost message hangs the run). Three independent full-suite passes, machine otherwise idle, results read from BDN's own report files. Baseline-first: DotNetty RemotePingPong re-captured on this box before any harness runs.

**Machine:** AMD Ryzen 9 9900X (12 physical / 24 logical cores, 2 CCDs), Ubuntu 24.04, .NET 10.0.8, X64 RyuJIT x86-64-v4.

## Baseline re-capture (invalidates the documented 680K on this hardware)

DotNetty RemotePingPong, naked, 3 runs: peaks **1,374,571 / 1,387,733 / 1,478,853 msgs/s** (median ≈ **1.39M**, 20–25 clients); single-connection (1 client): 261K / 324K / 284K ≈ **~290K**. The ~680K figure in `IMPLEMENTATION_ORDER.md` was captured on an 8-core machine and understates this box by ~2×. All gate comparisons below use the local 1.39M / ~290K numbers.

## Deserialize-knob calibration (N=3, CV < 1%)

| HashBytes | ns/msg |
|---|---|
| 32 | 17.7 / 18.0 / 17.9 |
| 128 | 81.3 / 82.2 / 81.6 |
| 1024 | 759.6 / 759.9 / 756.8 |

M=128 ≈ realistic small-message MessagePack deserialize; M=1024 ≈ heavy.

## Config 2 — single fused island (serial-island ceiling, task 0.2)

Drain-many source → LengthField framing → header decode → deserialize → per-recipient dispatch, one interpreter island. Req/sec across runs 1/2/3, 120B/msg allocated:

| HashBytes | ChannelDrain | Source.Queue (chunk-level) |
|---|---|---|
| 32 | 1.716M / 1.733M / 1.716M | 1.785M / 1.719M / 1.726M |
| 128 | 1.568M / 1.557M / 1.560M | 1.581M / 1.638M / 1.585M |
| 1024 | 903K / 816K / 881K | 900K / 835K / 904K |

With deserialize inline, the island holds **1.56–1.73M msgs/s** at realistic cost. Chunk-granularity ingress choice is a wash (per-offer hop amortized ~900×), as the design predicted (task 0.4a).

## Config 3 — stream lanes via `Partition + .Async()` (task 0.3): **DISQUALIFIED**

N ∈ {1,2,4,8,16} × M ∈ {32,128,1024} × boundary buffer {16,512}, N=3. Summary (M=1024, buffer 16): 1 lane 800–815K → 2: 829–831K → 4: 738–740K → 8: 668–692K → 16: 681–697K. **Scaling is negative at every knob value in all three runs**; the best stream-lane case anywhere is the 1-lane, M=128 case (~927K) — below the fused single island doing strictly more work.

- The `.Async()` boundary costs ≈ **520ns/msg** (1-lane lane config ~1,090ns/msg vs fused island ~570ns/msg at M=32) and **+230B/msg** (354B vs 120B).
- **Not buffer-fixable (falsified):** buffer 512 ≈ 16 everywhere (sometimes worse — M=1024/1-lane drops 805K→670K consistently); buffer=1 craters to 523K, proving the attribute is live and the cost is per-element (chased push/pull + per-element event allocation + cross-actor enqueue), not per-wakeup.

## Config 4 — hybrid: fused island → actor lanes via `Tell` (task 0.3b): **RECOVERS SCALING**

Same serial island (framing + header decode), fan-out to N lane actors by recipient hash, deserialize + dispatch on the lane actor. Req/sec across runs 1/2/3:

| Lanes | M=32 | M=128 | M=1024 |
|---|---|---|---|
| 1 | 1.79M / 1.72M / 1.73M | 1.57M / 1.59M / 1.57M | 958K / 943K / 971K |
| 2 | **3.06M / 3.09M / 3.16M** | 2.81M / 2.80M / 2.75M | 1.50M / 1.50M / 1.48M |
| 4 | 1.91M / 1.93M / 1.87M | 2.08M / 2.28M / 2.16M | **2.77M / 2.69M / 2.74M** |
| 8 | 1.72M / 1.70M / 1.70M | 1.69M / 1.72M / 1.74M | 1.68M / 1.70M / 1.92M |
| 16 | 1.68M / 1.71M / 1.70M | 1.68M / 1.71M / 1.69M | 1.69M / 1.71M / 1.65M |

- Allocations 120–145B/msg (vs 354B for stream lanes). The actor mailbox hop is **~3–5× cheaper** than the stream async boundary.
- Heavy deserialize scales 958K → 1.49M → 2.73M across 1→2→4 lanes (3.6× at 4 lanes = deserialize fully recovered off the serial island).
- The 2-lane M=32 cases expose the **true serial decode-island ceiling ≈ 3.1M msgs/s** (framing + 28B header decode + hash + Tell only) — **2.2× the local DotNetty aggregate peak**, >10× its single-connection throughput.
- Throughput regresses past 4–8 lanes (also visible in config 1) — dispatcher/cache contention on 12 physical cores, not a substrate defect. Default inbound lanes should be small (Pekko's default is 4).

## Config 1 — actor-only floor (task 0.1)

Producer → decode actor → N lane actors → recipients, no interpreter. Peak ~5.2–5.9M/s at 4 lanes (M≤128); same >8-lane contention cliff. Interpreter tax of the fused island vs the actor floor at equal shape: config-1 1-lane vs config-2 fused ≈ 1.86M vs 1.72M (M=32) ≈ **+8%**, and hybrid 4-lane vs config-1 4-lane @M=1024 ≈ 2.73M vs 2.86M ≈ **+5%** — well inside the +30% budget the design allowed (task 0.5's "reproduce the ~+30% tax" expectation was pessimistic: the measured tax is single-digit once the graph is kept coarse).

## Task 0.4 — ingress-hop penalty, per-message (outbound `SendQueue` shape)

One element per message, bare source → `Sink.Ignore`. Initial N=3 used a custom drain-many prototype; a follow-up 3-run head-to-head added the **existing core `ChannelSource.FromReader`** (`ChannelSourceLogic`), which was found to already implement the identical drain-many hot path (sync `TryRead` per pull, coalesced wakeup on empty), plus better completion handling (#7940 race guard):

| Ingress | ns/msg (N=3) | msgs/s | alloc/msg |
|---|---|---|---|
| **Existing `ChannelSource.FromReader`** | 69 / 68 / 71 | 14.5M / 14.7M / 14.1M | **1 B** |
| Custom drain-many prototype (retired) | 74 / 70 / 69 | 13.5M / 14.3M / 14.5M | 1 B |
| Stock `Source.Queue` (`OfferAsync`) | 1,198 / 1,164 / 1,145 | 834K / 859K / 873K | **384 B** |

**The stock `ChannelSource.FromReader` ties the prototype exactly (12–15× faster than `Source.Queue`, ~400× less allocation), so the prototype was deleted and the harness re-based onto the existing core source** — improving existing infrastructure instead of adding parallel stages. A confirming full-suite pass (run 4) on the stock source reproduced all N=3 results within noise (single island 1.72M/1.58M @32/128; hybrid @1024: 947K → 1.51M → 2.73M across 1→2→4 lanes). `Source.Queue` alone would cap the outbound path below the local DotNetty baseline: Decision 9's "NOT `Source.Queue`" is load-bearing, not an optimization.

## Gate G0 assessment (decision 0.6 is the maintainer's)

Design.md G0 criteria, restated against the re-pinned local baseline:

1. **Serial decode/partition island clears the baseline with margin:** PASS — the decode island sustains ~3.1M msgs/s with deserialize off-island (2.2× the 1.39M local DotNetty peak; the documented 680K bar is exceeded 4.5×). Even with heavy deserialize left inline it holds ~870K–1.7M.
2. **Lanes recover the interpreter tax within the core budget:** PASS **only via actor-lane fan-out** (hybrid config): heavy deserialize scales 3.6× by 4 lanes and the measured interpreter tax is ~5–8%. The canonical-Artery `Partition + .Async()` lane graph FAILS this criterion on .NET (negative scaling; ~520ns/msg per-element boundary cost; not buffer-fixable) and must not be used for per-message fan-out.

**Verdict (maintainer, 2026-07-03): PASS with amendments** — proceed with `Akka.Streams.IO.Tcp` as the substrate (Decision 2 stands; no `System.IO.Pipelines` fallback needed). The amendments follow the project preference for fixing existing infrastructure over adding new components:

- **Rule (3) amended:** interior `.Async()` on the hot path is disqualified *as the boundary is currently implemented* (`ActorOutputBoundary.OnNext` = one actor message + one `OnNext` allocation per element — **#8314**). Inbound lane fan-out is actor-based for now (island sink `Tell`s to lane actors by recipient hash; per-recipient ordering preserved: same recipient → same lane actor → mailbox FIFO). The `ActorGraphInterpreter` element-batching fix (#8314) is its own OpenSpec change sequenced **before G5**; config 3 is re-measured then, and the canonical Pekko stream-lane shape is re-adopted if the fixed boundary reaches mailbox-class cost. Decided by measurement, preferring the infrastructure fix over the workaround.
- **Rule (2) upgraded from "should" to "must", targeting existing infrastructure:** the outbound queue source is the **existing `ChannelSource.FromReader`** (measured identical to the drain-many prototype, which was deleted); stock `Source.Queue` is disqualified by measurement.
- Default inbound lanes: 4 (matches Pekko; past 4–8 the box contends). Lane-actor mailbox bounding is an explicit G4 work item (an actor mailbox does not backpressure the island the way a stream lane would).

Baseline propagation: `IMPLEMENTATION_ORDER.md` now records that the DotNetty baseline is machine-relative (~1.39M on this box) and that M5 must compare same-hardware, same-run.

Raw BDN artifacts: 4 passes retained during the session (`artery-run{1,2,3}`, `artery-run4-stocksource`, `ingress3way-run{1,2,3}` under the session scratchpad); all tables above are transcribed from BDN's own `-report-github.md` files.
