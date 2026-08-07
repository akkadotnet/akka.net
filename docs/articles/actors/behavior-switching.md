---
uid: behavior-switching
title: Behavior Switching (Become / Unbecome)
---
# Behavior Switching (`Become` / `Unbecome`)

Akka.NET actors can **hot-swap** their message-handling logic at runtime. That is the actor keeps the same `IActorRef` and mailbox, but changes *how* it responds to messages. This is done with `Become` / `BecomeStacked` and `UnbecomeStacked` on `IActorContext` (available as `Context` inside the actor).

Use behavior switching when an actor has a small number of distinct modes — idle vs busy, unauthorized vs ready, open vs closed — and you want each mode expressed as its own handler instead of a pile of `if` flags.

> [!WARNING]
> After a supervisor **restarts** an actor, behavior resets to the constructor / initial `Receive` setup. Switched behaviors do not survive restart unless you re-apply them in `PreStart` / `PostRestart` (or encode state in fields that drive which behavior you install).

## When to use it

Good fits:

* Protocols with ordered steps (handshake → work → shutdown).
* Temporary modes (e.g. "shutting down: reject new work").
* Lightweight FSMs without the full [`FSM<TState, TData>`](xref:finite-state-machine) API.
* Pairing with [`IWithStash`](xref:receive-actor-api#stash) to park messages until the actor is ready.

Prefer a dedicated FSM or explicit state object when you have many states, complex transitions, or need timers/state data as first-class concepts.

## Replace vs stack

| API | Effect | Typical use |
|-----|--------|-------------|
| `Become(handler)` | **Replace** the current behavior (top of stack). | Most apps — explicit next state, no `Unbecome`. |
| `BecomeStacked(handler)` | **Push** a new behavior on the stack. | Nested modes that must return to the previous one. |
| `UnbecomeStacked()` | **Pop** the stack. | Must match pushes or you leak stack frames. |

`Become` (replace) is the default you want. Only use the stack when you truly need to return to the prior behavior.

## Example: replace behavior (`ReceiveActor`)

```csharp
public sealed class MoodActor : ReceiveActor
{
    public MoodActor()
    {
        // initial behavior
        Receive<string>(s => s == "angry", _ => Become(Angry));
        Receive<string>(s => s == "happy", _ => Become(Happy));
    }

    private void Angry()
    {
        Receive<string>(s => s == "angry", _ => Sender.Tell("already angry"));
        Receive<string>(s => s == "happy", _ => Become(Happy));
    }

    private void Happy()
    {
        Receive<string>(s => s == "happy", _ => Sender.Tell("already happy"));
        Receive<string>(s => s == "angry", _ => Become(Angry));
    }
}
```

Each `Become(...)` installs a new set of `Receive` handlers for subsequent messages.

## Example: stacked behavior

```csharp
public sealed class Swapper : ReceiveActor
{
    public sealed class Swap
    {
        public static readonly Swap Instance = new();
        private Swap() { }
    }

    public Swapper()
    {
        Receive<Swap>(_ =>
        {
            BecomeStacked(() =>
            {
                Receive<Swap>(_ =>
                {
                    UnbecomeStacked(); // back to previous behavior
                });
            });
        });
    }
}
```

Keep push/pop balanced. Unbalanced `BecomeStacked` without matching `UnbecomeStacked` is a memory leak.

## With stash

A common pattern: stash messages while uninitialized, then `Become` the ready behavior and `Stash.UnstashAll()`:

```csharp
public sealed class NeedsInit : ReceiveActor, IWithStash
{
    public IStash Stash { get; set; }

    public NeedsInit()
    {
        Receive<Init>(_ =>
        {
            Become(Initialized);
            Stash.UnstashAll();
        });
        ReceiveAny(_ => Stash.Stash());
    }

    private void Initialized()
    {
        Receive<Work>(w => /* handle */);
    }

    public sealed class Init { }
    public sealed class Work { }
}
```

See [Stash](xref:receive-actor-api#stash) for mailbox requirements (`IWithStash`).

## Related APIs

* [`ReceiveActor` Become/Unbecome](xref:receive-actor-api#becomeunbecome) — same concepts inline in the ReceiveActor guide.
* [`UntypedActor` Become/Unbecome](xref:untyped-actor-api#becomeunbecome) — `Become(Action<object>)` style handlers.
* [Finite State Machines](xref:finite-state-machine) — richer state machines with timers and `OnTransition`.
