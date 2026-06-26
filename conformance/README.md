# ACT conformance — verify by hand

This directory lets you build and verify the **ACT (Akka Conformance Tester)** system end to end on
your own machine (macOS incl. Apple Silicon / M3, or Linux).

- **`act-host/`** — a C# console that runs an instrumented *reference seed*. It embeds the modified
  `Akka.Cluster` (the `protocol-recorder`) plus the ACT harness, observes a node-under-test, and prints
  the ACT verdict (the 10-step ladder) and the full captured trace.
- **`go-worker/`** — a from-scratch Akka.NET cluster node in Go.
- **`js-worker/`** — the same in JavaScript (Node.js).
- **`py-worker/`** — the same in Python, with a Flask-like `@app.actor` interface for its actors.

Each worker speaks the real Akka.NET remoting + cluster wire protocol and is driven through:
join → converge → **broadcast routee delivery** → graceful leave → clean shutdown (10 steps).

> The reference seed must be built from this branch's source because it embeds the *modified*
> `Akka.Cluster`; there is no stock-NuGet shortcut. The first build compiles the core (~3–5 min).

## Requirements (one-time, on M3 via Homebrew)

```bash
brew install dotnet go node     # all have native arm64 builds
dotnet --version                # need .NET 10.x
```

Everything runs on `127.0.0.1` (loopback), so there's no networking setup and no firewall prompts.

## One-shot verification

```bash
cd conformance
./verify.sh          # or: make verify
```

This builds the reference seed and the Go worker, runs the **C#** in-process suite (positive + negative),
then drives the **Go** and **JavaScript** workers — each against its own fresh reference seed — and prints:

```
== Summary ==
  C# (in-process) : PASS
  Go              : PASS
  JavaScript      : PASS
  ALL WORKERS PASSED the 10-step ACT conformance ladder.
```

## Hands-on (watch it happen, two terminals)

**Terminal 1 — start a reference seed** (prints `SEED_URI`, then the verdict + trace when the worker leaves):

```bash
cd conformance
make seed                       # binds 127.0.0.1:5110, runs ~45s
# -> SEED_URI=akka.tcp://ConformanceCluster@127.0.0.1:5110
```

**Terminal 2 — run a worker against that URI:**

```bash
# Go
cd conformance/go-worker && go build -o go-worker .
./go-worker --seed=akka.tcp://ConformanceCluster@127.0.0.1:5110 --port=6000

# or JavaScript
cd conformance/js-worker
node worker.js --seed=akka.tcp://ConformanceCluster@127.0.0.1:5110 --port=6100

# or Python (Flask-like)
cd conformance/py-worker
python3 worker.py --seed=akka.tcp://ConformanceCluster@127.0.0.1:5110 --port=6300
```

Terminal 1 prints `CONFORMANCE PASSED — all 10 steps satisfied` plus the ordered protocol/membership/
routing trace.

### See ACT "stop and teach"

Run a worker with `--leave=false` so it joins and converges but never leaves gracefully. ACT stops at
the graceful-leave step and prints a language-agnostic explanation of what's required:

```bash
./go-worker --seed=<SEED_URI> --port=6000 --leave=false
# Terminal 1: CONFORMANCE FAILED at step 7 of 10: Graceful leave announced (Leaving) ...
```

## Just the C# side (only .NET needed)

```bash
cd conformance && make verify-cs
```

Runs the in-process conformance suite: one worker that passes all 10 steps, and a crashing worker that
ACT catches at the graceful-leave step.

## Make targets

| target | what it does |
|---|---|
| `make verify` | build + run all three workers through the ladder |
| `make verify-cs` | run only the C# in-process suite |
| `make build` | build the reference seed and the Go worker |
| `make seed` | start a foreground reference seed for hands-on runs |
| `make clean` | remove build artifacts |

---

## Code map (for Akka.NET maintainers)

