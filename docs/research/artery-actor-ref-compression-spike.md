# Actor-ref compression spike

Experimental and disabled by default. This is a measurement vehicle for simplifying #8343,
not a production replacement. It is stacked on the allocation work through #8819.

## Question

Can ordinary Artery sender/recipient metadata avoid per-message path formatting, UTF-8
path decoding and positive actor-ref resolution without the original string-keyed table,
frequency-sketch/heavy-hitter pair, custom control formatters, and activation/retry lookup?

Payload serialization is unchanged. This applies to envelope sender/recipient refs for any
payload serializer. It does not compress actor refs embedded inside serialized messages,
serializer IDs, or manifests. Control and large-message streams retain literal metadata.

## Mechanism

- One association-incarnation-owned state is shared by connections and outbound lanes.
- One bounded counter observes both refs every 64 ordinary messages. Capacity is four times
  the table maximum, capped at 65535. Cold advertisement sorts candidates and clears counts.
- Receiver publishes an immutable decode table before advertising its ordered paths.
  Lookup is pure. ACK or successfully decoded compressed data confirms publication and
  stops retries. One pending table retries each interval. Confirmed snapshots retain three
  older tables; a pending publication temporarily retains four older tables.
- Advertisements use source-generated native `byte` and `string[]` fields. No custom
  formatter, table wrapper, manifest compression, or separate heavy-hitter structure.
- Sender resolves advertised refs once, then looks up actual `IActorRef` values. Actor-ref
  equality includes actor UID. Misses format the original literal path.
- Receiver caches only positive refs with matching explicit actor UID; unresolved and
  temporary refs keep literal resolution/retry behavior. A one-reference metadata slot
  holds either a literal string or a table-owned cached ref and its original wire path.
- Existing recipient lane hashing still uses the original wire path, preserving lane choice.
  Application payload bytes, serializer ID and manifest stay unchanged.
- Stale/conflicting advertisements and stale ACKs are rejected. Installing outbound tables
  is atomic and monotonic. Unknown table/index drops the ordinary message.

```mermaid
sequenceDiagram
  participant A as Sender A
  participant B as Receiver B
  A->>B: ordinary literal sender/recipient
  B->>B: sample refs and publish decode table
  B->>A: ARAdv(B uid, A uid, version, paths[])
  A->>A: resolve once; install ref-to-index map
  A->>B: ARAck(B's table version)
  A->>B: same payload; compressed sender/recipient tags
  B->>B: cached positive refs; ordinary Tell
```

## Deliberate limits

**Do not enable this in production.** Association ownership alone does not fence already
encoded frames against receiver restart. The envelope identifies the sender incarnation,
not the intended receiver incarnation. Two receiver lifetimes can publish version 1/index 0
for different actors while an old sender frame still exists. A witness test demonstrates
this ambiguity. A receiver epoch or a proven connection/buffer fence needs a separate design
and on-wire restart/replay tests before shipping.

The spike never wraps generations: after version 127 it keeps its existing table and stops
publishing replacements. This avoids pretending that a stale-frame bound has been proved.
It also means adaptation eventually stops. The retained-table count is bounded, not a proof
that every arbitrarily delayed frame remains decodable.

Mixed-peer tests use the same binary with the feature disabled on one side. Compatibility
with binaries that do not recognize the new control manifests is not established. No
capability negotiation or rolling-upgrade guarantee is supplied here. Sampling, retry
interval, high-cardinality admission, and cache lifecycle need broader workload evaluation.

## Reproduce

Configuration (both peers):

```hocon
akka.remote.artery.advanced.ref-compression-spike {
  enabled = on
  maximum = 256
  interval = 100ms
}
```

Run existing `ArterySinglePassEncodeBenchmarks` with filter `*ActorRefs*`. Paired encode arms
reuse the same payload, serializer, actual refs and envelope. Cold formats literals; warm
uses a setup-built table. Paired decode arms parse the actual codec frame and either resolve
literal paths or retrieve cached refs. Table construction/learning and envelope allocation
are outside those microbenchmarks. This is warm-path isolation, not an end-to-end result.

RemotePingPong `run --artery --ref-compression-spike` enables both loopback systems. The
harness requires compressed-frame observations from both systems; an enabled run cannot
silently benchmark all literals. Its timer includes table learning and advertisement.
Compare with the same binary and identical arguments without that flag. Never run two
RemotePingPong processes together or overlap them with our builds/tests/BDN runs.

Focused correctness: `dotnet test src/core/Akka.Remote.Tests -c Release --filter FullyQualifiedName~ActorRefCompressionSpikeSpec`.
Results and the generated BenchmarkDotNet Markdown table belong directly in the draft PR
body after execution. Retain raw logs, source/binary hashes and exact run commands with
benchmark artifacts. Shared-host timings are directional; allocation counts provide the
stronger initial signal.
