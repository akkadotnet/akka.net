---
uid: fault-tolerance
title: Fault tolerance
---
# Fault Tolerance

As explained in [Actor Systems](xref:actor-systems), each actor is the supervisor of its
children, and as such each actor defines a fault handling supervisor strategy.
This strategy cannot be changed after a child actor is created.

## Fault Handling in Practice

Let's set up an example strategy which will handle data store errors in a child actor. In this sample we use a best effort re-connect approach.

## Creating a Supervisor Strategy

```csharp
protected override SupervisorStrategy SupervisorStrategy()
{
    return new OneForOneStrategy(
        maxNrOfRetries: 10,
        withinTimeRange: TimeSpan.FromMinutes(1),
        localOnlyDecider: ex =>
        {
            switch (ex)
            {
                case ArithmeticException ae:
                    return Directive.Resume;
                case NullReferenceException nre:
                    return Directive.Restart;
                case ArgumentException are:
                    return Directive.Stop;
                default:
                    return Directive.Escalate;
            }
        });
}
```

We will handle a few exception types to demonstrate some fault handling directives described in [Supervision and Monitoring](xref:supervision). This strategy is "one-for-one", meaning that each child is treated separately. The alternative is an "all-for-one" strategy, where a decision is applied to _all_ children of the supervisor, not only the failing one. We have chosen to set a limit of maximum 10 restarts per minute; The child actor is stopped if the limit is exceeded. We could have chosen to leave this argument out, which would have created a strategy where the child actor would restart indefinitely.

> [!NOTE]
> If the strategy is declared inside the supervising actor (as opposed to
within a companion object) its decider has access to all internal state of
the actor in a thread-safe fashion, including obtaining a reference to the
currently failed child (available as the `Sender` of the failure message).

### Default Supervisor Strategy

When the supervisor strategy is not defined for an actor the following
exceptions are handled by default:

* `ActorInitializationException` will stop the failing child actor;
* `ActorKilledException` will stop the failing child actor; and
* Any other type of `Exception` will restart the failing child actor.

You can combine your own strategy with the default strategy like this:

```csharp
protected override SupervisorStrategy SupervisorStrategy()
{
    return new OneForOneStrategy(
        maxNrOfRetries: 10,
        withinTimeRange: TimeSpan.FromMinutes(1),
        localOnlyDecider: ex =>
        {
            if (ex is ArithmeticException)
            {
                return Directive.Resume;
            }

            return Akka.Actor.SupervisorStrategy.DefaultStrategy.Decider.Decide(ex);
        });
}
```

### Stopping Supervisor Strategy

An alternative which is closer to the Erlang way is to stop children when they fail
and then take corrective action in the supervisor when DeathWatch signals the
loss of the child. This strategy is also provided pre-packaged as
`SupervisorStrategy.StoppingStrategy` with an accompanying
`StoppingSupervisorStrategy` configurator to be used when you want the
`"/user"` guardian to apply it.

### Logging of Actor Failures

The default strategy logs failures unless they are escalated. You can mute the default logging of a `SupervisorStrategy` by setting
`loggingEnabled` to `false` when instantiating it. Customized logging
can be done inside the `Decider`. Note that the reference to the currently
failed child is available as the `Sender` when the `SupervisorStrategy` is
declared inside the supervising actor.

You can also customize the logging in your own ``SupervisorStrategy`` implementation
by overriding the `logFailure` method.

## Supervision of Top-Level Actors

