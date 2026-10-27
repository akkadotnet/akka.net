# RemotePingPong

`RemotePingPong` is the Akka.Remote throughput harness. It moves messages between two `ActorSystem`s
across a real remote transport and reports msgs/sec, so you can compare transports, tuning knobs, and
serializers against each other over an otherwise identical workload.

Unlike `../PingPong` (which measures local actor mailboxes with no network in the loop), this project
always runs through `Akka.Remote`. It can also run the two systems in two separate processes on two
separate machines, which is the only way to measure a real network.

## What it measures

Three things, each selectable without touching code:

| Axis | Options | What it changes |
|------|---------|-----------------|
| Transport | DotNetty TCP (default), Artery TCP (`--artery`) | Which remote driver carries the traffic |
| Traffic pattern | ping-pong (default), one-way firehose (`--oneway`) | Whether every message round-trips |
| Payload | `"hit"` string (default), realistic message (`--payload real --serializer …`) | Whether serialization cost is in the measured path |

Everything else — actor counts, message totals, timing windows — stays the same across those axes, so
the numbers compare directly.

## Build and run

The project targets `$(NetTestVersion)` (currently `net10.0`) and is `IsPackable=false`; it is not a
NuGet package. Build it like any other project in the repo:

```bash
dotnet build src/benchmark/RemotePingPong/RemotePingPong.csproj -c Release
```

Then run either form:

```bash
# via dotnet run (note the -- separator before the harness's own args)
dotnet run --project src/benchmark/RemotePingPong -c Release -- run --artery

# or the built binary directly
dotnet src/benchmark/RemotePingPong/bin/Release/net10.0/RemotePingPong.dll run --artery
```

Always build `-c Release`. A Debug build measures the wrong thing.

The harness sets `ProcessPriorityClass.High` at startup. If the OS refuses (common for
non-privileged users on Linux), it prints a warning and carries on at normal priority — worth fixing
before trusting a run, since priority skew changes throughput.

Server GC is on by default: `RemotePingPong.csproj` sets `ServerGarbageCollection` and `TieredPGO`, and
`App.config` sets `gcServer`. `Is Server GC: True` in the output header confirms it took effect.

## Modes

```
RemotePingPong [run | server | client] [options]
```

Invoking with **no subcommand** is identical to `run` with every option at its default.

### `run` — single-process loopback (default)

Both `ActorSystem`s (`SystemA` and `SystemB`) live inside this process and talk over `127.0.0.1` on
OS-assigned ephemeral ports. This is the quick A/B harness and the historical behavior of this project.

Because both sides share one process, they share GC, thread pool, and CPU. Numbers from this mode are
great for relative comparisons (`--artery` on vs. off at the same settings) and poor as absolute
throughput claims for a deployed system.

### `server` / `client` — split two-process mode

Runs the two sides in separate processes so they can sit on two physical machines and cross a real
NIC.

The **server is deliberately dumb.** It creates no benchmark actors of its own. Every echo/receiver
actor is `RemoteScope`-deployed onto it by the client, so it lands under
`/remote/[scheme]/SystemA@[myhost]:[port]/…`. The remote-deployment daemon is already a per-client
actor factory keyed by client identity, which is why no run-id negotiation protocol is needed. The
server serves an arbitrary number of sequential runs and stays up until you stop it. Ctrl+C is handled:
it terminates the `ActorSystem` instead of hard-killing the process. A raw `SIGTERM` is **not** handled,
so it stops the process without that clean shutdown — prefer Ctrl+C (or `kill -INT`).

```bash
# machine A — the receiving side
dotnet RemotePingPong.dll server --host 10.0.0.5 --oneway --window 200 --clients 25

# machine B — drives the benchmark, repeats the sweep 3x
dotnet RemotePingPong.dll client --times 3 --host 10.0.0.5 --oneway --window 200 --clients 25
```

Two rules that are easy to get wrong:

- **`--host` is both bind and advertise address.** Neither transport config sets a separate bind
  hostname, so the listener binds directly to whatever you pass. Use the machine's LAN IP — not
  `0.0.0.0`.
