---
uid: fsm
title: FSM
---
# Finite State Machine

## A Simple Example

To demonstrate most of the features of the `FSM` class, consider an actor which shall receive and queue messages while they arrive in a burst and send them on after the burst ended or a flush request is received.

First, consider all of the below to use these import statements:
```csharp
using Akka.Actor;
using Akka.Event;
```

The contract of our `Buncher` actor is that it accepts or produces the following messages:

[!code-csharp[Main](../../../src/core/Akka.Docs.Tests/Actors/FiniteStateMachine/FiniteStateMachine.Messages.cs?name=FSMEvents)]

`SetTarget` is needed for starting it up, setting the destination for the Batches to be passed on; `Queue` will add to the internal queue while `Flush` will mark the end of a burst.

[!code-csharp[Main](../../../src/core/Akka.Docs.Tests/Actors/FiniteStateMachine/FiniteStateMachine.Messages.cs?name=FSMData)]

The actor can be in two states: no message queued (aka `Idle`) or some message queued (aka `Active`). It will stay in the active state as long as messages keep arriving and no flush is requested. The internal state data of the actor is made up of the target actor reference to send the batches to and the actual queue of messages.

[!code-csharp[Main](../../../src/core/Akka.Docs.Tests/Actors/FiniteStateMachine/ExampleFSMActor.cs?name=FSMActorStart)]
[!code-csharp[Main](../../../src/core/Akka.Docs.Tests/Actors/FiniteStateMachine/ExampleFSMActor.cs?name=FSMActorEnd)]

The basic strategy is to declare the actor, inherit from `FSM` class and specifying the possible states and data values as type parameters. Within the body of the actor a DSL is used for declaring the state machine:

- `StartWith` defines the initial state and initial data
- then there is one `When(<state>, () => {})` declaration per state to be handled
- finally starting it up using initialize, which performs the transition into the initial state and sets up timers (if required).

In this case, we start out in the `Idle` and `Uninitialized` state, where only the `SetTarget()` message is handled; stay prepares to end this event’s processing for not leaving the current state, while the using modifier makes the `FSM` replace the internal state (which is `Uninitialized` at this point) with a fresh `Todo()` object containing the target actor reference. The `Active` state has a state timeout declared, which means that if no message is received for 1 second, a `FSM.StateTimeout` message will be generated. This has the same effect as receiving the `Flush` command in this case, namely to transition back into the Idle state and resetting the internal queue to the empty vector. But how do messages get queued? Since this shall work identically in both states, we make use of the fact that any event which is not handled by the `When()` block is passed to the `WhenUnhandled()` block:

[!code-csharp[Main](../../../src/core/Akka.Docs.Tests/Actors/FiniteStateMachine/ExampleFSMActor.cs?name=UnhandledHandler)]

The first case handled here is adding `Queue()` requests to the internal queue and going to the `Active` state (this does the obvious thing of staying in the `Active` state if already there), but only if the `FSM` data are not `Uninitialized` when the `Queue()` event is received. Otherwise—and in all other non-handled cases—the second case just logs a warning and does not change the internal state.

The only missing piece is where the `Batches` are actually sent to the target, for which we use the `OnTransition` mechanism: you can declare multiple such blocks and all of them will be tried for matching behavior in case a state transition occurs (i.e. only when the state actually changes).

[!code-csharp[Main](../../../src/core/Akka.Docs.Tests/Actors/FiniteStateMachine/ExampleFSMActor.cs?name=TransitionHandler)]

The transition callback is a function which takes as input a pair of states—the current and the next state. The `FSM` class includes a convenience extractor for these in form of an arrow operator, which conveniently reminds you of the direction of the state change which is being matched. During the state change, the old state data is available via `StateData` as shown, and the new state data would be available as `NextStateData`.

To verify that this buncher actually works, it is quite easy to write a test using the `Testing Actor Systems`.

[!code-csharp[Main](../../../src/core/Akka.Docs.Tests/Actors/FiniteStateMachine/ExampleFSMActorTests.cs?name=FSMTest)]

## Reference

### The FSM class
The FSM class inherits directly from `ActorBase`, when you extend `FSM` you must be aware that an actor is actually created:

```csharp
public class Buncher : FSM<State, IData>
{
    public ExampleFSMActor()
    {
        // fsm body ...
        Initialize();
    }
}

```

