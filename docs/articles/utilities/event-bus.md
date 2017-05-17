---
uid: event-bus
title: Event Bus
---
# EventBus

## Subscribing to Dead letter messages

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

## Subscribing to messages of type "string"

```csharp
var system = ActorSystem.Create("MySystem");
var subscriber = system.ActorOf<SomeActor>();
//Subscribe to messages of type string
system.EventStream.Subscribe(subscriber,typeof(string));
//send a message
system.EventStream.Publish("hello"); //this will be forwarded to subscriber
```

### Further reading
* Read more on http://doc.akka.io/docs/akka/snapshot/java/event-bus.html