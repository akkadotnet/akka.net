# Stream TCP Pipeline Spike

## Scope

The first stream transport spike adds an opt-in TCP transport class backed by Akka.Streams TCP and the existing Akka.IO.Tcp substrate:

- Transport class: `Akka.Remote.Transport.Streams.TcpStreamTransport, Akka.Remote`
- Opt-in shape: override `akka.remote.dot-netty.tcp.transport-class` while keeping `akka.remote.enabled-transports = ["akka.remote.dot-netty.tcp"]`
- Public scheme remains `akka.tcp` because the raw transport scheme remains `tcp` and the existing `AkkaProtocolTransport` wrapper still augments it.

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

## Deferred Work

The spike does not yet remove the inbound `ProtocolStateActor` mailbox hop. The future BidiFlow-style protocol replacement should handle the protocol events documented in `protocol-state-machine-map.md` and present the same `AkkaProtocolHandle` / `InboundAssociation` / `InboundPayload` / `Disassociated` behavior to existing remoting actors.

The spike also does not yet integrate the sequence/writer PDU codec into stream framing. Frames are currently bridged through `ByteString` at the classic transport boundary so the old protocol actor can remain authoritative.