Top-level actors means those which are created using `system.ActorOf()`, and
they are children of the [User Guardian](xref:supervision#user-the-guardian-actor). There are no
special rules applied in this case, the guardian simply applies the configured
strategy.

## Test Application

Consider this custom `SupervisorStrategy`:

```csharp
public class Supervisor : UntypedActor
{
    protected override SupervisorStrategy SupervisorStrategy()
    {
        return new OneForOneStrategy(
            maxNrOfRetries: 10,
            withinTimeRange: TimeSpan.FromMinutes(1),
            localOnlyDecider: ex =>
            {
                switch (ex)
                {
                    case ArithmeticException ae:
                        return Directive.Resume;
                    case NullReferenceException nre:
                        return Directive.Restart;
                    case ArgumentException are:
                        return Directive.Stop;
                    default:
                        return Directive.Escalate;
                }
            });
    }

    protected override void OnReceive(object message)
    {
        if (message is Props p)
        {
            var child = Context.ActorOf(p); // create child
            Sender.Tell(child); // send back reference to child actor
        }
    }
}
```

This supervisor will be used to create a child actor:

```csharp
public class Child : UntypedActor
{
    private int state = 0;

    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case Exception ex:
                throw ex;
                break;
            case int x:
                state = x;
                break;
            case "get":
                Sender.Tell(state);
                break;
        }
    }
}
```

We'll use the utilities in [Akka-Testkit](xref:testing-actor-systems) to help us describe and test the expected behavior.

First, we'll create actors:

```csharp
var supervisor = system.ActorOf<Supervisor>("supervisor");

supervisor.Tell(Props.Create<Child>());
var child = ExpectMsg<IActorRef>(); // retrieve answer from TestKit’s TestActor
```

Our first test will demonstrate `Directive.Resume`, so we set some non-initial state in the child actor and cause it to fail:

```csharp
child.Tell(42); // set state to 42
child.Tell("get");
ExpectMsg(42);

child.Tell(new ArithmeticException()); // crash it
child.Tell("get");
ExpectMsg(42);
```

As you can see the value 42 survives the fault handling directive because we're using the `Resume` directive, which does not cause the actor to restart.

If we change the failure to a more serious `NullReferenceException`, which we defined above to result in a `Restart` directive, that will no longer be the case:

```csharp
child.Tell(new NullReferenceException());
child.Tell("get");
ExpectMsg(0);
```

This is because the actor has restarted and the original `Child` actor instance that was processing messages will be destroyed and replaced by a brand-new instance defined using the same `Props`.

And finally in case of the fatal `ArgumentException`, our strategy will return a stop directive, and the child will be terminated by the supervisor:

```csharp
Watch(child); // have testActor watch "child"
child.Tell(new ArgumentException()); // break it
ExpectMsg<Terminated>().ActorRef.Should().Be(child);
```

Up to now the supervisor was completely unaffected by the child's failure, because the directives in our strategy handled the exception. However, if we cause an `Exception`, none of our handlers are invoked and the supervisor escalates the failure.

```csharp
supervisor.Tell(Props.Create<Child>()); // create new child
var child2 = ExpectMsg<IActorRef>();
Watch(child2);
child2.Tell("get"); // verify it is alive
ExpectMsg(0);

child2.Tell(new Exception("CRASH"));
var message = ExpectMsg<Terminated>();
message.ActorRef.Should().Be(child2);
message.ExistenceConfirmed.Should().BeTrue();
```

The supervisor itself is supervised by the top-level actor provided by the
`ActorSystem`. This has the default policy to restart as a result of all
`Exception`s except `ActorInitializationException` and `ActorKilledException`. Since the
default directive in case of a restart is to kill all children, our poor
child did not survive this failure.

If we don't want our children to be restarted we can override `PreRestart` in the Supervisor:

```csharp
public class Supervisor2 : UntypedActor
{
    protected override SupervisorStrategy SupervisorStrategy()
    {
        return new OneForOneStrategy(
            maxNrOfRetries: 10,
            withinTimeRange: TimeSpan.FromMinutes(1),
            localOnlyDecider: ex =>
            {
                switch (ex)
                {
                    case ArithmeticException ae:
                        return Directive.Resume;
                    case NullReferenceException nre:
                        return Directive.Restart;
                    case ArgumentException are:
                        return Directive.Stop;
                    default:
                        return Directive.Escalate;
                }
            });
    }

    protected override void PreRestart(Exception reason, object message)
    {
    }

    protected override void OnReceive(object message)
    {
        if (message is Props p)
        {
            var child = Context.ActorOf(p); // create child
            Sender.Tell(child); // send back reference to child actor
        }
    }
}
```

With this parent, the child survives the escalated restart, as demonstrated in
this last test:

```csharp
var supervisor2 = system.ActorOf<Supervisor2>("supervisor2");

supervisor2.Tell(Props.Create<Child>());
var child3 = ExpectMsg<IActorRef>();

child3.Tell(23);
child3.Tell("get");
ExpectMsg(23);

child3.Tell(new Exception("CRASH"));
child3.Tell("get");
ExpectMsg(0);
```
