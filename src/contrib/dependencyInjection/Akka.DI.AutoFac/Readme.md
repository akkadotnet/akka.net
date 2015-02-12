#Akka.DI.Autofac

**Actor Producer Extension** backed by the [Autofac](https://github.com/autofac/Autofac) Dependency Injection Container for the [Akka.NET](https://github.com/akkadotnet/akka.net) framework.

#What is it?

**Akka.DI.Autofac** is an **ActorSystem extension** for the Akka.NET framework that provides an alternative to the basic capabilities of [Props](http://akkadotnet.github.io/wiki/Props) when you have Actors with multiple dependencies.  

If Autofac is your IOC container of choice and your Actors have dependencies that make using the factory method provided by Props prohibitive  and code maintenance is an important concern then this is the extension for you.

#How to you use it?

The best way to understand how to use it is by example. If you are already considering this extension then we will assume that you know how how to use the [Autofac](https://github.com/autofac/Autofac) container. This example is demonstrating a system using ConsistentHashingGroup Routing along with extension. 

Start by creating your builder, registering your Actors and dependencies, and build your container.

    //Create builder
    var builder = new Autofac.ContainerBuilder();

    //Register Actors and dependent services
	builder.RegisterType<WorkerService>().As<IWorkerService>();
    builder.RegisterType<TypedWorker>();

    //Build the container
    Autofac.IContainer container = builder.Build();
       
Next you have to create your ActorSystem and inject that system reference along with the container reference into a new instance of the AutoFacDependencyResolver.

    //Create ActorSystem
    using (var system = ActorSystem.Create("MySystem"))
        {
           //Create the dependency resolver
           IDependencyResolver propsResolver = 
               new AutoFacDependencyResolver(container, system);

To register the Actors with the system use method `Akka.Actor.Props Create<TActor>()` Member of `IDependencyResolver` method implemented by the AutoFacDependencyResolver.
		
			system.ActorOf(propsResolver.Create<TypedWorker>(), "Worker1");
			system.ActorOf(propsResolver.Create<TypedWorker>(), "Worker2");

Finally create your router, message and send the message to the router.

            var hashGroup = 
                system.ActorOf(Props.Empty.WithRouter(new ConsistentHashingGroup(config)));
 
            TypedActorMessage msg = 
               new TypedActorMessage { Id = 1, 
                                       Name = Guid.NewGuid().ToString() };
             hashGroup.Tell(msg);

		}

