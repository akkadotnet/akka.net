# Sharding wire fixtures

The original 52 legacy id 13 fixtures were captured from untouched upstream
`2d6a067972bfc7c49e5e4b898c542861eae9f6d9` before the port, in commit `b8c775b74`.
Fifty writable cases cover all 41 current forms, including empty collections and migration
flags. The two additional CB/CD records use the original single-entity protobuf schemas and
exercise their historical bulk-event reader aliases. Legacy bytes and production source stay
unchanged. The additional null-envelope legacy fixture was captured from the same unchanged
serializer in `d8890a30e`. Capture used the same values in `ShardingWireCases` and `HexDumpFormatter`.

Goldens use fixed full remote actor paths (including UID 42), fixed addresses, and empty or
single-entry collections to avoid random legacy hash iteration order. Multi-entry coordinator
maps, reconstructed per-region shard lists, nested statistics/entity sets, runtime flag
omission, ambient transport actor paths and registered user payload serializers are checked
separately. V2 collections have canonical ordering. Its timeout preserves TimeSpan ticks.

The existing coordinator format persists shard-to-region allocations and region identities,
then reconstructs each region's shard list. RememberEntities is not persisted: recovery
reapplies that setting. The V2 format preserves this contract. Native actor-reference fields
retain the owning actor system and ambient transport context. Empty references are resolved through
the actual receiver provider, preserving local DeadLetters versus remote NoSender behavior.
Opaque envelope payloads retain their own registered serializer id and manifest, including null.

ShardingV2PersistenceSpec uses the real EventSourcedRememberEntitiesShardStore with a byte-only
file journal and the actual LocalSnapshotStore across whole ActorSystem replacement, off/on/off.
It checks event payload ids/manifests and snapshot payload ids, retains original bytes, and
forces snapshot recovery by tombstoning replayable journal events. LocalSnapshotStore's outer
protobuf wrapper stays id 8; these checks do not assume a headerless outer file can change format.
The existing multi-node persistent and DData remember-entity flows exercise real failover;
their shared in-memory backend alone does not establish retained serializer-id/byte durability.
DData new-cluster remember recovery remains excluded by its existing issue #4262 condition.

Both id 13 and id 53 are readable on this candidate. Install the new reader on all peers before
enabling writers. Toggle-off or an explicit IClusterShardingSerializable pin restores id 13
writes, but stored id 53 journal/snapshot payloads still need the new reader. An older-binary
rollback needs pre-change backups or a verified storage migration. Codec-only native probes
do not establish native Sharding bootstrap; generic DData native support remains a prerequisite.
