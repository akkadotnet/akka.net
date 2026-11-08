## Why

Artery's TCP/TLS stack provides a shared protocol and an encrypted baseline for evaluating QUIC.
An experimental adapter can test independent ordinary streams while keeping actor routing,
serialization, handshakes, reliable system messages and quarantine in the existing pipeline.
Throughput, latency and resource benefits remain hypotheses to validate.

This develops [the QUIC epic](https://github.com/akkadotnet/akka.net/issues/7466) without a release,
production-support or default-transport commitment. It does not implement Aeron wire compatibility
or HTTP/3.

## What Changes

- Add an explicitly selected `System.Net.Quic` byte adapter under Artery.
- Use one connection per outbound peer direction, with separate control, ordinary and optional
  large-message streams.
- Support independently configured ordinary-stream counts and processing lanes, with fixed
  lane-to-stream routing and bounded admission.
- Reuse Akka.IO certificate loading and peer policies through `ArteryTlsSettings`/`ArteryTlsSetup`.
- Keep telemetry/compression extension points and all Artery protocol state shared with TCP.
- Extend RemotePingPong with explicit transport selection, encrypted credentials, realistic-payload
  comparisons, lane/stream sweeps and optional diagnostics.
- Add native transport and ordering tests. Keep broader failure/resource and performance gates open.

## Capabilities

### New Capabilities

- `artery-quic-prototype`: Experimental QUIC transport and comparison tooling over the shared Artery protocol.

### Modified Capabilities

Existing Artery TCP wire bytes, public APIs and default transport selection remain unchanged.

## Impact

- **Akka.Remote:** internal native-I/O adapter, stream admission, cancellation and lifecycle handling.
- **Tests:** shared TLS fixtures plus native QUIC ordering, rejection, reset and ownership checks.
- **Benchmark source:** existing RemotePingPong workload and a repeatable comparison driver.
- **Deployment:** native QUIC support, UDP reachability, TLS 1.3 credentials and explicit selection on both peers.
- **Compatibility:** no automatic fallback or promise of mixed TCP/QUIC rolling upgrades.

This change contains source and specifications. Benchmark results, machine records and local
research artifacts are maintained separately.
