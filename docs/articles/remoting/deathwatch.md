---
uid: remote-deathwatch
title: Detecting & Handling Network Failures with Remote DeathWatch
---

# Detecting and Handling Network Failures with Remote DeathWatch
Programmers often fall victim to the [fallacies of distributed computing](http://blog.fogcreek.com/eight-fallacies-of-distributed-computing-tech-talk/) when building networked applications, so in this section we want to spend some time address both the realities of network programming AND how to deal with those realities powerfully with Akka.Remote.

Akka.Remote gives us the ability to handle and respond to network failures by extending [Actor life cycle monitoring (DeathWatch)](xref:receive-actor-api#lifecycle-monitoring-aka-deathwatch) to work across the network.

## Syntax
Setting up DeathWatch is done the same way locally as it's done over the network (because, [Location Transparency](xref:location-transparency).)

```csharp
public class MyActor : ReceiveActor{
    public MyActor(){
        Receive<Foo>(x => {
            // notify us if the actor who sent
            // this message dies in the future.
            Context.Watch(Sender);
        });

        // message we'll receive anyone we DeathWatch
        // dies, OR if the network terminates
        Receive<Terminated>(t => {
            // ...
        });
    }
}
```

[`Context.Watch`](http://api.getakka.net/docs/stable/html/716F6CCE.htm) is how we establish a DeathWatch for an actor, even one that exists on a remote process!

`Context.Watch` is how we create the DeathWatch subscription, and the [`Terminated`](http://api.getakka.net/docs/stable/html/6853A61F.htm) message is what we receive in the event the actor we're  watching does die.

## When Will You Receive a `Terminated` Message?
Under Akka.Remote, you will receive a `Terminated` message from remote DeathWatch under the following two circumstances:

1. **The remote actor was gracefully terminated**, meaning that the actor or an actor above it on the hierarchy was explicitly terminated. The network is still healthy.
2. **The association between the two remote actor systems has disassociated**; this can mean that there was a network failure or that the remote process crashed. In either case, we assume that the remote actor is dead and receive a `Terminated` message.

### Different Types of `DeathWatch` Notifications
One important piece of data that [the `Terminated` message](http://api.getakka.net/docs/stable/html/6853A61F.htm "Akka.NET API Docs - Terminated Class") gives is you some explanation as to WHY the actor you were watching has died.

```csharp
public sealed class Terminated : IAutoReceivedMessage, IPossiblyHarmful
{
    public Terminated(IActorRef actorRef, bool existenceConfirmed, bool addressTerminated)
    {
        ActorRef = actorRef;
        ExistenceConfirmed = existenceConfirmed;
        AddressTerminated = addressTerminated;
    }

    public IActorRef ActorRef { get; private set; }


    public bool AddressTerminated { get; private set; }

    public bool ExistenceConfirmed { get; private set; }

    public override string ToString()
    {
        return "<Terminated>: " + ActorRef + " - ExistenceConfirmed=" + ExistenceConfirmed;
    }
}
```

The `Terminated.AddressTerminated` property returns **true** if the reason why we're receiving this death watch notification is because the association between our current `ActorSystem` and the remote `ActorSystem` that `Terminated.ActorRef` lived on failed.