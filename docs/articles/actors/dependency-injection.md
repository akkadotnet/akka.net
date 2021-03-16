---
uid: dependency-injection
title: Dependency injection
---
# Dependency Injection
As of Akka.NET v1.4.15 we recommend to Akka.NET users adopt the Akka.DependencyInjection library, which integrates directly with Microsoft.Extensions.DependencyInjection and deprecates the previous Akka.DI.Core and Akka.DI.* libraries.

You can install Akka.DependencyInjection via NuGet:

```
PS> Install-Package Akka.DependencyInjection
```

Akka.DependencyInjection allows users to pass in an [`IServiceProvider`](https://docs.microsoft.com/en-us/dotnet/api/system.iserviceprovider) into the `ActorSystem` before the latter is created, via [a new kind of programmatic configuration `Setup` that was introduced in Akka.NET v1.4](xref:configuration#programmatic-configuration-with-setup)

## Integrating with Microsoft.Extensions.DependencyInjection
Many .NET applications begin with a `Startup` class that uses the Microsoft.Extensions.DependencyInjection to build an `IServiceCollection` that contains 1 or more service bindings:

[!code-csharp[Startup](../../../src/examples/AspNetCore/Samples.Akka.AspNetCore/Startup.cs?name=DiSetup)]

In this instance, we're going to run Akka.NET behind the scenes as a [`Microsoft.Extensions.Hosting.IHostedService`](https://docs.microsoft.com/en-us/dotnet/api/microsoft.extensions.hosting.ihostedservice) and in the process, we will pass the `IServiceProvider` that contains our DI bindings to it:

[!code-csharp[AkkaServiceSetup](../../../src/examples/AspNetCore/Samples.Akka.AspNetCore/Actors/AkkaService.cs?name=AkkaServiceSetup)]

To incorporate our `IServiceProvider` into our `ActorSystem`, we will use a `Akka.DependencyInjection.ServiceProviderSetup` class to combine the `IServiceProvider` with the HOCON configuration we're going to use.

Once the `ActorSystem` is created, we can easily access the `IServiceProvider` by calling `ServiceProvider.For(ActorSystem)`:

[!code-csharp[ServiceProviderFor](../../../src/examples/AspNetCore/Samples.Akka.AspNetCore/Actors/AkkaService.cs?name=ServiceProviderFor)]

From there, we want to call `ServiceProvider.Props` to create a set of `Props` for our actor, whose arguments can be populated via the services registered with the `IServiceProvider`:

[!code-csharp[HasherActor](../../../src/examples/AspNetCore/Samples.Akka.AspNetCore/Actors/HasherActor.cs?name=HasherActor)]

> [!NOTE]
> Akka.DependencyInjection is not going to manage the lifecycle of your dependencies for you. Keep reading.

### Managing Lifecycle Dependencies with Akka.DependencyInjection
Akka.DependencyInjection allows Akka.NET developers to mix and match injected dependencies along with non-injected dependencies - for instance:

[!code-csharp[NonDiActor](../../../src/contrib/dependencyinjection/Akka.DependencyInjection.Tests/ActorServiceProviderPropsWithScopesSpecs.cs?name=NonDiArgsActor)]

In this case, the actor accepts:

1. A singleton scoped service, injected directly via the constructor;
2. A `IServiceProvider` instance, which is also populated directly via the DI system; and
3. Two `string` arguments that _are not_ provided via dependency injection.

Here's how Akka.DependencyInjection is used to instantiate this actor via `Props`:

[!code-csharp[NonDiActor](../../../src/contrib/dependencyinjection/Akka.DependencyInjection.Tests/ActorServiceProviderPropsWithScopesSpecs.cs?name=CreateNonDiActor)]

The `ServiceProvider.Props` method will accept additional arguments that can be used to instantiate the actor in addition to the arguments that will be provided via your depedency injection container.

Akka.DependencyInjection does not manage the lifecycle of your dependencies automatically - in fact, Akka.NET recommends that you follow [Microsoft's own dependency injection guidelines](https://docs.microsoft.com/en-us/dotnet/core/extensions/dependency-injection-guidelines):

> Use the factory pattern to create an instance outside of the parent scope. In this situation, the app would generally have a Create method that calls the final type's constructor directly. If the final type has other dependencies, the factory can:
> * Receive an `IServiceProvider` in its constructor.
> * Use `ActivatorUtilities.CreateInstance` to instantiate the instance outside of the container, while using the container for its dependencies.

A best practice for working with Akka.NET actors and Microsoft.Extensions.DependencyInjection:

1. Always provide an `IServiceProvider` into the constructor of your actors, to use Microsoft's so-called "factory" pattern;
2. Always use the `IServiceProvider` to create an `IServiceScope`;
3. Use the `IServiceScope` to create your dependencies; and
4. Dispose your `IServiceScope` inside the `PostStop` method of your actor to ensure orderly disposal of any transient or scoped dependencies your actor may have used.

You can see an example of an actor that follows this pattern below:

[!code-csharp[MixedActor](../../../src/contrib/dependencyinjection/Akka.DependencyInjection.Tests/ActorServiceProviderPropsWithScopesSpecs.cs?name=MixedActor)]

## Akka.DI - Deprecated Akka.NET DI Support

> [!WARNING]
> As of [Akka.NET v1.4.15](https://github.com/akkadotnet/akka.net/releases/tag/1.4.15), Akka.DI.Core and all of the libraries that implement it are deprecated. Going forward Akka.NET users are encouraged to use the [Akka.DependencyInjection library](xref:dependency-injection) instead, which uses the Microsoft.Extensions.DependencyInjection interfaces to integration DI directly into your Akka.NET actors.

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

### Creating Child Actors using DI
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

### Notes

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

#### AutoFac

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

#### CastleWindsor

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

#### Ninject

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

#### Other frameworks

Support for additional dependency injection frameworks may be added in the
future, but you can easily implement your own by implementing an
[Actor Producer Extension](xref:di-core).
