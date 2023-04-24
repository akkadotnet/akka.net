---
uid: event-bus
title: Event Bus
---
# EventBus

EventBus provides several types of custom messages to subscribe to:

1. DeadLetter - messages that are not delivered to actor
2. UnhandledMessage - messages which actor receives and doesn't understand
3. SuppressedDeadLetter - similar to DeadLetter with the slight twist of NOT being logged by the default dead letters listener
4. Dropped - messages dropped due to overfull queues or routers with no routees
5. AllDeadLetters - shortcut for all types of beforementioned messages

## Subscribing to Dead Letter Messages

The following example demonstrates the capturing of dead letter messages generated from a stopped actor.  The dedicated actor will output the message, sender and recipient of the captured dead letter to the console.

```csharp
void Main()
{
    // Setup the actor system
    ActorSystem system = ActorSystem.Create("MySystem");

    // Setup an actor that will handle deadletter type messages
    var deadletterWatchMonitorProps = Props.Create(() => new DeadletterMonitor());
    var deadletterWatchActorRef = system.ActorOf(deadletterWatchMonitorProps, "DeadLetterMonitoringActor");
    
    // subscribe to the event stream for messages of type "DeadLetter"
    system.EventStream.Subscribe(deadletterWatchActorRef, typeof(DeadLetter));    
    
    // Setup an actor which will simulate a failure/shutdown
    var expendableActorProps = Props.Create(() => new ExpendableActor());
    var expendableActorRef = system.ActorOf(expendableActorProps, "ExpendableActor");
    
    // simulate the expendable actor failing/stopping
    expendableActorRef.Tell(Akka.Actor.PoisonPill.Instance);
    
    // try sending a message to the stopped actor
    expendableActorRef.Tell("another message");

}

// A dead letter handling actor specifically for messages of type "DeadLetter"
public class DeadletterMonitor : ReceiveActor
{
    
    public DeadletterMonitor()
    {
        Receive<DeadLetter>(dl => HandleDeadletter(dl));
    }
    
    private void HandleDeadletter(DeadLetter dl)
    {
        Console.WriteLine($"DeadLetter captured: {dl.Message}, sender: {dl.Sender}, recipient: {dl.Recipient}");
    }
}

// An expendable actor which will simulate a failure
public class ExpendableActor : ReceiveActor {  }
```

sample capture

```string
DeadLetter captured: another message, sender: [akka://MySystem/deadLetters], recipient: [akka://MySystem/user/ExpendableActor#1469246785]
```

## Subscribing to Unhandled Messages

The following example demonstrates the capturing of unhandled messages. The dedicated actor will output captured unhandled message to the console.

```csharp
public class DumbActor : ReceiveActor {  }

public class UnhandledMessagesMonitorActor : ReceiveActor
{
    public UnhandledMessagesMonitorActor()
    {
        Receive<UnhandledMessage>(Console.WriteLine);
    }
}

using (var system = ActorSystem.Create("MySystem"))
{
    var dumbActor = system.ActorOf<DumbActor>();
    var monitorActor = system.ActorOf<UnhandledMessagesMonitorActor>();
    
    // Subscribe to messages of type UnhandledMessage
    system.EventStream.Subscribe(monitorActor, typeof(UnhandledMessage));
    
    // try sending a message to actor which it doesn't understand
    dumbActor.Tell("Hello");
    
    Console.ReadLine();
}
```

sample capture

```string
DeadLetter from [akka://MySystem/deadLetters] to [akka://MySystem/user/$a#965879198]: <Hello>
```

## Subscribing to AllDeadLetters Messages

The following example demonstrates the capturing of all unhandled messages types. The dedicated actor will output captured unhandled messages to the console.

```csharp
public class DumbActor : ReceiveActor {  }

public class AllDeadLettersMessagesMonitorActor : ReceiveActor
{
    public AllDeadLettersMessagesMonitorActor()
    {
        Receive<AllDeadLetters>(Console.WriteLine);
    }
}

using (var system = ActorSystem.Create("MySystem"))
{
    var dumbActor = system.ActorOf<DumbActor>();
    var monitorActor = system.ActorOf<AllDeadLettersMessagesMonitorActor>();
    
    // Subscribe to messages of type AllDeadLetters
    system.EventStream.Subscribe(monitorActor, typeof(AllDeadLetters));
    
    // try sending a message to actor which it doesn't understand
    dumbActor.Tell("Hello");
    
    // simulate the actor failing/stopping
    dumbActor.Tell(Akka.Actor.PoisonPill.Instance);
    
    // try sending a message to the stopped actor
    dumbActor.Tell("world");
    
    Console.ReadLine();
}
```

sample capture

```string
DeadLetter from [akka://MySystem/deadLetters] to [akka://MySystem/user/$a#1479030912]: <Hello>
DeadLetter from [akka://MySystem/deadLetters] to [akka://MySystem/user/$a#1479030912]: <world>
```

## Subscribing to Messages of Type `string`

```csharp
var system = ActorSystem.Create("MySystem");
var subscriber = system.ActorOf<SomeActor>();
//Subscribe to messages of type string
system.EventStream.Subscribe(subscriber,typeof(string));
//send a message
system.EventStream.Publish("hello"); //this will be forwarded to subscriber
```

### Further Reading

* Read more on <http://doc.akka.io/docs/akka/snapshot/java/event-bus.html>
