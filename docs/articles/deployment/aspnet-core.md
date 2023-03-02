---
uid: aspnet-core-scenario
title: ASP.NET Core
---

# ASP.NET Core

<!-- markdownlint-disable MD033 -->
<iframe width="560" height="315" src="https://www.youtube.com/embed/_BVC9Is8Tnk" title="YouTube video player" frameborder="0" allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share" allowfullscreen></iframe>
<!-- markdownlint-enable MD033 -->

## The Bridge

When deploying Akka.NET in ASP.NET Core, one major concern is how to expose `actor` in an ASP.NET Core controllers. We will design an `interface` for this!

[!code-csharp[Main](../../../src/examples/AspNetCore/Akka.AspNetCore/IActorBridge.cs?name=actor-bridge)]

## Akka.NET with `IHostedService`

With the `IActorBridge` created, next is to host Akka.NET with `IHostedService` which will also implement the `IActorBridge`:

[!code-csharp[Main](../../../src/examples/AspNetCore/Akka.AspNetCore/AkkaService.cs?name=akka-aspnet-core-service)]

## Interaction Between Controllers and Akka.NET

[!code-csharp[Main](../../../src/examples/AspNetCore/Akka.AspNetCore/Controllers/AkkaController.cs?name=akka-aspnet-core-controllers)]

### Wire up Akka.NET and ASP.NET Core

We need to replace the default `IHostedService` with `AkkaService` and also inject `IActorBridge`.

[!code-csharp[Main](../../../src/examples/AspNetCore/Akka.AspNetCore/Program.cs?name=akka-asp-net-program)]

> [!NOTE]
> Visit our site's blog post for [Best Practices for Integrating Akka.NET with ASP.NET Core and SignalR](https://petabridge.com/blog/akkadotnet-aspnetcore)