- **The client's `--host` must match the server's `--host` exactly.** That string is part of the
  association key both sides agree on; a hostname/IP mismatch just never connects.

The client's own advertised address defaults to auto-detection: it opens a connected UDP socket toward
the server and reads the local endpoint the kernel picked. That sends nothing, so it works before the
server is up, and it picks the right NIC on a multi-homed box. Override it with `--myhost` when the
guess is wrong. Replies matter in both patterns (echoes in ping-pong, `Ack`/`Complete` credit grants in
one-way), so this address must be reachable **from the server** — never `localhost` when the server is
another machine. A client header line prints the resolved address and tags it
`(auto-detected)` or `(explicit --myhost)` so a bad guess is visible.

Split mode is not identical to `run` in one respect, and says so in its header: **the server's
`ActorSystem` persists across all `--times` reps while the client recreates its own system each rep.**
Single-process mode restarts both. Split-mode rep 1 is therefore warmer than historical single-process
rep 1 numbers. Server-side actors are `PoisonPill`ed after each rep (outside the timed window) so a
long-lived server does not accumulate idle actors across runs.

## Options

| Option | Default | `run` | `server` | `client` | Maps to |
|--------|---------|:-----:|:--------:|:--------:|---------|
| `--artery` | off | ✓ | ✓ | ✓ | `akka.remote.artery.enabled = on` instead of `akka.remote.dot-netty.tcp` |
| `--oneway` | off | ✓ | ✓ | ✓ | one-way firehose instead of ping-pong |
| `--window N` | `50` | ✓ | ✓ | ✓ | in-flight priming window per client |
| `--clients N` | sweep | ✓ | ✓ | ✓ | pin one client count instead of sweeping |
| `--msgs N` | `100000` | ✓ | | ✓ | total messages per client |
| `--times N` | `1` | ✓ | | ✓ | repeat the whole sweep/run |
| `--iobuf SIZE` | untouched | ✓ | ✓ | ✓ | `akka.io.tcp.receive-buffer-size` / `send-buffer-size` |
| `--qsize N` | auto | ✓ | ✓ | ✓ | `akka.remote.artery.advanced.outbound-message-queue-size` |
| `--inbound-lanes N` | untouched | ✓ | ✓ | ✓ | `akka.remote.artery.advanced.inbound-lanes` |
| `--outbound-lanes N` | untouched | ✓ | ✓ | ✓ | `akka.remote.artery.advanced.outbound-lanes` |
| `--payload toy\|real` | `toy` | ✓ | ✓ | ✓ | which object crosses the wire |
| `--serializer v2\|protobuf\|msgpack` | — | ✓ | ✓ | ✓ | serializer bound to the real payload type |
| `--host ADDR` | **required** | | ✓ | ✓ | server bind + advertise address |
| `--port N` | `25520` | | ✓ | ✓ | server listener port |
| `--myhost ADDR` | auto-detect | | | ✓ | client's own advertised address |

`--port` defaults to `25520` on both sides (matching Artery's `canonical.port` default, also Pekko's)
and is used uniformly for both transports so command lines stay identical across them — note that this
is *not* DotNetty's own `2552` default, since the harness always writes the port explicitly. The client's
own system always binds an ephemeral port.

`--msgs` and `--times` are accepted by the client and by `run`, not by the server: the client owns the
protocol and decides when a rep is over.

The server accepts `--window`, `--clients`, and `--oneway` so an orchestrator can pass the same values
to both sides and eyeball consistency in the headers. Functionally, only the queue and lane sizes change
anything on the server: they size *its* outbound queue (reply traffic) and its Artery lane counts.

### `--artery`

Switches both systems from the classic DotNetty TCP transport to Artery TCP. It is the only difference
between a DotNetty run and an Artery run — same actors, same message counts, same timing. Wire
addresses differ accordingly (`akka.tcp://…` vs `akka://…`), and the harness builds the server address
by hand in split-client mode, so `--artery` must be passed to **both** sides.

### `--oneway`

