---
layout: wiki
title: EventBus
---
* Read more on http://doc.akka.io/docs/akka/snapshot/java/event-bus.html

```csharp
var system = ActorSystem.Create("MySystem");
var subscriber = system.ActorOf<SomeActor>();
//Subscribe to messages of type string
system.EventStream.Subscribe(subscriber,typeof(string));
//send a message
system.EventStream.Publish("hello"); //this will be forwarded to subscriber
```