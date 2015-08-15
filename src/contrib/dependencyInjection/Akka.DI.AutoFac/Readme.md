#Akka.DI.Autofac

**Actor Producer Extension** backed by the [Autofac](https://github.com/autofac/Autofac) Dependency Injection Container for the [Akka.NET](https://github.com/akkadotnet/akka.net) framework.

#What is it?

**Akka.DI.Autofac** is an **ActorSystem extension** for the Akka.NET framework that provides an alternative to the basic capabilities of [Props](http://getakka.net/docs/Props) when you have Actors with multiple dependencies.  

If Autofac is your IoC container of choice and your actors have dependencies that make using the factory method provided by Props prohibitive  and code maintenance is an important concern then this is the extension for you.

#How to you use it?

The best way to understand how to use it is by example. If you are already considering this extension then we will assume that you know how how to use the [Autofac](https://github.com/autofac/Autofac) container. This example is demonstrating a system using [ConsistentHashing](http://getakka.net/docs/working-with-actors/Routers#consistenthashing) routing along with this extension.

Start by creating your builder, registering your actors and dependencies, and build your container.

```csharp
// Setup Autofac
ContainerBuilder builder = new ContainerBuilder();
builder.RegisterType<WorkerService>().As<IWorkerService>();
builder.RegisterType<TypedWorker>();
IContainer container = builder.Build();
```

Next you have to create your ```ActorSystem``` and inject that system reference along with the container reference into a new instance of the ```AutoFacDependencyResolver```.

```csharp
// Create the ActorSystem
using (var system = ActorSystem.Create("MySystem"))
{
    // Create the dependency resolver
    IDependencyResolver resolver = new AutoFacDependencyResolver(container, system);

    // we'll fill in the rest in the following steps
}
```

To register the actors with the system use method ```Akka.Actor.Props Create<TActor>()``` of the  ```IDependencyResolver``` interface implemented by the ```AutoFacDependencyResolver```.

```csharp
// Register the actors with the system
system.ActorOf(resolver.Create<TypedWorker>(), "Worker1");
system.ActorOf(resolver.Create<TypedWorker>(), "Worker2");
```

Finally create your router, message and send the message to the router.

```csharp
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
```

The resulting code should look similar to the the following:

```csharp
// Setup Autofac
ContainerBuilder builder = new ContainerBuilder();
builder.RegisterType<WorkerService>().As<IWorkerService>();
builder.RegisterType<TypedWorker>();
IContainer container = builder.Build();

// Create the ActorSystem
using (var system = ActorSystem.Create("MySystem"))
{
    // Create the dependency resolver
    IDependencyResolver resolver = new AutoFacDependencyResolver(container, system);

    // Register the actors with the system
    system.ActorOf(resolver.Create<TypedWorker>(), "Worker1");
    system.ActorOf(resolver.Create<TypedWorker>(), "Worker2");

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
