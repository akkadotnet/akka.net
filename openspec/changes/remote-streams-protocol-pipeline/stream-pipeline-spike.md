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

## Inbound Single-Segment Copy Smoke Result

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
- TCP stream stage: `Tcp.NoAck` write path
- Inbound bridge: single-segment `ReadOnlySequence<byte>` uses `ByteString.CopyFrom(payload.FirstSpan)` instead of `payload.ToArray()`

| Num clients | Total messages | Msgs/sec | Total ms | Start threads | End threads |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 200000 | 101575 | 1969.44 | 27 | 48 |
| 5 | 1000000 | 586855 | 1704.86 | 48 | 52 |
| 10 | 2000000 | 674764 | 2964.74 | 52 | 52 |
| 15 | 3000000 | 747199 | 4015.93 | 51 | 51 |
| 20 | 4000000 | 746130 | 5361.32 | 51 | 51 |
| 25 | 5000000 | 754262 | 6629.54 | 51 | 51 |
| 30 | 6000000 | 776097 | 7731.59 | 51 | 51 |

This removes an avoidable intermediate array allocation/copy for the common inbound frame shape emitted by the stream framing stage.

## Repeat Median Comparison

Commands:

```bash
dotnet run -c Release --project "src/benchmark/RemotePingPong/RemotePingPong.csproj" -- 1 stream
dotnet run -c Release --project "src/benchmark/RemotePingPong/RemotePingPong.csproj" -- 1
```

Each transport was run 3 times on the same branch and machine. The table reports median `Msgs/sec`.

| Num clients | Stream median | DotNetty median | Delta |
| ---: | ---: | ---: | ---: |
| 1 | 98961 | 77370 | +27.9% |
| 5 | 566573 | 371886 | +52.4% |
| 10 | 655738 | 521921 | +25.6% |
| 15 | 696056 | 562009 | +23.9% |
| 20 | 690013 | 577284 | +19.5% |
| 25 | 721397 | 594319 | +21.4% |
| 30 | 740833 | 617031 | +20.1% |

Stream sample `Msgs/sec` values:

| Num clients | Sample 1 | Sample 2 | Sample 3 |
| ---: | ---: | ---: | ---: |
| 1 | 102146 | 97944 | 98961 |
| 5 | 597015 | 566573 | 535046 |
| 10 | 593648 | 669345 | 655738 |
| 15 | 696056 | 679348 | 722022 |
| 20 | 626665 | 702371 | 690013 |
| 25 | 721397 | 749738 | 717979 |
| 30 | 740833 | 781352 | 727009 |

DotNetty sample `Msgs/sec` values:

| Num clients | Sample 1 | Sample 2 | Sample 3 |
| ---: | ---: | ---: | ---: |
| 1 | 77370 | 72860 | 79555 |
| 5 | 392004 | 361272 | 371886 |
| 10 | 521921 | 508389 | 525211 |
| 15 | 576148 | 562009 | 551572 |
| 20 | 577284 | 572738 | 584283 |
| 25 | 610278 | 584796 | 594319 |
| 30 | 617031 | 618876 | 597372 |

## Validation

Validation run after the TCP stage no-ack and inbound single-segment copy changes:

| Command | Result |
| --- | --- |
| `dotnet test "src/core/Akka.Remote.Tests/Akka.Remote.Tests.csproj" -c Release` | Passed: 378, skipped: 5 |
| `dotnet test "src/core/Akka.Streams.Tests/Akka.Streams.Tests.csproj" -c Release --filter "FullyQualifiedName~TcpSpec"` | Passed: 20, skipped: 3 |
| `dotnet test "src/core/Akka.Cluster.Tests/Akka.Cluster.Tests.csproj" -c Release` | Passed: 364 |
| `openspec validate remote-streams-protocol-pipeline --strict` | Valid |

Additional TCP stream regression coverage:

- `Outgoing_TCP_stream_must_not_drop_writes_when_remote_reads_slowly` delays server-side reads while the client writes 2048 64-byte chunks, then verifies exact byte-for-byte delivery.

## Multi-Segment Outbound Frame Smoke Result

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
- TCP stream stage: `Tcp.NoAck` write path
- Inbound bridge: single-segment `ReadOnlySequence<byte>` avoids intermediate `ToArray()`
- Outbound frame encoder: emits a multi-segment `[4-byte length] + [payload]` `ReadOnlySequence<byte>` instead of allocating and copying `[length + payload]`

