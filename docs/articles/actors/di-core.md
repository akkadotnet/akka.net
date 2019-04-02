---
uid: di-core
title: DI Core
---

# Akka.DI.Core

**Actor Producer Extension** library is used to create a Dependency Injection Container for the [Akka.NET](https://github.com/akkadotnet/akka.net) framework.

# What is it?

**Akka.DI.Core** is an **ActorSystem extension** library for the Akka.NET
framework that provides a simple way to create an Actor Dependency Resolver
that can be used an alternative to the basic capabilities of [Props](xref:receive-actor-api#props)
when you have actors with multiple dependencies.

# How do you create an Extension?

- Create a new class library
- Reference your favorite IoC Container, the Akka.DI.Core, and of course Akka
- Create a class and implement the `IDependencyResolver`

Let's walk through the process of creating one for the CastleWindsor container.
You need to create  a new project named Akka.DI.CastleWindsor with all the necessary references including Akka.DI.Core, Akka, and CastleWindosor.
Name the initial class `WindsorDependencyResolver`.

```csharp
public class WindsorDependencyResolver : IDependencyResolver
{
    // we'll implement IDependencyResolver in the following steps
}
```

Add a constructor and private fields.

```csharp
private IWindsorContainer _container;
private ConcurrentDictionary<string, Type> _typeCache;
private ActorSystem _system;

public WindsorDependencyResolver(IWindsorContainer container, ActorSystem system)
{
    if (system == null) throw new ArgumentNullException("system");
    if (container == null) throw new ArgumentNullException("container");
    _container = container;
    _typeCache = new ConcurrentDictionary<string, Type>(StringComparer.InvariantCultureIgnoreCase);
    _system = system;
    _system.AddDependencyResolver(this);
}
```

You have defined three private fields

- ```IWindsorContainer _container``` is a reference to the CastleWindsor container.
- ```ConcurrentDictionary<string, Type> _typeCache``` is a thread safe map that contains actor name/type associations.
- ```ActorSystem _system``` is a reference to the ActorSystem.

First you need to implement ```GetType```. This is a basic implementation and
is just for demonstration purposes. Essentially this is used by the extension
to get the type of the actor from it's type name.

```csharp
public Type GetType(string actorName)
{
    _typeCache.
        TryAdd(actorName,
            actorName.GetTypeValue() ??
            _container.Kernel
            .GetAssignableHandlers(typeof(object))
            .Where(handler => handler.ComponentModel.Name.Equals(actorName, StringComparison.InvariantCultureIgnoreCase))
            .Select(handler => handler.ComponentModel.Implementation)
            .FirstOrDefault());

     return _typeCache[actorName];
}
```

Secondly you need to implement the ```CreateActorFactory``` method which will be
used by the extension to create the actor. This implementation will depend upon
the API of the container.

```csharp
public Func<ActorBase> CreateActorFactory(Type actorType)
{
    return () => (ActorBase)container.Resolve(actorType);
}
```

Thirdly, you implement the ```Create<TActor>``` which is used register the
`Props` configuration for the referenced actor type with the `ActorSystem`.
This method will always be the same implementation.

```csharp
public Props Create<TActor>() where TActor : ActorBase
{
    return system.GetExtension<DIExt>().Props(typeof(TActor).Name);
}
```

Lastly, you implement the Release method which, in this instance, is very simple.
This method is used to remove the actor from the underlying container.

```csharp
public void Release(ActorBase actor)
{
    this.container.Release(actor);
}
```

**Note: For further details on the importance of the release method please read
the following blog [post](http://blog.ploeh.dk/2014/05/19/di-friendly-framework/).**

The resulting class should look similar to the following:

```csharp
public class WindsorDependencyResolver : IDependencyResolver
{
    private IWindsorContainer container;
    private ConcurrentDictionary<string, Type> typeCache;
    private ActorSystem system;

    public WindsorDependencyResolver(IWindsorContainer container, ActorSystem system)
    {
        if (system == null) throw new ArgumentNullException("system");
        if (container == null) throw new ArgumentNullException("container");
        this.container = container;
        typeCache = new ConcurrentDictionary<string, Type>(StringComparer.InvariantCultureIgnoreCase);
        this.system = system;
        this.system.AddDependencyResolver(this);
    }

    public Type GetType(string actorName)
    {
        typeCache.TryAdd(actorName, actorName.GetTypeValue() ??
            container.Kernel
              .GetAssignableHandlers(typeof(object))
              .Where(handler => handler.ComponentModel.Name.Equals(actorName, StringComparison.InvariantCultureIgnoreCase))
              .Select(handler => handler.ComponentModel.Implementation)
              .FirstOrDefault());

        return typeCache[actorName];
    }

    public Func<ActorBase> CreateActorFactory(Type actorType)
    {
        return () => (ActorBase)container.Resolve(actorType);
    }

    public Props Create<TActor>() where TActor : ActorBase
    {
        return system.GetExtension<DIExt>().Props(typeof(TActor));
    }

    public void Release(ActorBase actor)
    {
        this.container.Release(actor);
    }
}
```

Now, with the preceding class, you can do something like the following example:

```csharp
// Setup CastleWindsor
IWindsorContainer container = new WindsorContainer();
container.Register(Component.For<IWorkerService>().ImplementedBy<WorkerService>());
container.Register(Component.For<TypedWorker>().Named("TypedWorker").LifestyleTransient());

// Create the ActorSystem
using (var system = ActorSystem.Create("MySystem"))
{
    // Create the dependency resolver
    IDependencyResolver resolver = new WindsorDependencyResolver(container, system);

    // Register the actors with the system
    system.ActorOf(system.DI().Props<TypedWorker>(), "Worker1");
    system.ActorOf(system.DI().Props<TypedWorker>(), "Worker2");

    // Create the router
    IActorRef router = system.ActorOf(Props.Empty.WithRouter(new ConsistentHashingGroup(config)));

    // Create the message to send
    TypedActorMessage message = new TypedActorMessage
    {
       Id = 1,
       Name = Guid.NewGuid().ToString()
    };

    // Send the message to the router
    router.Tell(message);
}
```
