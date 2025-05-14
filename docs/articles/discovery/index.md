---
uid: akka-discovery
title: Akka.NET Service Discovery with Akka.Discovery
---

# Akka.Discovery Overview

Akka.NET Discovery provides an interface around various ways of locating services. The built-in methods are:

* Configuration
* Aggregate

---

## Video: Form Akka.NET Clusters Dynamically with Akka.Management and Akka.Discovery

<!-- markdownlint-disable MD033 -->
<iframe width="560" height="315" src="https://www.youtube.com/embed/XCcrlhVtbKI" title="YouTube video player" frameborder="0" allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share" allowfullscreen></iframe>
<!-- markdownlint-enable MD033 -->

Watch this companion video for a visual walkthrough of dynamic cluster formation using Akka.Management and Akka.Discovery.

---

## Supported Akka.Discovery Plugins

* [`Akka.Discovery.AwsApi`](https://github.com/akkadotnet/Akka.Management/tree/dev/src/discovery/aws/Akka.Discovery.AwsApi): AWS EC2/ECS discovery.
* [`Akka.Discovery.KubernetesApi`](https://github.com/akkadotnet/Akka.Management/tree/dev/src/discovery/kubernetes/Akka.Discovery.KubernetesApi): Kubernetes API discovery.
* [`Akka.Discovery.Azure`](https://github.com/akkadotnet/Akka.Management/tree/dev/src/discovery/azure/Akka.Discovery.Azure): Azure Table Storage discovery.
* `Akka.Discovery.Configuration`: Built-in, static config-based discovery.

---

## How Akka.Management and Akka.Discovery Work Together

Akka.Management exposes HTTP endpoints for cluster coordination.
Akka.Discovery queries your environment (cloud, Kubernetes, Azure, etc.) for available nodes.
Cluster.Bootstrap uses these to dynamically populate the seed nodes list and safely form or join a cluster.

---

## Configuration with Akka.Hosting (Recommended)

> **Recommended:** We strongly encourage users to configure Akka.Discovery using [Akka.Hosting](https://github.com/akkadotnet/Akka.Hosting) for a modern, type-safe, and composable experience.

Example using Akka.Discovery.Azure with Akka.Hosting, including remoting, clustering, Akka.Management, and Cluster Bootstrap:

```csharp
using Akka.Hosting;
using Akka.Discovery.Azure;

var builder = Host.CreateDefaultBuilder()
    .ConfigureServices((context, services) =>
    {
        services.AddAkka("my-actor-system", (akkaBuilder, provider) =>
        {
            // Remoting
            akkaBuilder.WithRemoting("0.0.0.0", 4053);

            // Clustering (clear out static seed nodes for bootstrap)
            akkaBuilder.WithClustering(new ClusterOptions
            {
                SeedNodes = Array.Empty<string>(),
                Roles = new[] { "backend" }
            });

            // Akka.Management HTTP endpoint
            akkaBuilder.WithAkkaManagement(port: 8558);

            // Cluster Bootstrap
            akkaBuilder.WithClusterBootstrap(
                serviceName: "my-akka-service", // must match Azure discovery service name
                portName: "akka-remote",        // optional, for multi-port scenarios
                requiredContactPoints: 3         // safe value, never 1
            );

            // Azure Discovery
            akkaBuilder.WithAzureDiscovery(options =>
            {
                options.ServiceName = "my-akka-service";
                options.ConnectionString = "<your Azure Table Storage connection string>";
            });
        });
    });
```

**Notes:**

* The `SeedNodes` array must be empty for Cluster Bootstrap to take over.
* `requiredContactPoints` should be set to a safe value (typically 2 or 3 for production).
* The `serviceName` must match between Azure Discovery and Cluster Bootstrap.
* Adjust remoting and management ports as needed for your environment.

---

## Configuration with HOCON

You can also configure Akka.Discovery using HOCON directly. This is the traditional approach, but we recommend using Akka.Hosting for new projects.

### Discovery Method: Configuration

Configuration currently ignores all fields apart from service name.

For simple use cases, configuration can be used for service discovery. The advantage of using Akka Discovery with configuration rather than your own configuration values is that applications can be migrated to a more sophisticated discovery method without any code changes.

Configure it to be used as discovery method in your `application.conf`:

```hocon
akka {
  discovery.method = config
}
```

By default, the services discoverable are defined in `akka.discovery.config.services` and have the following format:

```hocon
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

---

## Discovery Method: Aggregate Multiple Discovery Methods

Aggregate discovery allows multiple discovery methods to be aggregated, e.g., try and resolve via one method and fall back to configuration.

To use aggregate discovery, add its dependency as well as all of the discovery methods that you want to aggregate.

Configure `aggregate` as `akka.discovery.method` and which discovery methods are tried and in which order.

```hocon
akka {
  discovery {
    method = aggregate
    aggregate {
      discovery-methods = ["azure", "config"]
    }
    azure {
      # Azure plugin configuration here
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

The above configuration will result in Azure being checked first, and if it fails or returns no targets for the given service name, then config is queried.

---

## Migration From Seed Nodes

Dynamic discovery is superior to static seed nodes because it allows clusters to form and recover in dynamic environments (cloud, PaaS, Kubernetes) where static addresses are not available or reliable. Static seed nodes can lead to split-brain scenarios and are not suitable for modern deployments.

---

## Caveats and Best Practices

* `requiredContactPoints` must be set to a safe value (never 1).
* All nodes must have the same discovery method and service name.
* If using Akka.Management, always clear out static `SeedNodes`.
* Use `contactWithAllContactPoints` for higher consistency if needed.

---

## Further Reading

* [DrawTogether.NET](https://github.com/petabridge/DrawTogether.NET) for a production example.
* [Form Akka.NET Clusters Dynamically with Akka.Management and Akka.Discovery (blog post)](https://petabridge.com/blog/akka-management/)
* [Companion video](https://www.youtube.com/watch?v=XCcrlhVtbKI)

---

For API details, see <xref:Akka.Discovery.Discovery>, <xref:Akka.Discovery.Lookup>, and related types.

Loading the extension:

```csharp
using Akka.Actor;
using Akka.Discovery;

...

var system = ActorSystem.Create("example");
var serviceDiscovery = Discovery.Get(system).Default;
```

A `Lookup` contains a mandatory `serviceName` and an optional `portName` and `protocol`. How these are interpreted is discovery method dependent.

```csharp
serviceDiscovery.Lookup(new Lookup("akka.net"), TimeSpan.FromSeconds(1));
// convenience for a Lookup with only a serviceName
serviceDiscovery.Lookup("akka.net", TimeSpan.FromSeconds(1));
```

`portName` and `protocol` are optional and their meaning is interpreted by the method.

```csharp
Task<ServiceDiscovery.Resolved> lookup = serviceDiscovery.Lookup(
    new Lookup("akka.net").WithPortName("remoting").WithProtocol("tcp"),
    TimeSpan.FromSeconds(1));
```

Port can be used when a service opens multiple ports, e.g., a HTTP port and an Akka remoting port.
