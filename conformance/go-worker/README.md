# Go cluster worker (ACT conformance node-under-test)

A from-scratch **Go** implementation of an Akka.NET cluster node, built to be tested by
**ACT (the Akka Conformance Tester)** running on a C# reference seed. It was developed by following
ACT's "stop and teach" messages one step at a time until it passed all nine conformance steps and
completed the full membership lifecycle against the real C# seed.

It speaks the Akka.NET remoting + cluster wire protocol directly — no Akka libraries, no codegen:

- **Framing** — 4-byte little-endian length prefix (`akka.go`).
- **Remoting handshake** — the Akka protocol `ASSOCIATE` exchange, bidirectionally: the worker dials
  the seed (connection A, worker→seed) *and* listens on its advertised port for the seed's dial-back
  (connection B, seed→worker), because Akka opens a separate association per direction (`node.go`).
- **Hand-rolled protobuf** — just enough of `WireFormats` / `ContainerFormats` / `ClusterMessages`
  to build and parse the PDUs (`proto.go`, `akka.go`).
- **Cluster messages** (ClusterMessageSerializer, id 5): `InitJoin`, `Join`, `Welcome`, `GossipEnvelope`,
  `Heartbeat`/`HeartbeatRsp`, `Leave`, `ExitingConfirmed`.
- **ActorSelection unwrapping** (MessageContainerSerializer, id 6): the seed sends gossip and
  heartbeats via `ActorSelection`, so they arrive wrapped in a `SelectionEnvelope` that must be unwrapped.
- **Staying reachable** — answers cluster heartbeats with `HeartbeatRsp`, or the seed's failure
  detector marks it unreachable and SBR downs the cluster.
- **Convergence** — on each gossip it adds its own address index to the gossip's `seen` set and echoes
  it back (leaving the vector clock untouched), so the leader observes convergence and moves it to Up.
- **Graceful leave** — sends `Leave(self)`, watches its own status in gossip, and sends
  `ExitingConfirmed` when it reaches Exiting, so the leader removes it cleanly (never Downed).

## Running it against the ACT host

```bash
# 1) Start the reference seed (from the repo root)
dotnet run --project conformance/act-host -- --port=5110 --seconds=40
#   prints: SEED_URI=akka.tcp://ConformanceCluster@127.0.0.1:5110

# 2) In another shell, build and run the worker against that seed
cd conformance/go-worker && go build -o go-worker .
./go-worker --seed=akka.tcp://ConformanceCluster@127.0.0.1:5110 --port=6000
```

The ACT host prints the verdict (`CONFORMANCE PASSED — all 9 steps satisfied`) and the full captured
protocol/membership trace when its window ends. Run `--leave=false` to make the worker quit without
leaving — ACT then stops at step 6 and teaches what a graceful leave requires.
