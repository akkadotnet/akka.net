Finite State Machine

For more info see real Akka FSM documentation: http://doc.akka.io/docs/akka/snapshot/scala/fsm.html

```csharp
public class MyFSM : FSM<int, object>
{
    public MyFSM(ActorRef target)
    {
        Target = target;
        StartWith(0, new object());
        When(0, @event =>
        {
            if (@event.FsmEvent.Equals("tick")) return GoTo(1);
            return null;
        });

        When(1, @event =>
        {
            if (@event.FsmEvent.Equals("tick")) return GoTo(0);
            return null;
        });

        WhenUnhandled(@event =>
        {
            if (@event.FsmEvent.Equals("reply")) return Stay().Replying("reply");
            return null;
        });

        Initialize();
    }

    public ActorRef Target { get; private set; }

    protected override void PreRestart(Exception reason, object message)
    {
        Target.Tell("restarted");
    }
}
```