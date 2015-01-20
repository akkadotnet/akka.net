## Receive timeout
The `IActorContext` `SetReceiveTimeout` defines the inactivity timeout after which the sending of a `ReceiveTimeout` message is triggered. When specified, the receive function should be able to handle an `Akka.Actor.ReceiveTimeout` message.

>**Note**<br/>
>Please note that the receive timeout might fire and enqueue the ReceiveTimeout message right after another message was enqueued; hence it is not guaranteed that upon reception of the receive timeout there must have been an idle period beforehand as configured via this method.

Once set, the receive timeout stays in effect (i.e. continues firing repeatedly after inactivity periods). Pass in `null` to `SetReceiveTimeout` to switch off this feature.

```csharp
public class MyActor : ReceiveActor
{
    public MyActor()
    {
          //trigger a receive timeout after 3 seconds of message silence.
          SetReceiveTimeout(Timespan.FromSeconds(3));
          //your own message handlers
          Receive<MyMessage>(message => Console.WriteLine("I am working.."));
          //do something with the timeout message
          Receive<ReceiveTimeout>(timeout => Console.WriteLine("I have timed out"));
    }
}
```