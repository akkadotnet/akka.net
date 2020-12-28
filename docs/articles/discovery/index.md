# Discovery Overview

> [!WARNING]
>This module is currently marked as **may change**.
>This means that API or semantics can change without warning or deprecation period. It's in a beta state.

Akka.NET Discovery provides an interface around various ways of locating services. The built in methods are:

* Configuration
* DNS
* Aggregate

## How it works

Loading the extension:

```csharp
using Akka.Actor;
using Akka.Discovery;

...

var system = ActorSystem.Create("example");
var serviceDiscovery = Discovery.Get(system).Default;
```

A `Lookup` contains a mandatory `serviceName` and an optional `portName` and `protocol`. How these are interpreted is discovery 
method dependent e.g. DNS does an A/AAAA record query if any of the fields are missing and an SRV query for a full look up:

```csharp
serviceDiscovery.Lookup(new Lookup("akka.io"), TimeSpan.FromSeconds(1));
// convenience for a Lookup with only a serviceName
serviceDiscovery.Lookup("akka.io", TimeSpan.FromSeconds(1));
```

`portName` and `protocol` are optional and their meaning is interpreted by the method.

```csharp
Task<ServiceDiscovery.Resolved> lookup = serviceDiscovery.Lookup(
    new Lookup("akka.io").WithPortName("remoting").WithProtocol("tcp"),
    TimeSpan.FromSeconds(1));
```

Port can be used when a service opens multiple ports e.g. a HTTP port and an Akka remoting port.

## Discovery Method: Configuration

Configuration currently ignores all fields apart from service name.

For simple use cases configuration can be used for service discovery. The advantage of using Akka Discovery with configuration rather than your own configuration values is that applications can be migrated to a more sophisticated discovery method without any code changes.

Configure it to be used as discovery method in your `application.conf`

```
akka {
  discovery.method = config
}
```

By default the services discoverable are defined in `akka.discovery.config.services` and have the following format:

```
akka.discovery.config.services = {
  service1 = {
    endpoints = [
        "cat:1233",
        "dog:1234"
    ]
  },
  service2 = {
    endpoints = []
  }
}
```

Where the above block defines two services, `service1` and `service2`. Each service can have multiple endpoints.

## Discovery Method: Aggregate multiple discovery methods

Aggregate discovery allows multiple discovery methods to be aggregated e.g. try and resolve
via DNS and fall back to configuration.

To use aggregate discovery add its dependency as well as all of the discovery that you
want to aggregate.

Configure `aggregate` as `akka.discovery.method` and which discovery methods are tried and in which order.

```
akka {
  discovery {
    method = aggregate
    aggregate {
      discovery-methods = ["akka-dns", "config"]
    }
    config {
      services {
        service1 {
          endpoints [
              "host1:1233",
              "host2:1234"
          ]
        }
      }
    }
  }
}

```

The above configuration will result in `akka-dns` first being checked and if it fails or returns no
targets for the given service name then `config` is queried which i configured with one service called
`service1` which two hosts `host1` and `host2`.

## Discovery Method: DNS

> [!NOTE]
> Akka.Discovery DNS implementation has not been added yet, but is a work in progress.