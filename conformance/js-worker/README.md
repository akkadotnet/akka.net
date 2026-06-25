# JavaScript cluster worker (ACT conformance node-under-test)

A from-scratch **JavaScript / Node.js** implementation of an Akka.NET cluster node, certified by
**ACT (the Akka Conformance Tester)** on a C# reference seed. Like the Go worker, it was built by
following ACT's "stop and teach" messages one failing step at a time until it passed all nine.

No Akka libraries, no protobuf codegen, no npm dependencies — just Node's `net` module and hand-rolled
protobuf over `Buffer`:

- `proto.js` — minimal proto3 encode/decode (varints via BigInt for exact 64-bit fields).
- `akka.js` — 4-byte little-endian framing, the Akka `ASSOCIATE` handshake, the remote envelope,
  `ActorSelection`/`SelectionEnvelope` unwrapping, and the cluster messages (`InitJoin`, `Join`,
  `Welcome`, `GossipEnvelope`, `Heartbeat`/`HeartbeatRsp`, `Leave`, `ExitingConfirmed`), plus the
  gossip "seen"-set surgery used to drive convergence.
- `worker.js` — the bidirectional node: dials the seed (conn A) **and** listens for the seed's
  dial-back (conn B), answers cluster heartbeats to stay reachable, echoes gossip with itself marked
  seen to converge to Up, then leaves gracefully (Leave → Exiting → ExitingConfirmed → Removed).

## How it was grown (one failing test at a time)

| Stage | Added | ACT result |
|-------|-------|------------|
| 1 | framing + ASSOCIATE + InitJoin + Join | steps 1–3 pass, stops at 4 (gossip) |
| 2 | listener + ActorSelection unwrap + HeartbeatRsp + gossip seen-echo | steps 4–5 pass, stops at 6 (leave) |
| 3 | Leave(self) + Exiting detection + ExitingConfirmed | all 9 pass |

## Running it against the ACT host

```bash
# 1) Start the reference seed (from the repo root)
dotnet run --project conformance/act-host -- --port=5110 --seconds=40
#   prints: SEED_URI=akka.tcp://ConformanceCluster@127.0.0.1:5110

# 2) Run the worker against that seed
cd conformance/js-worker
node worker.js --seed=akka.tcp://ConformanceCluster@127.0.0.1:5110 --port=6100
```

Pass `--leave=false` to make the worker stay Up without leaving — ACT then stops at step 6 and teaches
what a graceful leave requires.
