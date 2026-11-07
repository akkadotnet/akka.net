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
|---|---|---|---|---|
| Reliable Delivery, including durable producer queues | `reliable-delivery` / 36 | `reliable-delivery-v2` / 76 | Unreleased; [#8409](https://github.com/akkadotnet/akka.net/pull/8409) | Generic Delivery messages throw `SerializationException` when `Akka.DynamicTypeLoading` is off, on the JIT as well as Native AOT. |

The table identifies reader availability, not a performance or Native AOT recommendation.
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

Enabling the global setting also applies to new ports introduced by a later package upgrade.
Review that release's takeover table and retain explicit legacy bindings for any subsystem
you have not validated.

## Roll Back Writers While Retaining Readers

Set `akka.actor.serialization-v2 = off`, or call `WithSerializationV2(false)`, and restart
the affected actor systems. Future default writes return to the legacy serializers. Explicit
user bindings still take precedence, including an explicit binding to a V2 alias.

Previously stored V2 events and snapshots keep their id 76 payloads. Keep binaries containing
that reader for all future recoveries. Turning the setting off does not rewrite those records
or make them readable by a binary without the V2 serializer. A backup taken before enablement
does not include subsequent writes; restoring it requires a separate application data-recovery
decision.

The DistributedData LMDB store has no per-record serializer id. Its V2 migration must keep
durable storage pinned to the legacy serializer; changing the network binding must not change
the storage format. That prerequisite is tracked in [#8716](https://github.com/akkadotnet/akka.net/issues/8716).

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
