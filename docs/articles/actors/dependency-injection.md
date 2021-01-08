---
uid: dependency-injection
title: Dependency injection
---
# Dependency Injection
If your actor has a constructor that takes parameters then those need
to be part of the `Props` as well, as described above. But there are cases when
a factory method must be used, for example when the actual constructor arguments
are determined by a dependency injection framework.

The basic functionality is provided by a `DependencyResolver` class,
that can create `Props` using the DI container.

```csharp
// Create your DI container of preference
var someContainer = ... ;

// Create the actor system
var system = ActorSystem.Create("MySystem");

// Create the dependency resolver for the actor system
IDependencyResolver resolver = new XyzDependencyResolver(someContainer, system);
```
When creating actorRefs straight off your ActorSystem instance, you can use the DI() Extension.

```csharp
// Create the Props using the DI extension on your ActorSystem instance
var worker1Ref = system.ActorOf(system.DI().Props<TypedWorker>(), "Worker1");
var worker2Ref = system.ActorOf(system.DI().Props<TypedWorker>(), "Worker2");
```

## Creating Child Actors using DI
When you want to create child actors from within your existing actors using
Dependency Injection you can use the Actor Content extension just like in
the following example.

```csharp
// For example in the PreStart...
protected override void PreStart()
{
	var actorProps = Context.DI().Props<MyActor>()
		.WithRouter(/* options here */);
	
	var myActorRef = Context.ActorOf(actorProps, "myChildActor");
}
```

> [!NOTE]
> There is currently still an extension method available for the actor Context. `Context.DI().ActorOf<>`. However this has been officially **deprecated** and will be removed in future versions.

## Notes

> [!WARNING]
> You might be tempted at times to use an `IndirectActorProducer`
which always returns the same instance, e.g. by using a static field. This is
not supported, as it goes against the meaning of an actor restart, which is
described here: [What Restarting Means](xref:supervision#what-restarting-means).

When using a dependency injection framework, there are a few things you have
to keep in mind.

When scoping actor type dependencies using your DI container, only
`TransientLifestyle` or `InstancePerDependency` like scopes are supported.
This is due to the fact that Akka explicitly manages the lifecycle of its
actors. So any scope which interferes with that is not supported.

This also means that when injecting dependencies into your actor, using a
Singleton or Transient scope is fine. But having that dependency scoped per
httpwebrequest for example won't work.

Techniques for dependency injection and integration with dependency injection
frameworks are described in more depth in the
[Using Akka with Dependency Injection](http://letitcrash.com/post/55958814293/akka-dependency-injection)
guideline.

----

Currently the following Akka.NET Dependency Injection plugins are available:

### AutoFac

In order to use this plugin, install the Nuget package with
`Install-Package Akka.DI.AutoFac`, then follow the instructions:

```csharp
// Create and build your container
var builder = new Autofac.ContainerBuilder();
builder.RegisterType<WorkerService>().As<IWorkerService>();
builder.RegisterType<TypedWorker>();
var container = builder.Build();

// Create the ActorSystem and Dependency Resolver
var system = ActorSystem.Create("MySystem");
system.UseAutofac(container);
```

### CastleWindsor

In order to use this plugin, install the Nuget package with
`Install-Package Akka.DI.CastleWindsor`, then follow the instructions:

```csharp
// Create and build your container
var container = new WindsorContainer();
container.Register(Component.For<IWorkerService>().ImplementedBy<WorkerService>());
container.Register(Component.For<TypedWorker>().Named("TypedWorker").LifestyleTransient());

// Create the ActorSystem and Dependency Resolver
var system = ActorSystem.Create("MySystem");
var propsResolver = new WindsorDependencyResolver(container, system);
```

### Ninject

In order to use this plugin, install the Nuget package with
`Install-Package Akka.DI.Ninject`, then follow the instructions:

```csharp
// Create and build your container
var container = new Ninject.StandardKernel();
container.Bind<TypedWorker>().To(typeof(TypedWorker));
container.Bind<IWorkerService>().To(typeof(WorkerService));

// Create the ActorSystem and Dependency Resolver
var system = ActorSystem.Create("MySystem");
var propsResolver = new NinjectDependencyResolver(container,system);
```

### Other frameworks

Support for additional dependency injection frameworks may be added in the
future, but you can easily implement your own by implementing an
[Actor Producer Extension](xref:di-core).
