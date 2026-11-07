# Cluster protocol wire fixtures

Legacy id 5 fixtures were captured from untouched upstream
`2d6a067972bfc7c49e5e4b898c542861eae9f6d9` before implementation, in baseline commit
`a6f0af898`. Eighteen modern cases cover all thirteen writable forms; two additional
fixtures retain the pre-1.4.19 heartbeat tokens. The legacy source and captured bytes stay
unchanged. The capture program linked the exact same `ClusterWireCases` source and shared
`HexDumpFormatter` used by these tests.

The rich fixed gossip has two members, one role/app-version, one observer/record, one clock
hash and one tombstone, avoiding random legacy hash collection iteration order. It includes
64-bit UIDs and a tombstone sharing a live member's network address with a different UID.
Separate assertions cover all six valid member states, every reachability state, multiple
roles/application versions, and observers retaining versions after their records disappear.
Member equality alone cannot establish these fields, and Reachability equality compares
collection instances; the shared port cases therefore supply complete domain assertions.

The reviewed id 45 fixtures keep every existing manifest token. The historical heartbeat
configuration still drops sequence/time to -1. Gossip remains uncompressed, uses canonical
address/role/application-version tables, and reconstructs Member.Ordering and VectorClock
hash/counter mappings. Envelope deadlines remain local-only. The old port-zero-to-null
address normalization is retained. Local router pools keep their actor-system serializer
envelope.

Both ids remain readable on the candidate. Mixed-binding tests and multi-process membership
checks establish opposing writer settings on these binaries; they do not make earlier
binaries understand id 45. All peers need the reader supplied by this port before enabling
writers. Toggle off, or explicitly pin both IClusterMessage and ClusterRouterPool, to
restore legacy writes; drain id 45 traffic before downgrading to an older reader.

Use the shared [V2 port guide](../../../Akka.Tests.Shared.Internals.Xunit3/V2Port/README.md)
when reviewing a new format. Never accept changed legacy bytes to repair a failure.