> [!NOTE]
> The `FSM` class defines a receive method which handles internal messages and passes everything else through to the `FSM` logic (according to the current state). When overriding the receive method, keep in mind that e.g. state timeout handling depends on actually passing the messages through the `FSM` logic.

The `FSM` class takes two type parameters:
- the supertype of all state names, usually a sealed trait with case objects extending it,
- the type of the state data which are tracked by the `FSM` module itself.

> [!NOTE]
> The state data together with the state name describe the internal state of the state machine; if you stick to this scheme and do not add mutable fields to the `FSM` class you have the advantage of making all changes of the internal state explicit in a few well-known places.

### Defining States
A state is defined by one or more invocations of the method
```csharp
When(<stateName>, <stateFunction>[, timeout = <timeout>]).
```
The given name must be an object which is type-compatible with the first type parameter given to the `FSM` class. This object is used as a hash key, so you must ensure that it properly implements `Equals` and `GetHashCode`; in particular it must not be mutable. The easiest fit for these requirements are case objects.

If the `timeout` parameter is given, then all transitions into this state, including staying, receive this timeout by default. Initiating the transition with an explicit timeout may be used to override this default, see [Initiating Transitions](#initiating-transitions) for more information. The state timeout of any state may be changed during action processing with `SetStateTimeout(state, duration)`. This enables runtime configuration e.g. via external message.

The `stateFunction` argument is a `delegate State<TState, TData> StateFunction(Event<TData> fsmEvent)`.

[!code-csharp[Main](../../../src/core/Akka.Docs.Tests/Actors/FiniteStateMachine/ExampleFSMActor.cs?name=FSMHandlers)]

### Defining the Initial State
Each `FSM` needs a starting point, which is declared using
```csharp
StartWith(state, data[, timeout])
```
The optionally given timeout argument overrides any specification given for the desired initial state. If you want to cancel a default timeout, use `null`.

### Unhandled Events
If a state doesn't handle a received event a warning is logged. If you want to do something else in this case you can specify that with `WhenUnhandled(stateFunction)`:

```csharp
WhenUnhandled(state =>
{
    if (state.FsmEvent is Queue x)
    {
        _log.Info("Received unhandled event: " + x);
        return Stay();
    }
    else
    {
        _log.Warning("Received unknown event: " + state.FsmEvent);
        return Goto(new Error());
    }
});
```
Within this handler the state of the `FSM` may be queried using the stateName method.

> [!IMPORTANT]
> This handler is not stacked, meaning that each invocation of `WhenUnhandled` replaces the previously installed handler.

### Initiating Transitions
The result of any stateFunction must be a definition of the next state unless terminating the `FSM`, which is described in [Termination from Inside](#termination-from-inside). The state definition can either be the current state, as described by the stay directive, or it is a different state as given by `Goto(state)`. The resulting object allows further qualification by way of the modifiers described in the following:

- `ForMax(duration)`. This modifier sets a state timeout on the next state. This means that a timer is started which upon expiry sends a `StateTimeout` message to the `FSM`. This timer is canceled upon reception of any other message in the meantime; you can rely on the fact that the StateTimeout message will not be processed after an intervening message. This modifier can also be used to override any default timeout which is specified for the target state. If you want to cancel the default timeout, use `null`.
- `Using(data)`. This modifier replaces the old state data with the new data given. If you follow the advice above, this is the only place where internal state data are ever modified.
- `Replying(msg)`. This modifier sends a reply to the currently processed message and otherwise does not modify the state transition.

All modifiers can be chained to achieve a nice and concise description:
```csharp
When(State.SomeState, state => {
  return GoTo(new Processing())
        .Using(new Data())
        .ForMax(TimeSpan.FromSeconds(5))
        .Replying(new WillDo());
});
```

### Monitoring Transitions
Transitions occur "between states" conceptually, which means after any actions you have put into the event handling block; this is obvious since the next state is only defined by the value returned by the event handling logic. You do not need to worry about the exact order with respect to setting the internal state variable, as everything within the `FSM` actor is running single-threaded anyway.

#### Internal Monitoring
Up to this point, the `FSM DSL` has been centered on states and events. The dual view is to describe it as a series of transitions. This is enabled by the method
```csharp
OnTransition(handler)
```
which associates actions with a transition instead of with a state and event. The handler is a delegate `void TransitionHandler(TState initialState, TState nextState)` function which takes a pair of states as input; no resulting state is needed as it is not possible to modify the transition in progress.

```csharp
OnTransition((initialState, nextState) =>
{
    if (initialState == State.Active && nextState == State.Idle)
    {
        SetTimer("timeout", new Tick(), TimeSpan.FromSeconds(1), repeat: true);
    } 
    else if (initialState == State.Active)
    {
        CancelTimer("timeout");
    }
    else if (nextState == State.Idle)
    {
        _log.Info("entering Idle from " + initialState);
    }
});
```

The handlers registered with this method are stacked, so you can intersperse `OnTransition` blocks with when blocks as suits your design. It should be noted, however, that all handlers will be invoked for each transition, not only the first matching one. This is designed specifically so you can put all transition handling for a certain aspect into one place without having to worry about earlier declarations shadowing later ones; the actions are still executed in declaration order, though.

> [!NOTE]
> This kind of internal monitoring may be used to structure your FSM according to transitions, so that for example the cancellation of a timer upon leaving a certain state cannot be forgot when adding new target states.

#### External Monitoring
External actors may be registered to be notified of state transitions by sending a message `SubscribeTransitionCallBack(IActorRef)`. The named actor will be sent a `CurrentState(self, stateName)` message immediately and will receive `Transition(IActorRef, oldState, newState)` messages whenever a state change is triggered.

Please note that a state change includes the action of performing an `GoTo(S)`, while already being state S. In that case the monitoring actor will be notified with an `Transition(ref, S, S)` message. This may be useful if your FSM should react on all (also same-state) transitions. In case you'd rather not emit events for same-state transitions use `Stay()` instead of `GoTo(S)`.

External monitors may be unregistered by sending `UnsubscribeTransitionCallBack(IActorRef)` to the `FSM` actor.

Stopping a listener without unregistering will not remove the listener from the subscription list; use `UnsubscribeTransitionCallback` before stopping the listener.

### Timers
Besides state timeouts, `FSM` manages timers identified by String names. You may set a timer using

```csharp
SetTimer(name, msg, interval, repeat);
```

where `msg` is the message object which will be sent after the duration interval has elapsed. If repeat is true, then the timer is scheduled at fixed rate given by the interval parameter. Any existing timer with the same name will automatically be canceled before adding the new timer.

Timers may be canceled using

```csharp
CancelTimer(name);

```
which is guaranteed to work immediately, meaning that the scheduled message will not be processed after this call even if the timer already fired and queued it. The status of any timer may be inquired with

```csharp
IsTimerActive(name);
```

These named timers complement state timeouts because they are not affected by intervening reception of other messages.

### Termination from Inside
The `FSM` is stopped by specifying the result state as
```csharp
Stop(reason, stateData);
```
The reason must be one of `Normal` (which is the default), `Shutdown` or `Failure(reason)`, and the second argument may be given to change the state data which is available during termination handling.

> [!NOTE]
> It should be noted that stop does not abort the actions and stop the `FSM` immediately. The stop action must be returned from the event handler in the same way as a state transition.

```csharp
When(State.Error, state =>
{
    if (state.FsmEvent == "stop")
    {
        return Stop();
    }

    return null;
});
```

You can use `OnTermination(handler)` to specify custom code that is executed when the `FSM` is stopped. The handler is a partial function which takes a `StopEvent(reason, stateName, stateData)` as argument:

```csharp
OnTermination(termination =>
{
    switch (termination)
    {
        case StopEvent<State, IData> st when st.Reason is Normal:
            // ...
            break;
        case StopEvent<State, IData> st when st.Reason is Shutdown:
            // ...
            break;
        case StopEvent<State, IData> st when st.Reason is Failure:
            // ...
            break;
    }
});
```
As for the `WhenUnhandled` case, this handler is not stacked, so each invocation of `OnTermination` replaces the previously installed handler.

### Termination from Outside
When an `IActorRef` associated to a `FSM` is stopped using the stop method, its `PostStop` hook will be executed. The default implementation by the `FSM` class is to execute the onTermination handler if that is prepared to handle a `StopEvent(Shutdown, ...)`.

> [!WARNING]
> In case you override `PostStop` and want to have your onTermination handler called, do not forget to call `base.PostStop`.
