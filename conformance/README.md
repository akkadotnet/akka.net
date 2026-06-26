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