If you maintain Akka.NET and want to review or extend this, here's where the pertinent code lives.
**The only change to core is one opt-in, default-off recorder**; everything else is additive — a new
contrib library, a console host, and four standalone workers. Paths are repo-root-relative; line
numbers are approximate.

### 1. The one core change — `Akka.Cluster` protocol recorder (opt-in, default `off`)

A node with `akka.cluster.protocol-recorder = on` publishes a structured `ClusterProtocolEvent` to the
system `EventStream` (and logs a `CLUSTER-PROTOCOL …` line) for each membership-protocol message it
sends/receives. With the flag off it uses a no-op recorder (zero overhead). **All new types are
`internal`, so there is no public-API change** (API approval untouched), and the 364 existing
`Akka.Cluster.Tests` pass unchanged.

| File | Symbol(s) | What to look at |
|---|---|---|
| `src/core/Akka.Cluster/ClusterProtocolRecorder.cs` | `ClusterProtocolEvent` (~40), `IClusterProtocolRecorder` (~94), `NoOpClusterProtocolRecorder`, `EventStreamClusterProtocolRecorder` (~132, `LogPrefix="CLUSTER-PROTOCOL"`), `ClusterProtocolRecorderFactory` (~169) | the whole feature; a no-op vs event-stream recorder chosen by the flag |
| `src/core/Akka.Cluster/ClusterDaemon.cs` | `_protocolRecorder` field + ctor init (~982); **11 one-line `Record(...)` call sites** (~1322–2490): InitJoin, Join, Leave, ExitingConfirmed (in); InitJoinAck/Nack, Welcome×2, Gossip (out/in) | the only edits to the daemon hot path — each is gated by the no-op recorder when off |
| `src/core/Akka.Cluster/Configuration/Cluster.conf` | `protocol-recorder = off` (~18) | the flag + its doc comment |
| `src/core/Akka.Cluster/Properties/AssemblyInfo.cs` | `InternalsVisibleTo("Akka.Cluster.Conformance"[".Tests"])` | lets the harness see the internal event type |

### 2. The reference seed + ACT — `src/contrib/cluster/Akka.Cluster.Conformance/`

| File | Symbol(s) | Responsibility |
|---|---|---|
| `Act.cs` | `Act.Check` (~337), `Steps` ladder (~154), `Step`/`Context` | the 10-step **stop-and-teach** checker — each step is a `(predicate, language-agnostic teach message)` pair; `Check` stops at the first unmet one |
| `ConformanceModel.cs` | `ConformanceTrace` (~103), `ConformanceEvent`, `ConformanceSource{Protocol,Membership,Routing}`, `HasDirected` | the ordered, thread-safe trace the verdict is derived from |
| `ConformanceRecorderActor.cs` | subscribes to `ClusterProtocolEvent` (EventStream) **and** `ClusterEvent` membership events | merges both streams, in arrival order, into the trace |
| `ReferenceSeed.cs` | `StartAsync`; broadcast wiring (~103–112): `EchoActor`, `ClusterRouterGroup(BroadcastGroup("/user/echo"), …)`, `BroadcastCollectorActor` | boots the instrumented single-node seed (flag on), hosts `/user/echo`, the broadcast router, and the collector |
| `BroadcastProbe.cs` | `EchoActor` (~20), `BroadcastCollectorActor` (~36) | the routee (replies to sender) + the periodic broadcaster that records `RoutedReply` routing events |
| `WorkerUnderTest.cs` | `InProcessWorker` (~23) | the C# node-under-test: a stock cluster node + `/user/echo`, with an optional crash mode |
| `…Conformance.Tests/ClusterConformanceSpecs.cs` | positive + negative tests | a stock worker passing all 10 steps; a crasher caught at the graceful-leave step |
| `conformance/act-host/Program.cs` | `Main` | the runnable seed that prints `SEED_URI`, the verdict, and the trace (`make seed`) |

### 3. The workers — the same protocol, four languages

