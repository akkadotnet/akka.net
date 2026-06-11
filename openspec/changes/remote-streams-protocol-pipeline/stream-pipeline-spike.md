# Stream TCP Pipeline Spike

## Scope

The first stream transport spike adds an opt-in TCP transport class backed by Akka.Streams TCP and the existing Akka.IO.Tcp substrate:

- Transport class: `Akka.Remote.Transport.Streams.TcpStreamTransport, Akka.Remote`
- Opt-in shape: override `akka.remote.dot-netty.tcp.transport-class` while keeping `akka.remote.enabled-transports = ["akka.remote.dot-netty.tcp"]`
- Public scheme remains `akka.tcp` because the raw transport scheme remains `tcp` and the existing `AkkaProtocolTransport` wrapper still augments it.
- Stream write inlet buffering is controlled by `akka.remote.dot-netty.tcp.stream-write-buffer-size` and defaults to `65536` elements for this spike.

This proves a stream-backed TCP substrate can interoperate with classic DotNetty using the same length-framed Akka.Remote PDU bytes.

## Current Boundary

This spike intentionally keeps the existing classic protocol wrapper in place:

- `EndpointWriter` still serializes actor messages and writes payload bytes.
- `EndpointReader` still deserializes unwrapped payload bytes and dispatches messages.
- `AkkaProtocolTransport` / `ProtocolStateActor` still own handshake, heartbeat, `refuseUid`, disassociation, and listener buffering semantics.
- `TcpStreamTransport` owns only socket I/O, DotNetty-compatible length framing, and the raw `AssociationHandle` bridge.

## Compatibility Test

`StreamTcpTransportInteropSpec` validates:

- Classic DotNetty node sends to stream TCP node.
- Stream TCP node sends to classic DotNetty node.
- Stream TCP node shuts down and restarts on the same address.
- Classic DotNetty node reconnects to the restarted stream TCP node.
- Stream TCP node can still send back to the classic DotNetty node after restart.
- Stream TCP nodes can exchange messages with each other in both directions.

## RemotePingPong Smoke Result

Command:

```bash
dotnet run -c Release --project "src/benchmark/RemotePingPong/RemotePingPong.csproj" -- 1 stream
```

Environment summary from the run:

- OS: Unix 6.8.0.117
- Processor count: 8
- Server GC: true
- Transport: Akka.Streams TCP

| Num clients | Total messages | Msgs/sec | Total ms | Start threads | End threads |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 200000 | 85361 | 2343.11 | 26 | 50 |
| 5 | 1000000 | 360621 | 2773.77 | 50 | 54 |
| 10 | 2000000 | 406092 | 4925.38 | 54 | 54 |
| 15 | 3000000 | 489158 | 6133.34 | 53 | 52 |
| 20 | 4000000 | 522194 | 7660.26 | 47 | 45 |
| 25 | 5000000 | 631712 | 7915.40 | 45 | 45 |
| 30 | 6000000 | 623377 | 9625.28 | 43 | 43 |

This is a smoke result, not a final benchmark. The stream transport still uses the existing `AkkaProtocolTransport` / `ProtocolStateActor` wrapper and bridges framed payloads through `ByteString`.

## Source.ActorRef Revert Confirmation

Command:

```bash
dotnet run -c Release --project "src/benchmark/RemotePingPong/RemotePingPong.csproj" -- 1 stream
```

Environment summary from the run:

- OS: Unix 6.8.0.117
- Processor count: 8
- Server GC: true
- Transport: Akka.Streams TCP
- Write inlet: `Source.ActorRef`

| Num clients | Total messages | Msgs/sec | Total ms | Start threads | End threads |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 200000 | 89326 | 2239.59 | 25 | 45 |
| 5 | 1000000 | 541126 | 1848.32 | 45 | 50 |
| 10 | 2000000 | 590145 | 3389.45 | 50 | 50 |
| 15 | 3000000 | 460759 | 6511.61 | 48 | 49 |
| 20 | 4000000 | 625000 | 6400.36 | 47 | 47 |
| 25 | 5000000 | 617742 | 8094.51 | 47 | 44 |
| 30 | 6000000 | 618366 | 9703.65 | 44 | 44 |

This restores the faster fire-and-forget stream inlet after the `Source.Queue` experiment below.

## TCP Stage NoAck Smoke Result

Command:

```bash
dotnet run -c Release --project "src/benchmark/RemotePingPong/RemotePingPong.csproj" -- 1 stream
```

Environment summary from the run:

- OS: Unix 6.8.0.117
- Processor count: 8
- Server GC: true
- Transport: Akka.Streams TCP
- Write inlet: `Source.ActorRef`
- TCP stream stage: writes use `Tcp.NoAck` and pull the next upstream element immediately after enqueueing to Akka.IO TCP

| Num clients | Total messages | Msgs/sec | Total ms | Start threads | End threads |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 200000 | 96433 | 2074.60 | 27 | 32 |
| 5 | 1000000 | 558972 | 1789.18 | 32 | 36 |
| 10 | 2000000 | 655953 | 3049.72 | 36 | 50 |
| 15 | 3000000 | 663424 | 4522.44 | 50 | 51 |
| 20 | 4000000 | 636639 | 6283.79 | 51 | 51 |
| 25 | 5000000 | 660241 | 7573.99 | 51 | 50 |
| 30 | 6000000 | 738735 | 8122.63 | 50 | 50 |

This removes one stage-actor message per outbound TCP write. Current Akka.IO TCP enqueues writes into a pipe and sends the write ack immediately, so the old `WriteAck` roundtrip did not represent socket-flush backpressure. This optimization improves the stream RemotePingPong hot path while keeping stream TCP functional tests passing.

## Rejected Queue Write Path Smoke Result

Command:

```bash
dotnet run -c Release --project "src/benchmark/RemotePingPong/RemotePingPong.csproj" -- 1 stream
```

Environment summary from the run:

- OS: Unix 6.8.0.117
- Processor count: 8
- Server GC: true
- Transport: Akka.Streams TCP
- Rejected write inlet: `Source.Queue` plus internal async association write seam

| Num clients | Total messages | Msgs/sec | Total ms | Start threads | End threads |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 200000 | 64999 | 3077.24 | 27 | 47 |
| 5 | 1000000 | 271887 | 3678.56 | 47 | 51 |
| 10 | 2000000 | 284415 | 7032.90 | 51 | 49 |
| 15 | 3000000 | 303921 | 9871.47 | 46 | 43 |
| 20 | 4000000 | 310126 | 12898.89 | 43 | 42 |
| 25 | 5000000 | 308757 | 16194.15 | 42 | 41 |
| 30 | 6000000 | 295698 | 20291.62 | 41 | 41 |

This experiment was reverted. One in-flight async queue offer per `EndpointWriter` adds an actor-turn and task-completion cost per payload, reducing throughput from the previous `Source.ActorRef` stream spike. The next performance slice should keep the faster fire-and-forget write inlet or move the protocol/write pump into a purpose-built stream stage or bounded writer pump without per-message `Source.Queue.OfferAsync` acknowledgements on the endpoint hot path.

## Deferred Work

The spike does not yet remove the inbound `ProtocolStateActor` mailbox hop. The future BidiFlow-style protocol replacement should handle the protocol events documented in `protocol-state-machine-map.md` and present the same `AkkaProtocolHandle` / `InboundAssociation` / `InboundPayload` / `Disassociated` behavior to existing remoting actors.

The spike also does not yet integrate the sequence/writer PDU codec into stream framing. Frames are currently bridged through `ByteString` at the classic transport boundary so the old protocol actor can remain authoritative.
