---
uid: built-in-serialization-v2
title: Enabling Built-in Serialization V2
---

# Enabling Built-in Serialization V2

Akka.NET v1.6 can register a generated MessagePack serializer alongside a subsystem's legacy
serializer. The `akka.actor.serialization-v2` setting selects which one writes messages.
It defaults to `off`. Reads use the stored serializer id, so a version containing both
serializers reads either format with the setting on or off.

This setting applies to Akka.NET's built-in serializers. Your application messages keep their
own serializers, including when a built-in message wraps an application payload. To generate
serializers for your own messages, see [Source-Generated MessagePack Serialization](xref:source-generated-serialization).

## Check Reader Versions Before Enabling

Every process that can receive or recover a format must contain its reader before any process
writes that format. Include cluster members, standalone remote clients, recovery tools, and
temporarily offline consumers. A node running an earlier v1.6 beta might lack a later port;
checking only the major/minor version is insufficient.

| Subsystem | Legacy alias / id | V2 alias / id | First release containing the V2 reader | Native AOT boundary |
| --- | --- | --- | --- | --- |
| Reliable Delivery, including durable producer queues | `reliable-delivery` / 36 | `reliable-delivery-v2` / 76 | Unreleased; [#8409](https://github.com/akkadotnet/akka.net/pull/8409) | Generic Delivery messages throw `SerializationException` when `Akka.DynamicTypeLoading` is off, on the JIT as well as Native AOT. |
| Cluster protocol and cluster router pools | `akka-cluster` / 5 | `akka-cluster-v2` / 45 | Unreleased; [#8924](https://github.com/akkadotnet/akka.net/pull/8924) | Local codec probes cover current and historical heartbeat forms. Native cluster bootstrap and transport remain separate capabilities. |
| Cluster Singleton hand-over | `akka-singleton` / 14 | `akka-singleton-v2` / 54 | Unreleased; [#8915](https://github.com/akkadotnet/akka.net/pull/8915) | Local native codec canary passes all four manifests. Cluster startup and ownership hand-over remain separate capabilities. |
| Cluster Metrics | `akka-cluster-metrics` / 10 | `akka-cluster-metrics-v2` / 50 | Unreleased; [#8917](https://github.com/akkadotnet/akka.net/pull/8917) | A standalone native codec probe passes all five implemented manifests. Tracked CI coverage and complete Metrics subsystem AOT support remain pending. |
| CRDT states, deltas and keys | `akka-replicated-data` / 11 | `akka-replicated-data-v2` / 51 | Unreleased; [#8937](https://github.com/akkadotnet/akka.net/pull/8937) | Known primitive/ref forms have static factories. Generic maps, arbitrary type arguments and some historical generic readers remain separate native boundaries. |
| Replicator protocol | `akka-data-replication` / 12 | `akka-data-replication-v2` / 52 | Unreleased; [#8918](https://github.com/akkadotnet/akka.net/pull/8918) | Network CRDTs use id 11 or 51 according to the deployed port and bindings. Canonical legacy nesting stays on the active legacy alias; generic native reconstruction remains bounded. |
| Distributed PubSub | `akka-pubsub` / 9 | `akka-pubsub-v2` / 49 | Unreleased; [#8919](https://github.com/akkadotnet/akka.net/pull/8919) | A standalone native codec probe passes all six manifests with supported payloads. User payloads need their own native-compatible serializers. |
| Cluster Client / Receptionist | `akka-cluster-client` / 15 | `akka-cluster-client-v2` / 55 | Unreleased; [#8922](https://github.com/akkadotnet/akka.net/pull/8922) | Codec probes pass all eleven manifests. Complete client delivery additionally depends on PubSub and runtime activation. |
| Cluster Sharding | `akka-sharding` / 13 | `akka-sharding-v2` / 53 | Unreleased; [#8930](https://github.com/akkadotnet/akka.net/pull/8930) | Generic CRDTs and native cluster bootstrap remain separate. Verify composed durable and digest behavior before enabling. |
| Stream-ref protocol | `akka-stream-ref` / 30 | `akka-stream-ref-v2` / 70 | Unreleased; [#8929](https://github.com/akkadotnet/akka.net/pull/8929) | Only five protocol messages migrate. Bare generic refs keep id 30 and their dynamic-loading guards; typed fields remain [#8673](https://github.com/akkadotnet/akka.net/issues/8673). |
| Remote deployment | `daemon-create` / 3 | `daemon-create-v2` / 43 | Unreleased; [#8931](https://github.com/akkadotnet/akka.net/pull/8931) | Native remote deployment is explicitly unsupported. V2 rejects writes and reads when dynamic type loading is disabled. |
| System messages | `akka-system-msg` / 22 | `akka-system-msg-v2` / 62 | Unreleased; [#8932](https://github.com/akkadotnet/akka.net/pull/8932) | Known generated and historical protobuf exception reads have native proofs. Historical JSON fields need a compatible application reader; native actor activation and transport remain separate. |
| Miscellaneous protocol messages | `akka-misc` / 16 | `akka-misc-v2` / 56 | Unreleased; [#8934](https://github.com/akkadotnet/akka.net/pull/8934) | Known generated exception frames use static factories. Historical exception and arbitrary payload readers have separate native limits; all 21 bindings must be pinned for legacy writes. |
| Actor-selection containers | `akka-containers` / 6 | `akka-containers-v2` / 46 | Unreleased; [#8923](https://github.com/akkadotnet/akka.net/pull/8923) | The wrapper does not make application payloads or the whole remote subsystem native-compatible. |

The table identifies reader availability, not a performance or Native AOT recommendation.

Identifiers 0 through 99 are reserved for Akka.NET, including the 40–79 block used by these
V2 ports. Audit custom serializer IDs before upgrading: new readers are registered even
when V2 writing is off. Do not renumber a serializer that has already written durable data
without a migration that preserves access to the original records.

## Performance Readiness

The draft ports require maintainer review of the complete benchmark results before this
guide recommends activation. These measurements use the JIT and compare serializer
operations; native codec verification is separate, and application latency, throughput and
capacity need workload-specific checks. Results and host/runtime
limits are retained in the linked PRs, including regressions.

| Completed full benchmark | Observed trade-offs |
| --- | --- |
| [Reliable Delivery #8409](https://github.com/akkadotnet/akka.net/pull/8409) | Buffer writes improve by 20–71%. The small Request byte-array write takes 118% longer and allocates 61% more; its read takes 42% longer. |
| [Cluster protocol #8924](https://github.com/akkadotnet/akka.net/pull/8924) | Heartbeat and Join buffer writes improve by 34% and 46%. Gossip byte-array time/allocation grows by 90%/119%, while its buffer path is roughly unchanged. RouterPool byte-array time/allocation grows by 26%/74%. |
| [Singleton #8915](https://github.com/akkadotnet/akka.net/pull/8915) | All four fieldless controls are slower. V2 buffer writes take about 12–13 ns versus 2–3 ns; byte-array writes allocate 128 B and reads 24 B versus no allocation. Wire size grows from zero to one byte. |
| [Metrics #8917](https://github.com/akkadotnet/akka.net/pull/8917) | Gossip buffer writes improve by 78% time and 58% allocation. Default-pool byte-array writes are about 29 times slower; gossip wire size grows from 377 to 476 bytes. |
| [CRDT states and deltas #8937](https://github.com/akkadotnet/akka.net/pull/8937) | Counter and map buffer writes improve by 23–34% time, with mixed allocation results. ObservedSet buffer allocation grows by 117%; reads take 30% longer and allocate 21% more. All byte-array writes allocate 26–122% more. Generic map measurements require the JIT. |
| [Replicator #8918](https://github.com/akkadotnet/akka.net/pull/8918) | Gossip buffer allocation falls by 33% with similar timing. Metadata buffer allocation grows by 39%; Get reads take 43% longer while allocating 74% less. WriteAck buffer writes grow from 2.71 to 22.18 ns and allocate 56 B. |
| [Sharding #8930](https://github.com/akkadotnet/akka.net/pull/8930) | Buffer writes improve by 20–47% time and 29–77% allocation. All byte-array writes take 12–53% longer and allocate 28–81% more. All reads allocate less, with mixed timing results. |
| [PubSub #8919](https://github.com/akkadotnet/akka.net/pull/8919) | Send, Publish and Status buffer writes improve by 41–51%. Delta tombstone byte-array time/allocation grows by 97%/66% and reads take 38% longer. |
| [Cluster Client #8922](https://github.com/akkadotnet/akka.net/pull/8922) | Contacts, Send and Publish buffer writes improve by 32–46%. Heartbeat buffer writes grow from 2.65 to 12.19 ns and allocate 32 B; Contacts byte-array time/allocation grows by 70%/84%. |
| [Actor-selection containers #8923](https://github.com/akkadotnet/akka.net/pull/8923) | Buffer writes improve by 8–36% time and 60–77% allocation. EmptyPath, NestedSelection and PatternAndParent reads take 12–22% longer; wire sizes grow by 3–13 bytes. |
| [System messages #8932](https://github.com/akkadotnet/akka.net/pull/8932) | Watch, DeathWatch and Failed buffer writes improve by 16–39% time, while Failed buffer allocation grows by 16%. Suspend buffer writes grow from 2.58 to 14.99 ns and allocate 56 B. Failed wire size grows from 228 to 360 bytes with richer exception state. |
| [Miscellaneous messages #8934](https://github.com/akkadotnet/akka.net/pull/8934) | Payload buffer writes improve by 18–38% time, while byte-array allocation grows by 15–58%. Heartbeat buffer writes grow from 2.84 to 16.83 ns and allocate 56 B. Failure wire size grows from 177 to 314 bytes while preserving richer exception state. |
| [Remote deployment #8931](https://github.com/akkadotnet/akka.net/pull/8931) | Buffer writes improve by 24–59% time and 50–98% allocation. Default byte-array time/allocation grows by 15%/82%; Default and Deploy reads take 17% and 11% longer. These are JIT measurements; native deployment stays unsupported. |
| [Streams #8929](https://github.com/akkadotnet/akka.net/pull/8929) | Buffer writes improve by 31–44% time and 57–82% allocation. Demand byte-array time/allocation grows by 54%/150%; Demand and Next reads take 39% and 11% longer. |
| [Persistence messages #8927](https://github.com/akkadotnet/akka.net/pull/8927) | Buffer writes improve by 2–44% time and 19–72% allocation; reads improve by 6–12% time and 12–60% allocation. Byte-array allocation is unchanged; StateChange byte-array time grows from 22.85 to 24.83 ns. |
| [Persistence snapshots #8925](https://github.com/akkadotnet/akka.net/pull/8925) | Buffer writes improve by 19–29% time and 35–50% allocation; byte-array allocation is unchanged. String reads improve by 12–15%; Bytes and Null reads take 4–5% longer, while all reads allocate less. |

All 16 groups have completed their full 24-case measurements. Maintainer performance
acceptance remains pending. Keep the global switch off, or use the complete subsystem
pins below, until the deployed combination has been reviewed.
The same-format Persistence implementations at ids 7 and 8 apply independently of this switch.
Consult the release's validation results before enabling a port. Other built-in serializers
keep their existing behavior until a V2 replacement is shipped. Native `SerializerV2`
implementations that retain their original bytes and id, such as Primitive (17), are unaffected
by this setting.

## Upgrade Readers, Then Change Writers

1. Keep `akka.actor.serialization-v2 = off` while upgrading all participating readers to a
   release containing the ports you will use. Keep the Akka.NET and Akka.Hosting package
   versions aligned.
2. Back up durable state using your storage provider's normal procedure. Test recovery of
   representative production data with the upgraded application.
3. Enable V2 in a staged environment containing the same modules and custom bindings as
   production. Exercise traffic in both directions and restart persistent actors.
4. After reviewing compatibility, recovery, and performance results, enable it for the
   intended deployment. The framework does not check other processes' reader versions.

Set the HOCON key:

```hocon
akka.actor.serialization-v2 = on
```

Or use the existing Akka.Hosting builder method:

```csharp
services.AddAkka("my-system", (builder, _) =>
{
    builder.WithSerializationV2();
});
```

With Akka.Hosting, `WithSerializationV2(false)` writes the off setting. The builder prepends
this key, and a later call wins over an earlier call.

At startup, the `Serialization V2 is on` information message lists the module aliases whose
bindings changed. Check the listed takeovers and any explicit binding overrides. If no
deployed module has a V2 replacement, the message says that every binding stayed on legacy.
Do not infer a wire-format change merely from the setting being on.

## Keep a Subsystem on Its Legacy Writer

Explicit application bindings and `SerializationSetup` bindings take precedence over the
module defaults. For example, keep all Reliable Delivery messages on id 36 while enabling
other available ports:

```hocon
akka.actor {
    serialization-v2 = on
    serialization-bindings {
        "Akka.Delivery.Internal.IDeliverySerializable, Akka" = reliable-delivery
    }
}
```

The id 76 reader remains registered. Remove this override when you are ready to use its
writer. Pin an entire subsystem's binding set; a module with several bindings needs every
one pinned, not just a convenient example message type.

For other ports, the complete legacy binding sets are:

```hocon
akka.actor.serialization-bindings {
    # Singleton hand-over
    "Akka.Cluster.Tools.Singleton.IClusterSingletonMessage, Akka.Cluster.Tools" = akka-singleton

    # Distributed PubSub: both bindings are needed.
    "Akka.Cluster.Tools.PublishSubscribe.IDistributedPubSubMessage, Akka.Cluster.Tools" = akka-pubsub
    "Akka.Cluster.Tools.PublishSubscribe.Internal.SendToOneSubscriber, Akka.Cluster.Tools" = akka-pubsub

    # Cluster protocol and router pools: both bindings are needed.
    "Akka.Cluster.IClusterMessage, Akka.Cluster" = akka-cluster
    "Akka.Cluster.Routing.ClusterRouterPool, Akka.Cluster" = akka-cluster

    # Sharding, including remembered entities and coordinator snapshots
    "Akka.Cluster.Sharding.IClusterShardingSerializable, Akka.Cluster.Sharding" = akka-sharding

    # System messages
    "Akka.Dispatch.SysMsg.SystemMessage, Akka" = akka-system-msg

    # Remote deployment (still requires dynamic type loading)
    "Akka.Remote.DaemonMsgCreate, Akka.Remote" = daemon-create

    # CRDT states, deltas and keys
    "Akka.DistributedData.IReplicatedDataSerialization, Akka.DistributedData" = akka-replicated-data

    # Replicator protocol
    "Akka.DistributedData.IReplicatorMessage, Akka.DistributedData" = akka-data-replication

    # Metrics: all five bindings are needed.
    "Akka.Cluster.Metrics.Serialization.MetricsGossipEnvelope, Akka.Cluster.Metrics" = akka-cluster-metrics
    "Akka.Cluster.Metrics.AdaptiveLoadBalancingPool, Akka.Cluster.Metrics" = akka-cluster-metrics
    "Akka.Cluster.Metrics.MixMetricsSelector, Akka.Cluster.Metrics" = akka-cluster-metrics
    "Akka.Cluster.Metrics.CpuMetricsSelector, Akka.Cluster.Metrics" = akka-cluster-metrics
    "Akka.Cluster.Metrics.MemoryMetricsSelector, Akka.Cluster.Metrics" = akka-cluster-metrics
}
```

Keep only the subsystem groups you intend to pin. Verify their effective writers with the
same module versions and application configuration you will deploy.

Enabling the global setting also applies to new ports introduced by a later package upgrade.
Review that release's takeover table and retain explicit legacy bindings for any subsystem
you have not validated.

## Cluster Client Compatibility Setting

Cluster Client retains its existing `akka.cluster.client.use-legacy-serialization` setting.
After client settings are constructed, the effective bindings are:

| Global V2 setting | Client legacy setting | Receptionist messages | Client commands and ticks |
| --- | --- | --- | --- |
| Off | On (default) | 15 | Existing JSON path |
| On | On (default) | 55 | Existing JSON path |
| Off | Off | 15 | 15 |
| On | Off | 55 | 55 |

To use the generated codec for the complete client protocol, configure both existing keys:

```hocon
akka.actor.serialization-v2 = on
akka.cluster.client.use-legacy-serialization = off
```

The startup takeover log precedes the client setting's removal of its protocol binding.
Check effective writers after client initialization. To keep built-in legacy id 15 while
other modules use V2, set the client legacy setting to off and pin both interfaces:

```hocon
akka.cluster.client.use-legacy-serialization = off
akka.actor.serialization-bindings {
    "Akka.Cluster.Tools.Client.IClusterClientMessage, Akka.Cluster.Tools" = akka-cluster-client
    "Akka.Cluster.Tools.Client.IClusterClientProtocolMessage, Akka.Cluster.Tools" = akka-cluster-client
}
```

## Actor-Selection Format Boundary

Legacy ids 3 and 6 retain their empty manifests. New remote-deployment id 43 also uses token `a`; its stricter native guard does not enable native actor construction.

Legacy id 6 retains its empty manifest. New id 46 uses nonempty token `a`; this exception to
manifest parity keeps native manifest validation intact. Its release also includes Artery's
parser for both outer formats, so upgrade every receiver before sending id 46.

The V2 wrapper retains `WildCardFanOut`, which legacy omits. Recursive path selectors remain
unsupported and are rejected before output is written. To retain legacy container writes:

```hocon
akka.actor.serialization-bindings {
    "Akka.Actor.ActorSelectionMessage, Akka" = akka-containers
}
```

## Roll Back Writers While Retaining Readers

Set `akka.actor.serialization-v2 = off`, or call `WithSerializationV2(false)`, and restart
the affected actor systems. Future default writes return to the legacy serializers. Explicit
user bindings still take precedence, including an explicit binding to a V2 alias.

Previously stored V2 records keep their stored serializer ids, including Delivery id 76. Keep binaries containing
that reader for all future recoveries. Turning the setting off does not rewrite those records
or make them readable by a binary without the V2 serializer. A backup taken before enablement
does not include subsequent writes; restoring it requires a separate application data-recovery
decision.

The DistributedData LMDB store has no per-record serializer id. The Replicator port retains the
legacy built-in storage format while changing network bindings, and preserves existing custom
durable serializer bindings and legacy-alias overrides. Test the same custom serializer setup
during recovery; switching the global setting does not convert LMDB values. This protection
is tracked in [#8716](https://github.com/akkadotnet/akka.net/issues/8716).

Nested formats matter too. A sharding coordinator snapshot inside a CRDT can otherwise change
LMDB bytes and Replicator digests even when the outer durable serializer stays legacy.
The composed migration requires the canonical-payload policy in
[#8933](https://github.com/akkadotnet/akka.net/pull/8933): built-in replacements retain the
active legacy alias at that boundary, including explicit application alias overrides.
Network writer selection still follows the global setting and application bindings.
Verify the complete module set and saved state together before enabling.

## Stream-Ref Protocol Boundary

The stream-ref port moves only `IStreamRefsProtocol`. To pin those messages:

```hocon
akka.actor.serialization-bindings {
    "Akka.Streams.Implementation.StreamRef.IStreamRefsProtocol, Akka.Streams" = akka-stream-ref
}
```

Bare `SourceRefImpl<T>` and `SinkRefImpl<T>` keep their legacy binding. A native codec probe
for the five protocol messages does not establish native remote stream materialization.

## Miscellaneous Protocol Binding Set

Miscellaneous protocol messages (`akka-misc`, id 16 → `akka-misc-v2`, id 56) retain all 22 legacy manifest tokens. Both readers are registered; default writes remain legacy. The first reader release is **unreleased**. The generated Status.Failure format preserves original exception HResult, HelpLink and Data, which legacy id 16 discards. LocalScope retains its explicit codec but still has no default binding.

Enable the global switch only after every reader has upgraded, including any process that stores or later decodes these protocol bytes. User payload serializers inside Identify, ActorIdentity, Status, resizers and remote router pools remain separate. Switching back off changes future writes; it does not let an older binary read existing id 56 bytes.

To retain this entire protocol's legacy writer while the global switch enables other completed ports, pin all of its existing bindings:

```hocon
akka.actor.serialization-bindings {
  "Akka.Actor.Identify, Akka" = akka-misc
  "Akka.Actor.ActorIdentity, Akka" = akka-misc
  "Akka.Actor.IActorRef, Akka" = akka-misc
  "Akka.Actor.PoisonPill, Akka" = akka-misc
  "Akka.Actor.IntentionalRestart, Akka" = akka-misc
  "Akka.Actor.Kill, Akka" = akka-misc
  "Akka.Actor.Status+Failure, Akka" = akka-misc
  "Akka.Actor.Status+Success, Akka" = akka-misc
  "Akka.Actor.RemoteScope, Akka" = akka-misc
  "Akka.Routing.FromConfig, Akka" = akka-misc
  "Akka.Routing.DefaultResizer, Akka" = akka-misc
  "Akka.Routing.RoundRobinPool, Akka" = akka-misc
  "Akka.Routing.BroadcastPool, Akka" = akka-misc
  "Akka.Routing.RandomPool, Akka" = akka-misc
  "Akka.Routing.ScatterGatherFirstCompletedPool, Akka" = akka-misc
  "Akka.Routing.TailChoppingPool, Akka" = akka-misc
  "Akka.Routing.ConsistentHashingPool, Akka" = akka-misc
  "Akka.Configuration.Config, Akka" = akka-misc
  "Akka.Remote.RemoteWatcher+Heartbeat, Akka.Remote" = akka-misc
  "Akka.Remote.RemoteWatcher+HeartbeatRsp, Akka.Remote" = akka-misc
  "Akka.Remote.Routing.RemoteRouterConfig, Akka.Remote" = akka-misc
}
```

A native codec proof is bounded by its retained source and receipts. Historical non-null exception reads require the finite native reader in [#8935](https://github.com/akkadotnet/akka.net/pull/8935). It reconstructs known exception types using the old format's state; JSON-backed historical fields still require a compatible application reader. Generated serializers and `akka.actor.serialization-v2` do not by themselves enable dynamic actor construction or full remoting Native AOT. Performance and broader transport gates must be cleared before recommending this port for production activation.

## Same-Format Native Persistence Envelopes

The native Persistence implementations retain protobuf bytes and ids: message envelopes use
id 7 ([#8927](https://github.com/akkadotnet/akka.net/pull/8927)) and snapshot envelopes use id 8
([#8925](https://github.com/akkadotnet/akka.net/pull/8925)). These are unreleased implementation
updates independent of the global MessagePack switch. They do not require stored records to be
rewritten. Both continue to dispatch application payloads through their registered serializers.

Code retrieving a built-in serializer should use `Serializer` or `SerializerV2` contracts;
the module returns new internal concrete types. The public legacy classes remain available,
and copied configuration naming those classes resolves through exact static aliases.
Generic `PersistentFSMSnapshot<T>` retains its JIT implementation and explicitly throws when
dynamic type loading is disabled, even after a JIT cache was warmed. This does not establish
native support for arbitrary persistence plugins or payloads.

## Native AOT Is a Separate Capability

`akka.actor.serialization-v2` selects built-in wire formats. `Akka.DynamicTypeLoading` controls
runtime type resolution and is normally disabled by the packaged build targets for trimmed
or Native AOT applications. Enabling Serialization V2 does not enable AOT, turn dynamic type
loading back on, or prove that a subsystem can reconstruct arbitrary generic messages.

The current Delivery V2 wrapper rejects generic sequenced messages, consumer registrations,
message-sent events, and queue state when dynamic type loading is off. It rejects writes as
well as reads, so an unsupported queue cannot persist a new V2 record and discover the same
limitation only during recovery. The non-generic control messages have no such reconstruction
requirement. This boundary is tested with the switch disabled on the JIT; it is not a claim of
native end-to-end Delivery support.

See [Native AOT and Trimming](xref:native-aot) for the capabilities verified by native canaries.
