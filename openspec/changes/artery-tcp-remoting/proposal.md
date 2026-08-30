## Why

The previous plan to replace DotNetty with an Akka.Streams TCP transport under classic `AkkaProtocolTransport` does not address the real high-throughput remoting problem. Classic remoting is built around `EndpointWriter`, `EndpointReader`, `AkkaPduCodec`, protobuf envelopes, `ByteString`, a single logical stream, and actor-mailbox buffering. Retrofitting that path still leaves the core architecture allocation-heavy and unable to model Artery features cleanly.

Akka.NET 1.6 should add an Artery-style TCP remoting stack beside classic remoting. Classic remoting remains the compatibility path. Artery becomes the high-throughput path using the validated `SerializerV2` payload contract.

## What Changes

- Add `ArteryRemoting : RemoteTransport`, selected by configuration.
- Use `Akka.Streams.IO.Tcp` (`Tcp().Bind` / `Tcp().OutgoingConnection`) as the Artery TCP transport substrate — canonical Artery (verified against Pekko), gated on an early materializer-throughput validation against the 680K DotNetty baseline (see design Decision 2).
- Add Artery TCP connection framing: `AKKA` magic + stream id connection header, then 4-byte little-endian frame lengths.
- Add binary Artery envelope codec using `SerializerV2` payload serialization.
- Add association registry and UID-scoped association state.
- Add handshake with address and UID.
- Add control stream for handshake, liveness, quarantine, and system-message ACK/NACK.
- Add ordinary user message stream initially.
- Add reliable system-message delivery independent from user traffic.
- Keep classic remoting intact.

### What Does Not Change

- Classic remoting remains available and wire-compatible with its existing classic protocol.
- Artery does not need to be wire-compatible with classic remoting.
- QUIC is deferred to Akka.NET 1.7.
- TLS is deferred until plaintext Artery TCP validates the protocol, association, and performance model.
- Akka.Streams TCP remains a user-facing API. (It is ALSO the Artery TCP substrate — see design Decision 2 — which is a change, tracked under "What Changes.")

## Capabilities

### New Capabilities

- `artery-tcp-remoting`: Artery-style TCP remoting path with V2 payload serialization, association state, control stream, reliable system messages, and eventual lane/compression support.

## Impact

- **Akka.Remote**: new Artery namespace/components beside classic remoting.
- **Configuration**: new config switch selects classic or Artery remoting.
- **Serialization**: uses the validated `SerializerV2` payload contract.
- **Benchmarks**: RemotePingPong and envelope microbenchmarks target Artery TCP.
- **Compatibility**: classic remoting remains the compatibility path; Artery is a new protocol path.