| Num clients | Sample 1 msgs/sec | Sample 2 msgs/sec |
| ---: | ---: | ---: |
| 1 | 105319 | 119761 |
| 5 | 597015 | 572410 |
| 10 | 704226 | 724901 |
| 15 | 764916 | 731708 |
| 20 | 784776 | 772350 |
| 25 | 799106 | 783945 |
| 30 | 798616 | 846024 |

This removes the outbound payload pre-copy in `TcpStreamTransport.EncodeFrame`. The write path still copies segments into the Akka.IO TCP output pipe, but it no longer builds an intermediate contiguous `[header][payload]` array per frame.

## Akka.IO TCP NoAck Flush Batching Result

Implementation summary:

- `TcpConnection` batches no-ack writes when the active transport is `TcpTransportConnection`.
- A batch flushes after 32 no-ack writes, 64 KiB of no-ack payload, an immediate self-message, an acked write, or graceful/confirmed close.
- Acked writes keep the existing write-and-flush path and also flush any no-ack writes buffered ahead of them.
- Non-`TcpTransportConnection` implementations keep the existing write-and-flush behavior; no public `ITransportConnection` member was added.

Dedicated Akka.IO TCP benchmark sanity command:

```bash
dotnet run -c Release --project "src/benchmark/Akka.Benchmarks/Akka.Benchmarks.csproj" -- --filter "*TcpOperationsBenchmarks.ClientServerCommunication*" --job Dry --join
```

The existing `TcpOperationsBenchmarks` config adds `LongRun`; passing `--job Dry` adds a dry job instead of replacing the configured long job. The run therefore executed the broader parameter matrix and timed out near the later cases after producing partial current-branch data. It was useful as a sanity check that the Akka.IO TCP workload still ran, but it is not used as the comparative gate for this slice.

RemotePingPong stream command:

```bash
dotnet run -c Release --project "src/benchmark/RemotePingPong/RemotePingPong.csproj" -- 1 stream
```

RemotePingPong DotNetty command:

```bash
dotnet run -c Release --project "src/benchmark/RemotePingPong/RemotePingPong.csproj" -- 1
```

Environment summary:

- OS: Unix 6.8.0.117
- Processor count: 8
- Server GC: true
- Stream transport: Akka.Streams TCP with `Tcp.NoAck`, inbound single-segment copy avoidance, multi-segment outbound frame encoding, and Akka.IO TCP no-ack flush batching

Sequential stream samples were used. Two accidentally parallel stream samples were discarded because they interfered with each other.

| Num clients | Stream sample 1 | Stream sample 2 | Stream sample 3 | Stream median | DotNetty sample | Delta vs DotNetty |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 110254 | 108226 | 114091 | 110254 | 75302 | +46.4% |
| 5 | 630518 | 583091 | 629723 | 629723 | 396983 | +58.6% |
| 10 | 729395 | 688469 | 706215 | 706215 | 559441 | +26.2% |
| 15 | 796602 | 754148 | 744787 | 754148 | 603865 | +24.9% |
| 20 | 814996 | 795704 | 792394 | 795704 | 620733 | +28.2% |
| 25 | 807494 | 801925 | 777243 | 801925 | 634438 | +26.4% |
| 30 | 839161 | 841161 | 772400 | 839161 | 679041 | +23.6% |

The current stream medians are generally at or above the previous multi-segment outbound-frame smoke range while preserving the stream transport's lead over DotNetty. One sequential stream run logged a transient `EndpointDisassociatedException` during the benchmark but completed; focused stream remoting tests and the full Remote suite passed afterward.

Validation after the Akka.IO TCP no-ack flush batching change:

| Command | Result |
| --- | --- |
| `dotnet test "src/core/Akka.Tests/Akka.Tests.csproj" -c Release --filter "FullyQualifiedName~TcpConnectionBatchingSpec"` | Passed: 2 |
| `dotnet test "src/core/Akka.Streams.Tests/Akka.Streams.Tests.csproj" -c Release --filter "FullyQualifiedName~TcpSpec"` | Passed: 21, skipped: 3 |
| `dotnet test "src/core/Akka.Remote.Tests/Akka.Remote.Tests.csproj" -c Release --filter "FullyQualifiedName~MessageSerializerV2Spec|FullyQualifiedName~AkkaPduCodecWireFormatSpec|FullyQualifiedName~AkkaProtocolSpec|FullyQualifiedName~StreamTcpTransportInteropSpec"` | Passed: 26 |
| `dotnet test "src/core/Akka.Remote.Tests/Akka.Remote.Tests.csproj" -c Release` | Passed: 378, skipped: 5 |
| `dotnet test "src/core/Akka.Cluster.Tests/Akka.Cluster.Tests.csproj" -c Release` | Passed: 364 |
| `openspec validate remote-streams-protocol-pipeline --strict` | Valid |
| `git diff --check` | Passed |