Default ping-pong sends a message and the destination echoes every one back, so half your measured
traffic is replies. `--oneway` drops the reply loop: a sender fires a credit-based stream at a remote
receiver and measures pure one-directional transport throughput (the Pekko
`max-throughput`-style shape).

Credit control exists because unbounded fire-and-forget would overflow Artery's bounded outbound queue
and silently starve, with no reply loop to pace the sender. The sender pushes a full `--window` up
front, the receiver grants more credit with a small `Ack` every `window / 2` messages, and the
receiver's count is authoritative for completion. Total unacked messages are capped at
`clients × window` across the run.

If a window goes missing, the sender blind-resends the last batch after a 5-second ack timeout, up to
60 attempts before failing the rep. Any resend makes that rep's numbers suspect, and it prints a
magenta warning line saying so — on a healthy transport that line never appears.

### `--window N`

How many messages are in flight per client before the harness waits for completion — "priming the
pump" so the transports' writers get to batch. Must be greater than zero. In ping-pong it is the
outstanding echo count; in one-way it is the send credit. Raise it to test batching and queueing
behavior; lower it to test latency-bound throughput.

### `--clients N`

Without it, the harness sweeps **1, 5, 10, 15, 20, 25, 30** client pairs and prints a row per step.
With it, one count runs once. Pin it for quick A/Bs and for reproducing a specific reported number.

### `--msgs N` / `--times N`

`--msgs` overrides the 100,000-message-per-client total. One-way runs finish fast enough at the default
that a single rep can be sub-second, which is too short to see steady state; push it into multi-second
territory without editing the constant. `--times` repeats the entire sweep — useful for checking
variance, and note that the first rep of a process pays JIT warmup.

### `--iobuf SIZE`

