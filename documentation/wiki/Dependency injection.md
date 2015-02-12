---
layout: wiki
title: Dependency injection
---
### Dependency Injection
If your UntypedActor has a constructor that takes parameters then those need to be part of the Props as well, as described above. But there are cases when a factory method must be used, for example when the actual constructor arguments are determined by a dependency injection framework.

The basic functionality is provided by a `DependencyResolver` class, that can create `Props` using the DI container.

```csharp
// Create your DI container of preference
var someContainer = ... ; 

// Create the actor system
var system = ActorSystem.Create("MySystem");
            
// Create the dependency resolver for the actor system
IDependencyResolver propsResolver = new XyzDependencyResolver(someContainer, system);

// Create the Props using the IDependencyResolver instance
system.ActorOf(propsResolver.Create<TypedWorker>(), "Worker1");
system.ActorOf(propsResolver.Create<TypedWorker>(), "Worker2");

``` 

Currenty, the following Akka.NET Dependency Injection plugins are available:

* **AutoFac**

In order to use this plugin, install the Nuget package with `Install-Package Akka.DI.AutoFac`, then follow the instructions:

```csharp
// Create and build your container
var builder = new Autofac.ContainerBuilder();
builder.RegisterType<WorkerService>().As<IWorkerService>();
builder.RegisterType<TypedWorker>();
var container = builder.Build();

// Create the ActorSystem and Dependency Resolver
var system = ActorSystem.Create("MySystem");
var propsResolver = new AutoFacDependencyResolver(container, system);
```

* **CastleWindsor**

In order to use this plugin, install the Nuget package with `Install-Package Akka.DI.CastleWindsor`, then follow the instructions:

```csharp
// Create and build your container
var container = new WindsorContainer();
container.Register(Component.For<IWorkerService>().ImplementedBy<WorkerService>());
container.Register(Component.For<TypedWorker>().Named("TypedWorker").LifestyleTransient());

// Create the ActorSystem and Dependency Resolver
var system = ActorSystem.Create("MySystem");
var propsResolver = new WindsorDependencyResolver(container, system);
```

* **Ninject**

In order to use this plugin, install the Nuget package with `Install-Package Akka.DI.Ninject`, then follow the instructions:

```csharp
// Create and build your container
var container = new Ninject.StandardKernel();
container.Bind<TypedWorker>().To(typeof(TypedWorker));
container.Bind<IWorkerService>()To(typeof)WorkerService));

// Create the ActorSystem and Dependency Resolver
var system = ActorSystem.Create("MySystem");
var propsResolver = new NinjectDependencyResolver(container,system);
``` 

Support for additional dependency injection frameworks may be added in the future, but you can easily implement your own by implementing an [Actor Producer Extension](https://github.com/akkadotnet/akka.net/tree/dev/src/contrib/dependencyInjection/Akka.DI.Core).

**Warning**

You might be tempted at times to use an IndirectActorProducer which always returns the same instance, e.g. by using a static field. This is not supported, as it goes against the meaning of an actor restart, which is described here: [[What Restarting Means]].

When using a dependency injection framework, actor MUST NOT have singleton scope.


Techniques for dependency injection and integration with dependency injection frameworks are described in more depth in the [Using Akka with Dependency Injection](http://letitcrash.com/post/55958814293/akka-dependency-injection) guideline.