The C# worker is `InProcessWorker` above. The Go/JS/Python workers are standalone and structurally
identical — a wire library plus a node. Compare any one concern across the three columns:

| Concern | Go (`conformance/go-worker/`) | JavaScript (`conformance/js-worker/`) | Python (`conformance/py-worker/`) |
|---|---|---|---|
| hand-rolled protobuf | `proto.go` | `proto.js` | `akkaflask/proto.py` |
| framing + PDUs + envelope | `akka.go` | `akka.js` | `akkaflask/wire.py` |
| 4-byte LE frame reader | `read/writeFrame` | `FrameReader` (~79) | `read_frame`/`frame` |
| ASSOCIATE handshake | `constructAssociate` (akka.go:135), `connectOutbound`/`listen` (node.go:93/153) | `constructAssociate` (akka.js:100), `connectOutbound`/`listen` (worker.js:78/106) | `construct_associate` (wire.py:104), `_connect`/`_listen` (cluster.py:159/194) |
| remote envelope | `constructMessage` (akka.go:200), `parsePdu` (akka.go:503) | `constructMessage` (akka.js:117), `parsePdu` (akka.js:173) | `construct_message` (wire.py:122), `parse_pdu` (wire.py:182) |
| ActorSelection unwrap (serializer 6) | `parseSelectionEnvelope` (akka.go:278) | `parseSelectionEnvelope` (akka.js:214) | `parse_selection_envelope` (wire.py:241) |
| gossip "seen"-patch → convergence | `patchGossipSeen` (akka.go:436), `onGossip` (node.go:263) | `patchGossipSeen` (akka.js:312), `onGossip` (worker.js:190) | `patch_gossip_seen` (wire.py:346), `_on_gossip` (cluster.py:300) |
| cluster heartbeat reply | `dispatch` (node.go:207) | `dispatch` (worker.js:138) | `_dispatch` (cluster.py:246) |
| **echo routee** | `isEchoSelection` + echo branch (node.go:22/217) | echo branch (worker.js:150) | `@app.actor` + `_invoke_actor` (cluster.py:101/280) |
| graceful leave / Exiting | `onStatus`/`sendLeave` (node.go:312/61), `main` (main.go:32) | `onStatus`/`sendLeave` (worker.js:212/234), `main` (worker.js:240) | `_on_status`/`run` (cluster.py:324/109) |

**The Python worker's distinguishing feature** is a Flask-like surface over the same protocol: the
`Cluster.actor(path)` decorator (`cluster.py:101`) registers a handler whose **return value becomes the
reply** (`_invoke_actor`, `cluster.py:280`). Everything below that line is the identical wire protocol.

### 4. The 10-step ladder ↔ code

Each step is recorded on the **seed** (left) and must be produced by the **worker** (right):

| # | Step | Recorded by the seed | Produced by a worker |
|---|---|---|---|
| 1 | Initial contact | `ClusterDaemon` `Record(InitJoin/InitJoinAck)` | sends `InitJoin` |
| 2–3 | Join / Welcome | `Record(Join)` / `Record(Welcome)` | sends `Join`; accepts `Welcome` |
| 4 | Gossip participation | `Record(Gossip, Inbound)` (ACT requires **inbound**) | echoes gossip back |
| 5 | Convergence to Up | `ClusterEvent.MemberUp` | adds itself to the gossip `seen` set |
| 6 | Broadcast routee delivery | `BroadcastCollectorActor` → `RoutedReply` | hosts `/user/echo`, replies |
| 7–9 | Leaving / Exiting / ExitingConfirmed | `MemberLeft` / `MemberExited` / `Record(ExitingConfirmed)` | sends `Leave`, then `ExitingConfirmed` on reaching Exiting |
| 10 | Clean removal | `MemberRemoved` (previousStatus=Exiting, never Downed) | stays reachable through removal |

The teach text for any step lives next to its predicate in `Act.cs` `Steps` — that's the canonical,
language-agnostic spec of what a node must do at that point.
