# Cluster protocol native codec proof

This ordinary public consumer verifies the local id 5/45 codec boundary. It has its
own assembly identity and references only the product project. It uses no friend
assembly name, private protocol type references, reflection, assembly roots or
custom protocol registry. The executable fails unless it is actually native and
`Akka.DynamicTypeLoading` is false. Its local actor systems never start Cluster.

`FrozenFixtures.cs` copies all 20 reviewed protobuf/MessagePack snapshot pairs
from `Akka.Cluster.Tests/Serialization/Snapshots` at
`f98ccc3855e05569caa2eef6c4062ca07cd1a794`. Eighteen records use the current
heartbeat setting; the two historical heartbeat records use
`akka.cluster.use-legacy-heartbeat-message = on`. Every private message is created
through the public `Serialization.Deserialize(bytes, 5, manifest)` API. The public
fallback-alias lookup resolves the read-only `akka-cluster-v2` row using an
otherwise unbound consumer marker; it does not change protocol bindings.

For each frozen record the app checks global off, global on and global on with
both existing bindings pinned to `akka-cluster`:

```hocon
akka.actor.serialization-bindings {
  "Akka.Cluster.IClusterMessage, Akka.Cluster" = akka-cluster
  "Akka.Cluster.Routing.ClusterRouterPool, Akka.Cluster" = akka-cluster
}
```

All 60 cases require the exact frozen V2 writer bytes, registered id 45 reads,
direct reads, native `IBufferWriter<byte>` writes and two-segment reads. Restored
values must retain the exact runtime class and reproduce the complete frozen V2
bytes. Both reader IDs remain usable in every mode. All 60 actual writer
selections are checked. For the 48 non-heartbeat cases, the unchanged public
legacy writer also reproduces the complete original protobuf bytes for every
restored value. Selected writer bytes/manifests are checked wherever that writer
does not require Cluster startup.

The legacy heartbeat writer **and its Manifest method** call `Cluster.Get` to read
the heartbeat setting. This app does not call either on those messages. Instead,
the four heartbeat records check every protobuf and MessagePack field through
public `CodedInputStream`, `MessagePackReader` and `AddressFormatter`: protocol,
system, host, port, the complete 64-bit UID for responses, and sequence/time for
current messages. Historical frames contain no sequence/time. Public object
equality on both heartbeat domain classes additionally checks all those values,
including their restored historical `-1` sequence/time. This is an explicit old
heartbeat **write** limitation, not evidence for native Cluster startup.

Six separate custom nested-codec controls check both outer reader/writer IDs
under all three binding modes. They register a public consumer-owned pool codec
through `SerializationSetup`, require its callbacks and preserve the pool count,
dispatcher and complete Cluster pool settings. Unknown manifests must fail with
the existing per-format exception type (id 5: `ArgumentException`; id 45:
`SerializationException`), including a segmented native read. Every warning or
error logged by the actor systems fails the run.

Run from outside the checkout so its `global.json` does not select SDK 10.0.101.
The native evidence uses SDK 10.0.114 and runtime/compiler 10.0.14:

```bash
repo=/path/to/checkout
sdk=/path/to/dotnet-10.0.114
cd /tmp
PATH="$sdk:$PATH" DOTNET_ROOT="$sdk" "$sdk/dotnet" publish \
  "$repo/src/aot/Akka.Cluster.Protocol.AOT.App/Akka.Cluster.Protocol.AOT.App.csproj" \
  -c Release -r linux-x64 -o /tmp/cluster-public-native
/tmp/cluster-public-native/Akka.Cluster.Protocol.AOT.App
```

The project enables `PublishAot` when a runtime identifier is supplied. Do not
pass it as a global MSBuild property, which also applies it to the netstandard
generator project. `IlcTreatWarningsAsErrors=false` retains all ILC diagnostics;
it does not approve or suppress them. The retained publish log and warning
comparison document the remaining module warnings. No transport, native Cluster
bootstrap, generic ReliableDelivery support, all-assembly AOT readiness or
performance claim follows from this bounded codec proof.

The PR-validation pipeline also invokes `native-codec-canary.template.yaml` with
the exact completion marker above, a Cluster/Remote/runtime/app source scope and
this app's warning inventory. Its composed source includes the finite legacy
exception reader prerequisite, so the two previous exception-reader diagnostics
are gone. The remaining 16 keys are exactly the 11 existing Cluster diagnostics
from the retained public proof plus the five shared Remote diagnostics. Core is
checked by its separate rooted canary. The shared template defaults preserve the
existing Legacy/System/Misc job commands and markers. The expanded Cluster and
Legacy jobs were replayed locally; hosted CI execution remains pending.
