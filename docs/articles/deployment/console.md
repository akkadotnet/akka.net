---
uid: console-application
title: Console Application
---

## Create message class `Greet`

The way to tell an actor to do something is by sending it a message. `Greet` is the message type to be sent!

[!code-csharp[Main](../../../src/examples/HelloAkka/HelloWorld/Greet.cs?name=hello-world-message)]

## Creating the `GreetingActor`

[!code-csharp[Main](../../../src/examples/HelloAkka/HelloWorld/GreetingActor.cs?name=akka-hello-world-greeting)]

`PreStart` will be called by the Akka framework when the actor is getting started. The `PostStop` will be called after the actor is stopped.

## Creating a Console host

[!code-csharp[Main](../../../src/examples/HelloAkka/HelloWorld/Program.cs?name=akka-hello-world-main)]