Sets both `akka.io.tcp.receive-buffer-size` and `send-buffer-size` (Akka's IO default is `8k`) for every
system in the run. Takes a HOCON size value verbatim, so `128k` and `1m` both work. Omit it and no HOCON
key is written at all.

### `--qsize N`, queue auto-raise, and drops

`--qsize` overrides `akka.remote.artery.advanced.outbound-message-queue-size` (default `3072`, per
association), the bounded queue every client's outbound traffic funnels through. Plain integer — the
underlying key is read with `GetInt`, not `GetByteSize`, so unlike `--iobuf` there are no size suffixes.

If you omit `--qsize`, the harness raises the queue for you when the run would otherwise overflow it: if
`window × clients` (using the pinned count, or the sweep's maximum) exceeds 80% of 3072, the queue goes
to 2× that product and the header prints why, in yellow:

```
Outbound queue size (artery):      6000 (auto-raised)
outbound-message-queue-size auto-raised to 6000 (window x clients = 3000 exceeds default 3072)
```

An explicit `--qsize` always wins. When neither applies, the key is never written, so a default-shaped
run has the identical config it always had.

Overflow here is not an error — Artery drops messages on a full outbound queue (at-most-once by
design), and `akka.loglevel = ERROR` would hide even that. So the harness subscribes to
`Akka.Event.Dropped` on the local `EventStream` directly and prints a magenta line whenever a rep
dropped anything:

```
  *** SUSPECT: dropped 214 (outbound queue overflow) ***
```

A rep with drops is a rep whose throughput number means nothing. In split mode the server runs its own
watcher and prints deltas every 5 seconds to its own stdout, so both sides stay visible.

### `--inbound-lanes N` / `--outbound-lanes N`

Override `akka.remote.artery.advanced.inbound-lanes` and `outbound-lanes` (both default `1` in
`Remote.conf`): how many lanes ordinary-stream messages fan out across, for parallel deserialization
inbound and parallel writing outbound. Must be greater than zero.

Two caveats from the transport's own docs worth knowing before you tune these: `outbound-message-queue-size`
applies **per lane**, so raising `--outbound-lanes` multiplies total buffering rather than dividing it;
and each inbound lane has its own bounded consumer queue that backpressures rather than dropping.

### Artery-only options

`--qsize`, `--inbound-lanes`, and `--outbound-lanes` tune keys DotNetty's transport never reads. They are
accepted in DotNetty mode and reported as ignored rather than silently doing nothing:

```
--qsize ignored (DotNetty)
--inbound-lanes/--outbound-lanes ignored (DotNetty)
```

## Payload and serializer options

### `--payload toy` (default)

The actors exchange the literal string `"hit"`, serialized by Akka's default NewtonsoftJson fallback.
Nothing about this path is meant to be realistic — it is the historical baseline, and leaving `--payload`
out reproduces it byte-for-byte.

### `--payload real --serializer <arm>`

Sends a realistic message instead: primitives (`int`, `long`, `double`, `bool`), a string, a nested
complex type (`DeviceInfo`), and a collection of 8 `Reading` entries. One canonical instance built by
`RealPayload/RealPayloadFactory.cs` feeds every arm, and its values are fixed literals — no randomness,
no clock reads — so every run and every arm serializes identical logical content and A/B comparisons
mean what they look like.

`--serializer` picks which serializer crosses the wire, and is **required** with `--payload real`. The
three arms carry the same content by different means:

| Arm | Implementation |
|-----|----------------|
| `v2` | `Akka.Serialization.V2` source-generated MessagePack (`[AkkaSerializable]` / `[AkkaField(n)]`; the generator emits the serializer partial at build time) |
| `protobuf` | hand-written `Google.Protobuf`, string manifest; C# generated at build time from `RealPayload/Protobuf/RealBenchmarkMessage.proto` via `Grpc.Tools`, the same convention `Akka.Remote` uses for its own `.proto` files |
| `msgpack` | hand-written `SerializerWithStringManifest` over an attribute-based `[MessagePackObject]`/`[Key(n)]` POCO — MessagePack directly, no generator |

`--payload real` writes `akka.actor.serializers` and `serialization-bindings` for the chosen arm onto
every system in the invocation. There is no negotiation over the wire, so in split mode you must pass
the **same** `--payload`/`--serializer` flags to both `server` and `client`.

Before any actor starts, the harness proves the wiring: it resolves the serializer Akka actually picked
for the payload, prints its type and id, and round-trips the message. It throws if the resolved
serializer is not the requested arm or the round-trip is not value-equal — which is what makes a silent
fallback to NewtonsoftJson from a bindings typo impossible to miss:

```
Real-payload serializer wiring (system "SystemA"):
  --serializer arm:                  v2
  Resolved serializer:               RemotePingPong.RealPayload.V2.RealBenchmarkSerializer (Id=987001)
  Round-trip check:                  PASSED (348 bytes)
```

`--serializer` is rejected with `--payload toy`, and both flags are validated before any system starts.

## Reading the output

The header reports the environment and the effective configuration, then one table row per client
count:

```
Transport:                         DotNetty
Mode:                              ping-pong
OSVersion:                         Unix 6.8.0.137
ProcessorCount:                    8
ClockSpeed:                        0 MHZ
Actor Count:                       16
Messages sent/received per client: 400000  (4e5)
Is Server GC:                      True
Thread count:                      35
Window size (in-flight):           50
Pinned client count:               1

Num clients, Total [msg], Msgs/sec, Total [ms], Start Threads, End Threads
         1,  400000,    120410,    3322.71,            35,              59
Done..
```

- **`Messages sent/received per client`** is `msgs × 2` in ping-pong (every message travels out and
  back) and `msgs` in one-way (no reply loop). Keep that in mind comparing rows across modes.
- **`Total [msg]`** is `clients × msgs × 2` for ping-pong, `clients × msgs` for one-way.
- **`Msgs/sec`** is `Total [msg] / elapsed`, rounded up. A `-1` here means the rep finished inside one
  measured millisecond — lower `--msgs` or read `Total [ms]` directly.
- **Green** marks a row that matched or beat the best throughput so far in the sweep; **red** marks one
  that did not. Colors track the sweep's shape, not correctness.
- **`ClockSpeed`** is always `0`: reading it lives behind a `THREADS` compile symbol that the build never
  defines. **`Actor Count`** is just `ProcessorCount × 2`. Both are environment notes, not settings.
- **`Start Threads` / `End Threads`** bracket the timed window, so you can see how many threads a
  configuration costs.
- Anything printed in **magenta** below a row — drops, one-way resends — invalidates that row's numbers.

## Recipes

```bash
# DotNetty vs Artery, everything else identical
dotnet RemotePingPong.dll run
dotnet RemotePingPong.dll run --artery

# one-way throughput sweep, 3 reps to see variance
dotnet RemotePingPong.dll run --artery --oneway --times 3

# long one-way run so the rep is not sub-second
dotnet RemotePingPong.dll run --oneway --window 1000 --clients 10 --msgs 500000

# serializer comparison on realistic payloads (also the fastest way to sanity-check a wire format)
dotnet RemotePingPong.dll run --artery --payload real --serializer v2       --times 3
dotnet RemotePingPong.dll run --artery --payload real --serializer protobuf --times 3
dotnet RemotePingPong.dll run --artery --payload real --serializer msgpack  --times 3

# lane parallelism, Artery only
dotnet RemotePingPong.dll run --artery --inbound-lanes 2 --outbound-lanes 2

# two machines, Artery
dotnet RemotePingPong.dll server --artery --host 10.0.0.5 --oneway --window 200 --clients 25
dotnet RemotePingPong.dll client --artery --host 10.0.0.5 --oneway --window 200 --clients 25 --times 3
```

## Getting representative numbers

- Release build, `Is Server GC: True`, and the priority warning absent. Then run it three times and
  ignore the first — the process warms up.
- Same rules as any benchmark: nothing else loadable on the box, and do not compare a loopback `run`
  number to a split-mode number. They measure different things.
- Never overlap two invocations of this harness on one machine. Shared CPU and GC make both results
  meaningless.
- Only trust rows with no magenta lines under them.
- `msgs/sec` alone conflates CPU cost with frame size. When you change payload or serializer, read it
  next to `Serialized payload size` — the arms produce very different frame sizes for identical logical
  content. That figure is the serialized message only; DotNetty adds a 4-byte length prefix plus a
  `RemoteEnvelope` protobuf wrapper on top, and Artery adds its own compact binary header.

## Configuration written by this harness

Every system in a run gets this base, from `CreateActorSystemConfig()`:

```hocon
akka {
  actor.provider = remote
  loglevel = ERROR
  suppress-json-serializer-warning = on
  log-dead-letters = off
  remote.log-remote-lifecycle-events = off
}
```

plus exactly one transport block (`akka.remote.artery` or `akka.remote.dot-netty.tcp` with the host and
port), plus `akka.io.tcp` buffer keys only if `--iobuf` was passed, plus
`akka.remote.artery.advanced` keys only if Artery mode is on and an explicit `--qsize`/`--inbound-lanes`/
`--outbound-lanes` or the queue auto-raise actually resolved a value, plus `akka.actor` serializer
bindings only for `--payload real`. Options left out write no HOCON at all, which is what keeps
default-shaped runs comparable to old ones.

## Source layout

| File | Contents |
|------|----------|
| `Program.cs` | CLI parsing, config assembly, both benchmark patterns, all actors |
| `RealPayload/RealPayloadFactory.cs` | the one canonical real message and its per-arm conversions |
| `RealPayload/V2/` | `[AkkaSerializable]` POCO; the generator emits the serializer partial |
| `RealPayload/Protobuf/` | `.proto` and its hand-written serializer |
| `RealPayload/MsgPack/` | `[MessagePackObject]` POCO and its hand-written serializer |
| `App.config` | `gcServer` for the .NET Framework-style host config |

Actors: `BenchmarkActor` + `EchoActor` (ping-pong), `OneWaySenderActor` + `OneWayReceiverActor`
(one-way), `AllStartedActor` (the `Identify` readiness handshake that confirms every remote actor is
reachable before timing starts), `DropCounterActor` (queue-overflow visibility).
