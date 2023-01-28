---
uid: Akka.Actor.ILogReceive
summary: Implement this interface in order to enable Receive logging 
---
ReceiveActor inherits from a base class: ActorCell.  All Receive calls go through this following method in ActorCell: 

```c#
        protected virtual void ReceiveMessage(object message)
        {
            var wasHandled = _actor.AroundReceive(_state.GetCurrentBehavior(), message);

            if (System.Settings.AddLoggingReceive && _actor is ILogReceive)
            {
                var msg = "received " + (wasHandled ? "handled" : "unhandled") + " message " + message + " from " + Sender.Path;
                Publish(new Debug(Self.Path.ToString(), _actor.GetType(), msg));
            }
        }
```
As can be seen in the above code, in order to enable Receive logging, you must implement ILogReceive on your actor class.  

