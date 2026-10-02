---
uid: akka-hosting
title: Akka.Hosting
---

# Akka.Hosting

Akka.Hosting provides HOCON-less configuration, application lifecycle management, `ActorSystem` startup, and actor instantiation for [Akka.NET](https://getakka.net/).

See the ["Introduction to Akka.Hosting - HOCON-less, "Pit of Success" Akka.NET Runtime and Configuration" video](https://www.youtube.com/watch?v=Mnb9W9ClnB0) for a walkthrough of the library and how it can save you a tremendous amount of time and trouble.

> [!NOTE]
> As of this release, the Akka.Hosting packages ship from the main [akka.net](https://github.com/akkadotnet/akka.net) repository and are versioned identically to the rest of Akka.NET. Previously they lived in the separate `akkadotnet/Akka.Hosting` repository.

## What Akka.Hosting Ships

* `Akka.Hosting` - the core package, needed for everything. Provides `AkkaConfigurationBuilder`, the `ActorRegistry`, and `IRequiredActor<TKey>`.
* `Akka.Remote.Hosting` - enables Akka.Remote configuration.
* `Akka.Cluster.Hosting` - used for Akka.Cluster, Akka.Cluster.Sharding, and Akka.Cluster.Tools.
* `Akka.Persistence.Hosting` - used for adding persistence functionality, including local database-less testing.
* `Akka.Hosting.TestKit` - a `Microsoft.Extensions.Hosting`-based TestKit for writing tests against `AkkaConfigurationBuilder`-configured `ActorSystem`s.
* `Akka.Hosting.TestKit.Xunit2` - xUnit 2 bindings for `Akka.Hosting.TestKit`.

## Getting Started

Install the `Akka.Hosting` package, and any of the extension packages you need, from NuGet:

```console
PS> Install-Package Akka.Hosting
```

Then configure your `ActorSystem` using the `AddAkka` extension method on `IServiceCollection`:

```csharp
using Akka.Hosting;
using Akka.Actor;
using Akka.Actor.Dsl;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAkka("MyActorSystem", configurationBuilder =>
{
    configurationBuilder
        .WithActors((system, registry) =>
    {
        var echo = system.ActorOf(act =>
        {
            act.ReceiveAny((o, context) =>
            {
                context.Sender.Tell($"{context.Self} rcv {o}");
            });
        }, "echo");
        registry.TryRegister<Echo>(echo); // register for DI
    });
});

var app = builder.Build();

app.MapGet("/", async (context) =>
{
    var echo = context.RequestServices.GetRequiredService<ActorRegistry>().Get<Echo>();
    var body = await echo.Ask<string>(context.TraceIdentifier, context.RequestAborted).ConfigureAwait(false);
    await context.Response.WriteAsync(body);
});

app.Run();
```

No HOCON is required. Akka.Hosting automatically runs all Akka.NET application lifecycle best practices behind the scenes, and binds the `ActorSystem` and the `ActorRegistry` to the `IServiceCollection` so they can be safely consumed both by actors and by non-Akka.NET parts of your .NET application.

To learn more, see:

* [The `AkkaConfigurationBuilder` API](xref:hosting-configuration-builder)
* [Dependency Injection Outside and Inside Akka.NET](xref:hosting-dependency-injection)
* [Microsoft.Extensions.Configuration Integration](xref:hosting-configuration)
* [Microsoft.Extensions.Logging Integration](xref:hosting-logging)
* [OpenTelemetry Trace Correlation](xref:hosting-opentelemetry)
* [Microsoft.Extensions.Diagnostics.HealthChecks Integration](xref:hosting-health-checks)

## Supported Packages

### Akka.NET Core Packages

* `Akka.Hosting` - the core `Akka.Hosting` package, needed for everything
* `Akka.Remote.Hosting` - enables Akka.Remote configuration
* `Akka.Cluster.Hosting` - used for Akka.Cluster, Akka.Cluster.Sharding, and Akka.Cluster.Tools
* `Akka.Persistence.Hosting` - used for adding persistence functionality to perform local database-less testing

### Akka Persistence Plugins

* [`Akka.Persistence.SqlServer.Hosting`](https://github.com/akkadotnet/Akka.Persistence.SqlServer/tree/dev/src/Akka.Persistence.SqlServer.Hosting) - used for Akka.Persistence.SqlServer support. Documentation can be read [here](https://github.com/akkadotnet/Akka.Persistence.SqlServer/blob/dev/src/Akka.Persistence.SqlServer.Hosting/README.md)
* [`Akka.Persistence.PostgreSql.Hosting`](https://github.com/akkadotnet/Akka.Persistence.PostgreSql/tree/dev/src/Akka.Persistence.PostgreSql.Hosting) - used for Akka.Persistence.PostgreSql support. Documentation can be read [here](https://github.com/akkadotnet/Akka.Persistence.PostgreSql/blob/dev/src/Akka.Persistence.PostgreSql.Hosting/README.md)
* [`Akka.Persistence.Azure.Hosting`](https://github.com/petabridge/Akka.Persistence.Azure) - used for Akka.Persistence.Azure support. Documentation can be read [here](https://github.com/petabridge/Akka.Persistence.Azure/blob/master/README.md)

### Akka.Management Plugins

Useful tools for managing Akka.NET clusters running inside containerized or cloud based environment. `Akka.Hosting` is embedded in each of its packages. See the [Akka.Management GitHub repository](https://github.com/akkadotnet/Akka.Management) for the full list.

#### Akka.Management Core Package

* [`Akka.Management`](https://github.com/akkadotnet/Akka.Management/tree/dev/src/management/Akka.Management) - core module of the management utilities which provides a central HTTP endpoint for Akka management extensions. Documentation can be read [here](https://github.com/akkadotnet/Akka.Management/tree/dev/src/management/Akka.Management#akka-management)
* `Akka.Management.Cluster.Bootstrap` - used to bootstrap a cluster formation inside dynamic deployment environments. Documentation can be read [here](https://github.com/akkadotnet/Akka.Management/tree/dev/src/management/Akka.Management#akkamanagementclusterbootstrap)

  > [!NOTE]
  > As of version 1.0.0, cluster bootstrap came bundled inside the core `Akka.Management` NuGet package and are part of the default HTTP endpoint for `Akka.Management`. All `Akka.Management.Cluster.Bootstrap` NuGet package versions below 1.0.0 should now be considered deprecated.

#### Akka.Discovery Plugins

* [`Akka.Discovery.AwsApi`](https://github.com/akkadotnet/Akka.Management/tree/dev/src/discovery/aws/Akka.Discovery.AwsApi) - provides dynamic node discovery service for AWS EC2 environment. Documentation can be read [here](https://github.com/akkadotnet/Akka.Management/blob/dev/src/discovery/aws/Akka.Discovery.AwsApi/README.md)
* [`Akka.Discovery.Azure`](https://github.com/akkadotnet/Akka.Management/tree/dev/src/discovery/azure/Akka.Discovery.Azure) - provides a dynamic node discovery service for Azure PaaS ecosystem. Documentation can be read [here](https://github.com/akkadotnet/Akka.Management/blob/dev/src/discovery/azure/Akka.Discovery.Azure/README.md)
* [`Akka.Discovery.KubernetesApi`](https://github.com/akkadotnet/Akka.Management/tree/dev/src/discovery/kubernetes/Akka.Discovery.KubernetesApi) - provides a dynamic node discovery service for Kubernetes clusters. Documentation can be read [here](https://github.com/akkadotnet/Akka.Management/blob/dev/src/discovery/kubernetes/Akka.Discovery.KubernetesApi/README.md)

#### Akka.Coordination Plugins

* [`Akka.Coordination.KubernetesApi`](https://github.com/akkadotnet/Akka.Management/tree/dev/src/coordination/kubernetes/Akka.Coordination.KubernetesApi) - provides a lease-based distributed lock mechanism backed by [Kubernetes CRD](https://kubernetes.io/docs/tasks/extend-kubernetes/custom-resources/custom-resource-definitions/) for [Akka.NET Split Brain Resolver](xref:split-brain-resolver), [Akka.Cluster.Sharding](xref:cluster-sharding), and [Akka.Cluster.Singleton](xref:cluster-singleton). Documentation can be read [here](https://github.com/akkadotnet/Akka.Management/blob/dev/src/coordination/kubernetes/Akka.Coordination.KubernetesApi/README.md)
* [`Akka.Coordination.Azure`](https://github.com/akkadotnet/Akka.Management/tree/dev/src/coordination/azure/Akka.Coordination.Azure) - provides a lease-based distributed lock mechanism backed by [Microsoft Azure Blob Storage](https://learn.microsoft.com/en-us/azure/storage/blobs/storage-blobs-overview) for [Akka.NET Split Brain Resolver](xref:split-brain-resolver), [Akka.Cluster.Sharding](xref:cluster-sharding), and [Akka.Cluster.Singleton](xref:cluster-singleton). Documentation can be read [here](https://github.com/akkadotnet/Akka.Management/blob/dev/src/coordination/azure/Akka.Coordination.Azure/README.md)
