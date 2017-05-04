---
uid: custom-serialization
title: Custom serialization
---
# Custom serialization
Serialization of snapshots and payloads of Persistent messages is configurable with Akka's Serialization infrastructure. For example, if an application wants to serialize

- payloads of type MyPayload with a custom MyPayloadSerializer and
- snapshots of type MySnapshot with a custom MySnapshotSerializer

it must add

```hocon
akka.actor {
  serializers {
    my-payload = "docs.persistence.MyPayloadSerializer"
    my-snapshot = "docs.persistence.MySnapshotSerializer"
  }
  serialization-bindings {
    "docs.persistence.MyPayload" = my-payload
    "docs.persistence.MySnapshot" = my-snapshot
  }
}
```
to the application configuration. If not specified, a default serializer is used.
