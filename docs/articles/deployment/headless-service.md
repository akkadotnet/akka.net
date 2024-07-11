---
uid: headless-service
title: Headless Service
---
# Akka.NET Headless Service

<!-- markdownlint-disable MD033 -->
<iframe width="560" height="315" src="https://www.youtube.com/embed/gpaaCBtKLss" title="YouTube video player" frameborder="0" allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share" allowfullscreen></iframe>
<!-- markdownlint-enable MD033 -->

## Headless Actor

[!code-csharp[Main](../../../src/examples/HeadlessService/AkkaHeadlesssService/HeadlessActor.cs?name=headless-actor)]

## Headless Service

[!code-csharp[Main](../../../src/examples/HeadlessService/AkkaHeadlesssService/AkkaService.cs?name=headless-akka-service)]

## Headless Service Host

[!code-csharp[Main](../../../src/examples/HeadlessService/AkkaHeadlesssService/Program.cs?name=headless-service-program)]

## What About Windows Service?

Well, quickly, we have a sample application, [Windows Service](xref:windows-service), to show how `Windows Services` are now being built with `BackgroundService`!

[learn more](https://docs.microsoft.com/en-us/dotnet/core/extensions/windows-service)
