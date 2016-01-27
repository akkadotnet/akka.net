#Akka.DI.Ninject

**Actor Producer Extension** backed by the [Ninject](http://www.ninject.org/) Dependency Injection Container for the [Akka.NET](https://github.com/akkadotnet/akka.net) framework.

#What is it?

**Akka.DI.Ninject** is an **ActorSystem extension** for the Akka.NET framework that provides an alternative to the basic capabilities of [Props](http://getakka.net/docs/Props) when you have Actors with multiple dependencies.  

If Ninject is your IoC container of choice and your actors have dependencies that make using the factory method provided by Props prohibitive  and code maintenance is an important concern then this is the extension for you.

#How to you use it?

The best way to understand how to use it is by example. If you are already considering this extension then we will assume that you know how how to use the [Ninject](http://www.ninject.org/) container. This example is demonstrating a system using [ConsistentHashing](http://getakka.net/docs/working-with-actors/Routers#consistenthashing) routing along with this extension.

Start by creating your ```StandardKernel```, registering your actors and dependencies.

```csharp
// Setup Ninject
IKernel container = new StandardKernel();
container.Bind<TypedWorker>().To(typeof(TypedWorker));
container.Bind<IWorkerService>().To(typeof(WorkerService));
```

Next you have to create your ```ActorSystem``` and inject that system reference along with the container reference into a new instance of the ```NinjectDependencyResolver```.

```csharp
// Create the ActorSystem
using (var system = ActorSystem.Create("MySystem"))
{
    // Create the dependency resolver
    IDependencyResolver resolver = new NinjectDependencyResolver(container, system);

    // we'll fill in the rest in the following steps
}
```

To register the actors with the system use method ```Akka.Actor.Props Create<TActor>()``` of the  ```IDependencyResolver``` interface implemented by the ```NinjectDependencyResolver```.

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
// Setup Ninject
IKernel container = new StandardKernel();
container.Bind<TypedWorker>().To(typeof(TypedWorker));
container.Bind<IWorkerService>()To(typeof)WorkerService));

// Create the ActorSystem
using (var system = ActorSystem.Create("MySystem"))
{
    // Create the dependency resolver
    IDependencyResolver resolver = new NinjectDependencyResolver(container, system);

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
