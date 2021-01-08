---
uid: fault-tolerance
title: Fault tolerance
---
# Fault Tolerance

As explained in [Actor Systems](xref:actor-systems) each actor is the supervisor of its
children, and as such each actor defines fault handling supervisor strategy.
This strategy cannot be changed afterwards as it is an integral part of the
actor system's structure.

## Fault Handling in Practice

First, let us look at a sample that illustrates one way to handle data store errors,
which is a typical source of failure in real world applications. Of course it depends
on the actual application what is possible to do when the data store is unavailable,
but in this sample we use a best effort re-connect approach.

## Creating a Supervisor Strategy

The following sections explain the fault handling mechanism and alternatives
in more depth.

For the sake of demonstration let us consider the following strategy:

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

I have chosen a few well-known exception types in order to demonstrate the application of the fault handling directives described in [Supervision and Monitoring](xref:supervision). First off, it is a one-for-one strategy, meaning that each child is treated separately (an all-for-one strategy works very similarly, the only difference is that any decision is applied to all children of the supervisor, not only the failing one). There are limits set on the restart frequency, namely maximum 10 restarts per minute; each of these settings could be left out, which means that the respective limit does not apply, leaving the possibility to specify an absolute upper limit on the restarts or to make the restarts work infinitely. The child actor is stopped if the limit is exceeded.

This is the piece which maps child failure types to their corresponding directives.

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

If the exception escalate all the way up to the root guardian it will handle it
in the same way as the default strategy defined above.

You can combine your own strategy with the default strategy:

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

Closer to the Erlang way is the strategy to just stop children when they fail
and then take corrective action in the supervisor when DeathWatch signals the
loss of the child. This strategy is also provided pre-packaged as
`SupervisorStrategy.StoppingStrategy` with an accompanying
`StoppingSupervisorStrategy` configurator to be used when you want the
`"/user"` guardian to apply it.

### Logging of Actor Failures

By default the `SupervisorStrategy` logs failures unless they are escalated.
Escalated failures are supposed to be handled, and potentially logged, at a level
higher in the hierarchy.

You can mute the default logging of a `SupervisorStrategy` by setting
`loggingEnabled` to `false` when instantiating it. Customized logging
can be done inside the `Decider`. Note that the reference to the currently
failed child is available as the `Sender` when the `SupervisorStrategy` is
declared inside the supervising actor.

You may also customize the logging in your own ``SupervisorStrategy`` implementation
by overriding the `logFailure` method.

## Supervision of Top-Level Actors

Top-level actors means those which are created using `system.ActorOf()`, and
they are children of the [User Guardian](xref:supervision#user-the-guardian-actor). There are no
special rules applied in this case, the guardian simply applies the configured
strategy.

## Test Application

The following section shows the effects of the different directives in practice,
wherefor a test setup is needed. First off, we need a suitable supervisor:

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

This supervisor will be used to create a child, with which we can experiment:

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

The test is easier by using the utilities described in [Akka-Testkit](xref:testing-actor-systems).

Let us create actors:

```csharp
var supervisor = system.ActorOf<Supervisor>("supervisor");

supervisor.Tell(Props.Create<Child>());
var child = ExpectMsg<IActorRef>(); // retrieve answer from TestKit’s TestActor
```

The first test shall demonstrate the `Resume` directive, so we try it out by
setting some non-initial state in the actor and have it fail:

```csharp
child.Tell(42); // set state to 42
child.Tell("get");
ExpectMsg(42);

child.Tell(new ArithmeticException()); // crash it
child.Tell("get");
ExpectMsg(42);
```

As you can see the value 42 survives the fault handling directive because we're using the `Resume` directive, which does not cause the actor to restart. Now, if we
change the failure to a more serious `NullReferenceException`, that will no
longer be the case:

```csharp
child.Tell(new NullReferenceException());
child.Tell("get");
ExpectMsg(0);
```

This is because the actor has restarted and the original `Child` actor instance that was processing messages will be destroyed and replaced by a brand-new instance defined using the original `Props` passed to its parent.

And finally in case of the fatal `IllegalArgumentException` the child will be
terminated by the supervisor:

```csharp
Watch(child); // have testActor watch "child"
child.Tell(new ArgumentException()); // break it
ExpectMsg<Terminated>().ActorRef.Should().Be(child);
```

Up to now the supervisor was completely unaffected by the child's failure,
because the directives set did handle it. In case of an `Exception`, this is not
true anymore and the supervisor escalates the failure.

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
`ActorSystem`, which has the default policy to restart in case of all
`Exception` cases (with the notable exceptions of
`ActorInitializationException` and `ActorKilledException`). Since the
default directive in case of a restart is to kill all children, we expected our poor
child not to survive this failure.

In case this is not desired (which depends on the use case), we need to use a
different supervisor which overrides this behavior.

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
the last test:

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