---
layout: wiki
title: DI Core
---

#Akka.DI.Core

**Actor Producer Extension** library used to create Dependency Injection Container for the [Akka.NET](https://github.com/akkadotnet/akka.net) framework.

#What is it?

**Akka.DI.Core** is an **ActorSystem extension** library for the Akka.NET framework that provides a simple way to create an Actor Dependency Resolver that can be used an alternative to the basic capabilities of [Props](http://akkadotnet.github.io/wiki/Props) when you have Actors with multiple dependencies.  

#How do you create an Extension?

-  Create a new class library
-  Reference your favorite IOC Container, the Akka.DI.Core and of course Akka
-  Create a class and implement the IDependencyResolver

Let's walk through the process of creating one for CastleWindsor container. You need to create  a new project named Akka.DI.CastleWindsor with all the necessary references including Akka.DI.Core, Akka and CastleWindosor. Name the initial class WindsorDependencyResolver.

```csharp
public class WindsorDependencyResolver : IDependencyResolver
{
	Type GetType(string ActorName)
	{
	    throw new NotImplementedException();
	}
	
	Func<ActorBase> CreateActorFactory(string ActorName)
	{
	    throw new NotImplementedException();
	}
	
	Props Create<TActor>()
	{
	    throw new NotImplementedException();
	}
}
```

Add a constructor and private fields.

```csharp
private IWindsorContainer container;
private ActorSystem system;

public WindsorDependencyResolver(IWindsorContainer container, ActorSystem system)
{
    if (system == null) throw new ArgumentNullException("system");
    if (container == null) throw new ArgumentNullException("container");
    this.container = container;
    this.system = system;
    this.system.AddDependencyResolver(this);
}
```

You have defined three private fields

- IWindsorContainer container
	- Reference to the container
- ActorSystem system
	- Reference to the ActorSystem

First you need to implement GetType. This is a basic implementation and is just from demonstration purposes. Essentially this is used by the Extension to get the Type of the Actor from it's type name.

```csharp
Type GetType(string actorName)
{
    var firstTry = Type.GetType(actorName);
    Func<Type> searchForType = () =>
    {
        return
        AppDomain.
            CurrentDomain.
            GetAssemblies().
            SelectMany(x => x.GetTypes()).
            Where(t => t.Name.Equals(actorName)).
            FirstOrDefault();
    };
    return firstTry ?? searchForType();
}
```
	
Secondly you need to implement the CreateActorFactory method which will be used by the extension to create the Actor. This implementation will depend upon the API of the container.

```csharp
public Func<ActorBase> CreateActorFactory(string actorName)
{
    return () => (ActorBase)container.Resolve(GetType(actorName));
}
```

Lastly, you implement the Create<TActor> which is used register the Props configuration for the referenced Actor Type with the ActorSystem. This method will always be the same implementation. 

```csharp
public Props Create<TActor>() where TActor : ActorBase
{
    return system.GetExtension<DIExt>().Props(typeof(TActor).Name);
}
```

So with that you can do something like the following code example:

```csharp
IWindsorContainer container = new WindsorContainer();
container.Register(Component.For<IWorkerService>().ImplementedBy<WorkerService>());
container.Register(Component.For<TypedWorker>().Named("TypedWorker").LifestyleTransient());

//Create ActorSystem
using (var system = ActorSystem.Create("MySystem"))
{
   //Create the dependency resolver
   IDependencyResolver propsResolver = 
		new WindsorDependencyResolver(container,system);

		system.ActorOf(propsResolver.Create<TypedWorker>(), "Worker1");
		system.ActorOf(propsResolver.Create<TypedWorker>(), "Worker2");

    var hashGroup = 
        system.ActorOf(Props.Empty.WithRouter(new ConsistentHashingGroup(config)));

    TypedActorMessage msg = 
       new TypedActorMessage { Id = 1, 
                               Name = Guid.NewGuid().ToString() };
     hashGroup.Tell(msg);
}
```
