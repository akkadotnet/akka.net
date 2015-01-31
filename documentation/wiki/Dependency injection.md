---
layout: wiki
title: Dependency injection
---
### Dependency Injection
If your UntypedActor has a constructor that takes parameters then those need to be part of the Props as well, as described above. But there are cases when a factory method must be used, for example when the actual constructor arguments are determined by a dependency injection framework.
```chsarp
import akka.actor.Actor;
import akka.actor.IndirectActorProducer;
class DependencyInjector implements IndirectActorProducer {
  final Object applicationContext;
  final String beanName;
  
  public DependencyInjector(Object applicationContext, String beanName) {
    this.applicationContext = applicationContext;
    this.beanName = beanName;
  }
  
  @Override
  public Class<? extends Actor> actorClass() {
    return MyActor.class;
  }
  
  @Override
  public MyActor produce() {
    MyActor result;
    // obtain fresh Actor instance from DI framework ...
    return result;
  }
}
  
  final ActorRef myActor = getContext().actorOf(
    Props.create(DependencyInjector.class, applicationContext, "MyActor"),
      "myactor3");
```
>**Warning**<br/>
You might be tempted at times to offer an IndirectActorProducer which always returns the same instance, e.g. by using a static field. This is not supported, as it goes against the meaning of an actor restart, which is described here: What Restarting Means.

When using a dependency injection framework, actor beans MUST NOT have singleton scope.

Techniques for dependency injection and integration with dependency injection frameworks are described in more depth in the Using Akka with Dependency Injection guideline and the Akka Java Spring tutorial in Typesafe Activator.
