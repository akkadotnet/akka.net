---
layout: wiki
title: EventStream
---
```csharp
var system = ActorSystem.Create("MySystem");
var subscriber = system.ActorOf<SomeActor>();
//Subscribe to messages of type string
system.EventStream.Subscribe(subscriber,typeof(string));
//send a message
system.EventStream.Publish("hello"); //this will be forwarded to subscriber
```