Additional regression coverage:

- `TcpConnection_should_batch_no_ack_writes_before_flushing` verifies several no-ack writes are flushed to the stream as one batch.
- `Outgoing_TCP_stream_must_flush_small_no_ack_batch_when_upstream_completes` verifies a small no-ack batch below the threshold is still delivered when upstream completes.

## Remote TCP Framing Stage Result

Implementation summary:

- Added `RemoteTcpFraming` for DotNetty-compatible 4-byte Remote TCP length framing.
- Inbound stream path now uses a Remote-specific decoder that emits payload slices directly instead of generic `Framing.LengthField(...).Select(frame => frame.Slice(4))`.
- Outbound stream path now encodes frames before telling the `Source.ActorRef`, removing the outbound stream `Select(EncodeFrame)` stage.
- The wire format remains `[4-byte payload length][payload bytes]` using the configured DotNetty byte order.

RemotePingPong stream command:

```bash
dotnet run -c Release --project "src/benchmark/RemotePingPong/RemotePingPong.csproj" -- 1 stream
```

RemotePingPong DotNetty command:

```bash
dotnet run -c Release --project "src/benchmark/RemotePingPong/RemotePingPong.csproj" -- 1
```

Environment summary:

- OS: Unix 6.8.0.117
- Processor count: 8
- Server GC: true
- Stream transport: Akka.Streams TCP with `Tcp.NoAck`, inbound single-segment copy avoidance, multi-segment outbound frame encoding, Akka.IO TCP no-ack flush batching, and Remote-specific TCP frame decoding

| Num clients | Stream sample 1 | Stream sample 2 | Stream sample 3 | Stream median | DotNetty sample | Delta vs DotNetty |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 101369 | 108050 | 122474 | 108050 | 75586 | +43.0% |
| 5 | 617284 | 623831 | 541712 | 617284 | 358552 | +72.2% |
| 10 | 728333 | 740467 | 711238 | 728333 | 548697 | +32.7% |
| 15 | 774594 | 755288 | 774794 | 774594 | 574713 | +34.8% |
| 20 | 805640 | 769972 | 800962 | 800962 | 624415 | +28.3% |
| 25 | 784314 | 804247 | 774234 | 784314 | 649858 | +20.7% |
| 30 | 829647 | 815883 | 838106 | 829647 | 657247 | +26.2% |

Compared with the prior no-ack batching slice, this change is mostly neutral to modestly positive: 10, 15, 20, and 25 client medians improved, 1, 5, and 30 client medians moved down but stayed within the recent stream smoke range. The main value is removing generic framing and map stages from the stream transport while preserving the stream transport's end-to-end lead over DotNetty.

Validation after the Remote TCP framing stage change:

| Command | Result |
| --- | --- |
| `dotnet test "src/core/Akka.Remote.Tests/Akka.Remote.Tests.csproj" -c Release --filter "FullyQualifiedName~RemoteTcpFramingSpec|FullyQualifiedName~StreamTcpTransportInteropSpec"` | Passed: 6 |
| `dotnet test "src/core/Akka.Remote.Tests/Akka.Remote.Tests.csproj" -c Release --filter "FullyQualifiedName~MessageSerializerV2Spec|FullyQualifiedName~AkkaPduCodecWireFormatSpec|FullyQualifiedName~AkkaProtocolSpec|FullyQualifiedName~StreamTcpTransportInteropSpec|FullyQualifiedName~RemoteTcpFramingSpec"` | Passed: 30 |
| `dotnet test "src/core/Akka.Streams.Tests/Akka.Streams.Tests.csproj" -c Release --filter "FullyQualifiedName~TcpSpec"` | Passed: 21, skipped: 3 |
| `dotnet test "src/core/Akka.Tests/Akka.Tests.csproj" -c Release --filter "FullyQualifiedName~TcpConnectionBatchingSpec"` | Passed: 2 |
| `dotnet test "src/core/Akka.Remote.Tests/Akka.Remote.Tests.csproj" -c Release` | Passed: 382, skipped: 5 |
| `dotnet test "src/core/Akka.Cluster.Tests/Akka.Cluster.Tests.csproj" -c Release` | Passed: 364 |

