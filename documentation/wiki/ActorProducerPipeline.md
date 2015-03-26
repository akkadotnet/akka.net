---
layout: wiki
title: ActorProducerPipeline
---
## Actor producer pipeline

Akka.NET gives you an ability to inject your custom behavior in the middle of actor production pipeline in safe, well-defined manner. This is done through specialized constructs called actor producer plugins or actor pipeline plugins. How do they relate to the rest of actor lifecycle?

Simplified actor incarnation cycle may look like:

- ActorCell creates new actor instance
- Actor pipeline is resolved and all included plugins `AfterIncarnated` methods are applied to actor instance
- If actor implements `InitializableActor` interface, it's `Init` method becomes invoked
- Actor is ready to serve incoming messages

In case of stop/failure similar cycle is rolled out:

- Actor's `AroundPreRestart` or `AroundPostStop` is invoked
- Actor pipeline is resolved and all included plugins `BeforeIncarnated` methods are applied to actor instance
- If actor implements `IDisposable` interface, it's `Dispose` method becomes invoked
- Actor becomes recycled

Custom actor pipeline plugins may be created by using `IActorProducerPlugin` or one of it's specializations `ActorProducerPluginBase` and it's generic version. What's common to all of them is that they define three major methods:

- `CanBeAppliedTo` is used to determine if current type of the actor is a valid one to apply plugin behavior to it's instances.
- `AfterIncarnated` is invoked on target actor after it's fresh instance has been created.
- `BeforeIncarnated` is invoked on target actor before it's being recycled.

All pipeline plugins can be registered directly through actor pipeline resolver, which is accessible for any `ExtendedActorSystem` instance. Therefore their best use case is to include them as part of custom akka extensions, hence their operate directly on extended actor system version.

Example bellow illustrates, how a custom actor pipeline plugin can be created and registered in actor system:

```csharp
class MyActorPlugin : ActorProducerPluginBase<MyActor>
{
	public override void AfterIncarnated(MyActor actor, IActorContext context)
	{
		Console.WriteLine("Plugged custom behavior to actor {0}", context.Self.Path);
	}
}

var extendedSystem = actorSystem as ExtendedActorSystem;
extendedSystem.ActorPipelineResolver.Register(new MyActorPlugin());
extendedSystem.ActorPipelineResolver.Insert(0, new MyOtherActorPlugin());
```

It's important to note that actor pipeline plugins are not moved automatically in remote actor deployment scenarios. They must be registered manually on each actor system utilizing their features.
