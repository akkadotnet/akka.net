---
uid: windows-service
title: Windows Service
---

# Windows Service

[learn more from Microsoft](https://docs.microsoft.com/en-us/dotnet/core/extensions/windows-service)

## Program.cs

[!code-csharp[Main](../../../src/examples/WindowsService/AkkaWindowsService/Program.cs?name=akka-windows-service-program)]

## AkkaService.cs

[!code-csharp[Main](../../../src/examples/WindowsService/AkkaWindowsService/AkkaService.cs?name=akka-windows-service)]

## JokeService.cs

[!code-csharp[Main](../../../src/examples/WindowsService/AkkaWindowsService/JokeService.cs?name=akka-windows-joke-service)]

## MyActor.cs

[!code-csharp[Main](../../../src/examples/WindowsService/AkkaWindowsService/MyActor.cs?name=akka-windows-service-actor)]
