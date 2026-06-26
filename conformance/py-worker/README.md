# Python cluster worker (Flask-like)

A from-scratch **Python** implementation of an Akka.NET cluster node, certified by **ACT (the Akka
Conformance Tester)** — with a deliberately **Flask-like interface** for hosting actors.

The membership protocol (the ASSOCIATE handshake, heartbeats, gossip convergence, graceful leave) lives
in the `akkaflask` package; the only application code you write is the actor, registered just like a
Flask route — and the handler's **return value is the reply** to the message's sender:

```python
from akkaflask import Cluster

app = Cluster("akka.tcp://ConformanceCluster@127.0.0.1:5110", port=6300)

@app.actor("/user/echo")          # cf. Flask's @app.route("/echo")
def echo(msg):
    return msg                    # cf. returning a response — here it's sent back to the sender

app.run()                         # join, converge, serve actors, leave gracefully
```

A cluster broadcast router fans a message out to `/user/echo` on every node; this worker's `echo`
handler bounces it back, which is conformance **step 6 (Broadcast routee delivery)**.

## Layout (standard library only — no pip installs)

- `akkaflask/proto.py` — hand-rolled proto3 encode/decode.
- `akkaflask/wire.py` — 4-byte LE framing, the ASSOCIATE handshake, the remote envelope,
  `ActorSelection`/`SelectionEnvelope` unwrapping, cluster messages, and gossip "seen"-set surgery.
- `akkaflask/cluster.py` — the `Cluster` framework: `@app.actor` registry, the bidirectional node
  (a listener thread for the seed's dial-back, a sender on the outbound connection), heartbeat
  responses, gossip convergence, and graceful leave. Handlers' return values become replies.
- `worker.py` — the entry point: registers `/user/echo` and runs the lifecycle.

## Running it against the ACT host

```bash
# 1) Start the reference seed (from the repo root)
dotnet run --project conformance/act-host -- --port=5110 --seconds=40
#   prints: SEED_URI=akka.tcp://ConformanceCluster@127.0.0.1:5110

# 2) Run the worker against that seed
cd conformance/py-worker
python3 worker.py --seed=akka.tcp://ConformanceCluster@127.0.0.1:5110 --port=6300
```

Pass `--leave=false` to make the worker stay Up without leaving — ACT then stops at step 7 and teaches
what a graceful leave requires.
