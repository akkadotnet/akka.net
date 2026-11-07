# PubSub wire fixtures

The eleven `*.legacy.verified.txt` fixtures were captured from upstream
`2d6a067972bfc7c49e5e4b898c542861eae9f6d9`, before port implementation, in baseline
commit `b0f544697`. The temporary capture program used the production legacy serializer
and the shared `HexDumpFormatter` with the same `PubSubWireCases` source used by the spec.
It wrote id 9, manifests A–F. The legacy serializer source is unchanged by this port.

Goldens use fixed values, empty or single-entry maps, and one nullable actor-reference
tombstone. Dictionary iteration order is process-dependent; multi-owner registries and live
actor references are checked separately as domain state. The V2 fixtures are reviewed
MessagePack output at id 49, with unchanged manifests. User string payloads retain their
separate serializer id and manifest inside the envelope.

Run `DistributedPubSubMessagePackSerializerSpec` to compare both sets. Follow the shared
[V2 port guide](../../../../../core/Akka.Tests.Shared.Internals.Xunit3/V2Port/README.md)
when reviewing a new V2 format. Never accept replacement legacy bytes to repair a failure.

The mixed-binding check exchanges actual bytes between two in-process ActorSystems using
opposite global-toggle values. It proves current-version readers handle both ids; it does
not establish that earlier released binaries read id 49, or exercise a remote cluster.
All peers need the reader introduced by this port before a writer is enabled. Toggle-off
systems on that reader version can continue reading id 49. Returning to an older reader
requires draining in-flight id 49 traffic before downgrading; the toggle cannot rewrite bytes.
