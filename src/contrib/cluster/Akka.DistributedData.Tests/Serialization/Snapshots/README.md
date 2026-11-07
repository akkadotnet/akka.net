# Replicator protocol fixtures

These immutable fixtures cover all eighteen legacy manifest tokens (A–R), optional fields,
consistency variants, pruning/delta metadata, empty messages, and compressed gossip.

The legacy fixtures were captured on Linux x64 with SDK 10.0.101 before any legacy serializer
edit, from upstream `dev` at `2d6a067972bfc7c49e5e4b898c542861eae9f6d9`:

- `ReplicatorMessageSerializer.cs`: blob `07af241b64cccceb7563efa513ea4ad1d3a66baa`
- Nested `ReplicatedDataSerializer.cs`: blob `fa026096a77483a9df68cd71b707ac6ca9ac213b`

Both source files remain unchanged by this port. Expected fixtures are read from files; no test
regenerates or accepts them automatically. A missing or changed fixture fails the shared port kit.
The corpus gives string-key protocol dictionaries a deterministic test-only hash comparer:
the unchanged legacy writer enumerates hash buckets, so process-random string hashes would
otherwise make Status, Gossip and DeltaPropagation writer snapshots drift across cold runs.
Gossip fixtures include the gzip container, so include Windows/Linux checks when reviewing
runtime or compression-library upgrades instead of silently replacing their expected bytes.

The V2 wrapper uses the source-generated codec and retains nested CRDTs as id-11 serializer
boundaries. Shared address fragments have these explicit field contracts:

| Fragment | Field ids |
|---|---|
| Address | 1 = hostname, 2 = port |
| UniqueAddress | 1 = Address, 2 = signed Int64 UID |
| VersionVector entry | 1 = UniqueAddress, 2 = version |

Protocol and actor-system name are reconstructed from the receiving system, as the legacy
format does. Arrays encoding wrapper dictionaries, pruning nodes, seen addresses and version
entries are sorted with ordinal hostname/key ordering and numeric port/UID tie-breakers before
encoding. Nested serializers retain responsibility for their own byte determinism.

This is a per-assembly generated fragment contract; a CRDT V2 port in the same assembly can reuse
it. A Cluster port may mirror it without introducing a dependency from Cluster to DistributedData.
No cross-assembly public schema or runtime reflection registry is introduced.

The dynamic-type-loading-off gate round-trips every case on the JIT with concrete GCounter and
GCounterKey payloads. It does not prove generic CRDT Native AOT compatibility: legacy id 11 still
uses Type.GetType, MakeGenericType and MakeGenericMethod for generic keys, sets and dictionaries
(`ReplicatedDataSerializer.cs`). The separate CRDT port/native audit must resolve that boundary.

Rollout keeps `akka.actor.serialization-v2` off by default. Both ids 12 and 52 are always registered;
upgrade all readers, including offline durable readers, before enabling V2 writes. An explicit
binding of `Akka.DistributedData.IReplicatorMessage, Akka.DistributedData` to `akka-data-replication`
pins the whole protocol to its legacy writer. Turning the switch off affects future writes only.
The LMDB adapter independently retains its existing headerless legacy or custom durable format;
neither a new record header nor a storage migration is introduced by this protocol port.

Replicator SHA1 digests also retain the legacy envelope format when network bindings select id 52.
Opposing writers must hash equal logical states identically, or repeated differences can starve new
keys in bounded gossip batches. This currently depends on nested CRDT id 11 staying unchanged.
The future id-51 CRDT port (#8679) must preserve a canonical nested digest format across bindings;
pinning only the outer envelope serializer to id 12 will not establish that invariant once id 51
can be selected for its nested payload. Custom legacy aliases and explicit bindings remain intact.
The same nested-format audit applies to headerless LMDB storage: its legacy outer envelope
delegates CRDT serialization too. The id-51 port must prove off/on/off recovery against existing
stores without silently replacing nested id-11 bytes before that new writer is enabled.
