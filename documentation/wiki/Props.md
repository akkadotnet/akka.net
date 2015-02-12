---
layout: wiki
title: Props
---
## Props

Props is a configuration class to specify options for the creation of actors, think of it as an immutable and thus freely shareable recipe for creating an actor including associated deployment information (e.g. which dispatcher to use, see more below). Here are some examples of how to create a Props instance.
```csharp
  Props props1 = Props.Create(typeof(MyActor));
  Props props2 = Props.Create(() => new MyActor(..), "...");
  Props props3 = Props.Create<MyActor>();
```

>**Warning**<br/>
In the second line, using `new`; Do note that the arguments passed in will be evaluated *once*, when the `Props` are created. 
This is because the arguments can be passed to remote systems for [remote deployment](Remote deploy).
The behavior is somewhat odd since a lambda containing a `new` statement generally evaluate the entire expression every time it is invoked.

### Recommended Practices
It is a good idea to provide static factory methods on the UntypedActor which help keeping the creation of suitable Props as close to the actor definition as possible. This also allows usage of the Creator-based methods which statically verify that the used constructor actually exists instead relying only on a runtime check.
```csharp
public class DemoActor : ReceiveActor {
  
  /**
   * Create Props for an actor of this type.
   * @param magicNumber The magic number to be passed to this actor’s constructor.
   * @return a Props for creating this actor, which can then be further configured
   *         (e.g. calling `.withDispatcher()` on it)
   */
  public static Props Props(int magicNumber) 
  {
    return Props.Create(() => new DemoActor(magicNumber));
  }
  
  private int magicNumber;
 
  public DemoActor(int magicNumber) 
  {
    this.magicNumber = magicNumber;
    ...some receive declarations here
    //Receive<string>(str => ..);
  }
}
 
system.ActorOf(DemoActor.Props(42), "demo");
```
### Creating Actors with Props
Actors are created by passing a Props instance into the `ActorOf` factory method which is available on `ActorSystem` and `ActorContext`.

```csharp
// ActorSystem is a heavy object: create only one per application
ActorSystem system = ActorSystem.Create("MySystem");
ActorRef myActor = system.ActorOf(Props.Create(typeof(MyActor)),
  "myactor");
```
Using the ActorSystem will create top-level actors, supervised by the actor system’s provided guardian actor, while using an actor’s context will create a child actor.

```csharp
class A : ReceiveActor 
{
  ActorRef child =
      Context.ActorOf(Props.Create(typeof(MyActor)), "myChild");
  // plus some behavior ...
}
```
It is recommended to create a hierarchy of children, grand-children and so on such that it fits the logical failure-handling structure of the application, see [[Actor Systems]].

The call to `ActorOf` returns an instance of `ActorRef`. This is a handle to the actor instance and the only way to interact with it. The `ActorRef` is immutable and has a one to one relationship with the Actor it represents. The `ActorRef` is also serializable and network-aware. This means that you can serialize it, send it over the wire and use it on a remote host and it will still be representing the same Actor on the original node, across the network.

The name parameter is optional, but you should preferably name your actors, since that is used in log messages and for identifying actors. The name must not be empty or start with $, but it may contain URL encoded characters (eg. `%20` for a blank space). If the given name is already in use by another child to the same parent an `InvalidActorNameException` is thrown.

Actors are automatically started asynchronously when created.