Additional regression coverage:

- `RemoteTcpFraming_should_decode_multiple_big_endian_frames_from_one_chunk` verifies multiple payload frames, including empty payloads, from one upstream chunk.
- `RemoteTcpFraming_should_decode_little_endian_frames_split_across_chunks` verifies split header/payload reassembly and byte-order handling.
- `RemoteTcpFraming_should_reject_oversized_frames` verifies frame-size enforcement.
- `RemoteTcpFraming_should_reject_truncated_final_frame` verifies partial EOF failure behavior.

## Inbound Bridge Lock Fast Path Result

Implementation summary:

- `DeferredInboundBridge` now uses a volatile fast path once the `StreamAssociationHandle` has been installed.
- `StreamAssociationHandle.Notify` now uses a volatile fast path once the read listener has been registered.
- Pending inbound frames/events are still drained under the existing locks before the handle/listener is published, preserving ordering for startup races.
- The established inbound path avoids two uncontended locks per frame: bridge handle lookup and handle listener lookup.

RemotePingPong stream command:

```bash
dotnet run -c Release --project "src/benchmark/RemotePingPong/RemotePingPong.csproj" -- 1 stream
```

Environment summary:

- OS: Unix 6.8.0.117
- Processor count: 8
- Server GC: true
- Stream transport: Akka.Streams TCP with `Tcp.NoAck`, inbound single-segment copy avoidance, multi-segment outbound frame encoding, Akka.IO TCP no-ack flush batching, Remote-specific TCP frame decoding, and lock-free established inbound bridge notification

| Num clients | Stream sample 1 | Stream sample 2 | Stream sample 3 | Stream median | Prior stream median | Delta vs prior |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 106440 | 102093 | 104987 | 104987 | 108050 | -2.8% |
| 5 | 648930 | 622666 | 651466 | 648930 | 617284 | +5.1% |
| 10 | 796496 | 715052 | 773097 | 773097 | 728333 | +6.1% |
| 15 | 862317 | 753391 | 688706 | 753391 | 774594 | -2.7% |
| 20 | 907030 | 821187 | 701632 | 821187 | 800962 | +2.5% |
| 25 | 830979 | 849763 | 756659 | 830979 | 784314 | +5.9% |
| 30 | 806994 | 861946 | 789059 | 806994 | 829647 | -2.7% |

This is a small hot-path cleanup with mixed benchmark movement on a noisy VM. Treat the table as smoke data, not proof of a precise throughput win. The reason to keep the slice is structural: it removes synchronization from the steady-state inbound path while preserving behavior under focused and full test validation. A fresh DotNetty comparison sample in this pass was noisy at 1 client and logged transient disassociations, so it was not used as the gate for this micro-slice.

Validation after the inbound bridge lock fast path change:

| Command | Result |
| --- | --- |
| `dotnet test "src/core/Akka.Remote.Tests/Akka.Remote.Tests.csproj" -c Release --filter "FullyQualifiedName~RemoteTcpFramingSpec|FullyQualifiedName~StreamTcpTransportInteropSpec"` | Passed: 6 |
| `dotnet test "src/core/Akka.Remote.Tests/Akka.Remote.Tests.csproj" -c Release --filter "FullyQualifiedName~MessageSerializerV2Spec|FullyQualifiedName~AkkaPduCodecWireFormatSpec|FullyQualifiedName~AkkaProtocolSpec|FullyQualifiedName~StreamTcpTransportInteropSpec|FullyQualifiedName~RemoteTcpFramingSpec"` | Passed: 30 |
| `dotnet test "src/core/Akka.Streams.Tests/Akka.Streams.Tests.csproj" -c Release --filter "FullyQualifiedName~TcpSpec"` | Passed: 21, skipped: 3 |
| `dotnet test "src/core/Akka.Remote.Tests/Akka.Remote.Tests.csproj" -c Release` | Passed: 382, skipped: 5 |
| `dotnet test "src/core/Akka.Cluster.Tests/Akka.Cluster.Tests.csproj" -c Release` | Passed: 364 |
| `openspec validate remote-streams-protocol-pipeline --strict` | Valid |
| `git diff --check` | Passed |

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
