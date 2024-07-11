---
uid: wire-compatibility
title: Making Wire Format Changes to Akka.NET
---

# Making Wire Format Changes to Akka.NET

Sometimes it's necessary to introduce new elements to the wire format of Akka.Remote, Akka.Cluster, or Akka.Persistence. This document explains how we try to do that safely in a manner that supports rolling upgrades with no downtime inside production Akka.NET clusters.

## Wire Compatibility

[Wire compatibility is a distinct problem from API / binary compatibility](https://aaronstannard.com/oss-compatibility-standards/) - and the big problem with wire compatibility is that it runs in two directions:

1. **Backward compatibility**: old versions of the software must be able to successfully send messages to new versions of the software;
2. **Backward compatibility**: new versions of the software must be able to process previous versions of the wire format; and
3. **Forward compatibility**: old versions of the software must be able to process messages from _new versions of the software_ during the upgrade.

This can be difficult to do correctly especially the forward compatibility requirement.

### Akka.NET's Wire Compatibility Requirements

Here are the requirements that Akka.NET introduces to its wire compatibility:

1. All messages written using a previous stable release of Akka.NET should always be able to be read in the future; this is true across even major version upgrades_.
2. New changes to the wire format can be introduced on the read-side at any time, but the write-side must always be opt-in (disabled by default). This is designed to give the Akka.NET install base some time to gradually absorb the functioning, but mostly dormant read-side code into their applications so future rolling upgrades can be safely executed in the future.
3. Wire format changes can be made opt-out only after the release of the next minor version of Akka.NET, after which users have had a significant number of versions where the read-side code.
4. Under no circumstances are new wire types to be introduced using any type of polymorphic serialization. Schema-based serialization via Google Protocol Buffers only.

Again, we [apply the principles of extend-only design](https://aaronstannard.com/extend-only-design/) here. Once you incorporate a change into the wire format it's there for good.

#### Safely Enhancing Existing Wire Types and Introducing New Ones

Akka.NET largely relies on Google Protocol Buffers for all of its internal messaging, and although we support polymorphic serialization by default for user-objects we strongly encourage those users to adopt schema-based serialization as well.

One of the reasons why schema-based serialization is a preferred choice over polymorphic serialization is its inherent support for sane, manageable versioning. [Google's advice on how to update existing message types](https://developers.google.com/protocol-buffers/docs/proto3#updating) is excellent on this subject.

##### Case Study: `Heartbeat` Messages in Akka.Cluster

Consider the `Heartbeat` message in Akka.Cluster:

[!code-csharp[Heartbeat](../../../src/core/Akka.Cluster/ClusterHeartbeat.cs?name=Heartbeat)]

Prior to Akka.NET 1.4.19, we represented `Heartbeat` messages over the wire simply by piggy-backing off of the `Address` data type:

<!-- not using a `code-protobuf` block here because tags aren't supported for `.proto` files -->
```proto
// Defines a remote address.
message AddressData {
  string system = 1;
  string hostname = 2;
  uint32 port = 3;
  string protocol = 4;
}
```

In Akka.NET v1.4.19 we wanted to add some additional data to `Heartbeat` so we could keep track of inter-node latency and this would require us to introduce an entirely new wire type. How could we accomplish this?

> [!TIP]
> All Akka.NET serializers should implement the [`SerializerWithStringManifest` base class](xref:Akka.Serialization.SerializerWithStringManifest), which allows for explicit control type identifiers on the wire.

First, we introduced a new Protobuf to our other message definitions:

<!-- not using a `code-protobuf` block here because tags aren't supported for `.proto` files -->
```proto
/**
 * Prior to version 1.4.19
 * Heartbeat
 * Sends an Address
 * Version 1.4.19 can deserialize this message but does not send it
 */
 message Heartbeat {
  Akka.Remote.Serialization.Proto.Msg.AddressData from = 1;
  int64 sequenceNr = 2;
  sint64 creationTime = 3;
}

/**
 * Prior to version 1.4.19
 * HeartbeatRsp
 * Sends an UniqueAddress
 * Version 1.4.19 can deserialize this message but does not send it
 */
 message HeartBeatResponse {
  UniqueAddress from = 1;
  int64 sequenceNr = 2;
  int64 creationTime = 3;
}
```

And we updated the `ClusterMessageSerializer` to consume these new Protobuf types:

[!code-csharp[Heartbeat](../../../src/core/Akka.Cluster/Serialization/ClusterMessageSerializer.cs?name=MsgRead)]

But, importantly, **we didn't add the code to begin producing any of these message types immediately** - as we need the read-side code to propagate through the install base first. We will very likely switch over to the new message type for Akka.NET v1.5. This is the crucial step - allowing the read-side code to make its way into users production applications prior to enabling the new message types.

Another way we could do this is to introduce a configuration setting that is set to `off` by default, but when set to `on` enables the production of these new message types.
