---
uid: persistence-testing
title: Persistence Testing
---
# Persistence TestKit

It is hard to make persistence work properly. You can rely on Akka. Persistence does, but its own code can be made reliable only by writing
tests. For this sake, Akka.Net includes a specialized journal and snapshot store to aid in testing persistent actors.


## How to get started

Go and install an additional NuGet package `Akka.Persistence.TestKit.Xunit2`. That package includes a specialized persistent
journal named `TestJournal` and a snapshot store named `TestSnpashotStore` which will allow controlling behavior of all persistence
operations to simulate network failures, serialization problems, and other issues. For convenience, the package includes `PersistenceTestKit`
class to aid in writing unit tests for Akka.Net actor system. This class has a set of methods to alter different aspects of the journal and snapshot store.


## Persistence testing in action

We need a persistent actor that we will test. Our actor will do simple counting, upon request it will increase, decrease or
return currently stored value.

``` csharp
public class CounterActor : UntypedPersistentActor
{
    public CounterActor(string id)
    {
        PersistenceId = id;
    }

    private int value = 0;

    public override string PersistenceId { get; }

    protected override void OnCommand(object message)
    {
        switch (message as string)
        {
            case "inc":
                value++;
                break;

            case "dec":
                value++;
                break;

            case "read":
                Sender.Tell(value);
            
            default:
                return;
        }
    }

    protected override void OnRecover(object message)
    {
    }
}
```

Although the actor is inherited from `UntypedPersistentActor`, it is not persisting anything and will lose its value after
a restart. To fix that `inc` and `dec` must persist changes after an operation is done.

``` csharp
protected override void OnCommand(object message)
{
    switch (message as string)
    {
        case "inc":
            value++;
            Persist(message, _ => { });
            break;

        case "dec":
            value++;
            Persist(message, _ => { });
            break;

        case "read":
            Sender.Tell(value, Self);
            break;
        
        default:
            return;
    }
}
```

And we need `OnRecover` to be implemented so that the internal state is replyed when the actor is restarted.

``` csharp
protected override void OnRecover(object message)
{
    switch (message as string)
    {
        case "inc":
            value++;
            break;

        case "dec":
            value++;
            break;
        
        default:
            return;
    }
}
```

So now we are ready to write some tests.


### Writing tests

The current implementation has one fundamental flaw - actor persist changes in fire-n-forget style, that is no reliable as
underlying persistence can fail due to hundreds of reasons. We can verify that by writing a test which simulates network
failure of the underlying persistence store.

``` csharp
public class CounterActorTests : PersistenceTestKit
{
    [Fact]
    public async Task CounterActor_internal_state_will_be_lost_if_underlying_persistence_store_is_not_available()
    {
        await WithJournalWrite(write => write.Fail(), () =>
        {
            var actor = ActorOf(() => new CounterActor("test"), "counter");
            actor.Tell("inc", TestActor);
            actor.Tell("read", TestActor);

            var value = ExpectMsg<int>(TimeSpan.FromSeconds(3));
            value.ShouldBe(0);
        });
    }
}
```

When we will launch this test it will fail, because the persistence journal failed when we tried to tell `inc` command to the actor. The actor failed with the journal and `read` was never delivered anb we had not received any answer.


### How to make things better


## Reference


`TestJournal`  is based on `MemoryJournal` and initially works like it. To change its behavior an interceptor must be set. Interceptor must implement the following interface:

``` csharp
public interface IJournalInterceptor
{
    Task InterceptAsync(IPersistentRepresentation message);
}
```

Similarly `TestSnapshotStore` is based on `MemorySnapshotStore` and allows the use of interceptors on its persistence lifecycle methods. Interceptor must implement the following interface:

``` csharp
public interface ISnapshotStoreInterceptor
{
    Task InterceptAsync(string persistenceId, SnapshotSelectionCriteria criteria);
}
```

### PersistenceTestKit

This is a specialized test kit with a preconfigured persistence plugin that uses `TestJournal` and `TestSnapshotStore` by default. This class provides the following methods to control journal behavior: `WithJournalRecovery` and `WithJournalWrite`; to control snapshot store it provides `WithSnapshotSave`, `WithSnapshotLoad` and `WithSnapshotDelete` methods;
Usage example:
``` csharp
public class PersistentActorSpec : PersistenceTestKit
{
    [Fact]
    public async Task actor_must_fail_when_journal_will_fail_saving_message()
    {
        await WithJournalWrite(write => write.Fail(), () =>
        {
            var actor = ActorOf(() => new PersistActor());
            Watch(actor);

            actor.Tell("write", TestActor);
            ExpectTerminated(actor);
        });
    }
}
```

Each method accepts 2 arguments:
1. Behavior selector for operation under test;
2. Actual code which must be tested when selected behavior is applied. 

After the test code block is executed, journal and snapshot store will be switched back to normal mode, when all operations are passed to default in-memory implementation.

**Important!** All methods are `async`, this means that they **must** be awaited for proper execution.


### Built-in  journal behaviors

Out of the box, the package has the following behaviors:

* `Pass` - standard in-memory journal behavior, the message will be saved or restored without any errors or delays;
* `Fail` - the journal will always fail. All `Fail*` behaviors will crash the journal and actor by default will crash too. Use this and other `Fail*` methods to test journal store communication problems.
* `FailOnType` - journal will fail when it tries to write or recover the message of a given type;
* `FailIf` - the journal will fail if given predicate will return `true`;
* `FailUnless` - the journal will fail if given predicate will return `false`;
* `Reject` - reject all messages. All `Reject*` behaviors will signal that there are problems with a message and selected messages will not be persisted. Instead, the actor will receive a message from the persistence plugin about rejection and the actor must handle that.
* `RejectOnType` - reject messages only of specified type;
* `RejectIf` - reject messages if predicate will return `true`;
* `RejectUnless` - reject messages if predicate will return `false`.

All methods have additional overload to add artificial delay - `*WithDelay`, i.e. `FailWithDelay`. This could be helpful to simulate network delay or retry of physical persistence operation within the journal.

When all mentioned above behaviors are not enough, it is always possible to implement custom one by implementing the `IJournalInterceptor` interface. An instance of a custom interceptor can be set using the `SetInterceptorAsync` method.


### Built-in snapshot store behaviors

Snpahost store behaviors are following the same naming pattern as journal behaviors:

* `Pass` - standard in-memory snapshot store behavior, all operations will happen without any errors or delays;
* `Fail` - the snapshot store will always fail. All `Fail*` behaviors will crash the snapshot store. Use this and other `Fail*` methods to test snapshot store communication problems.
* `FailIf` - the snapshot store will fail if given predicate will return `true`;
* `FailUnless` - the snapshot store will fail if given predicate will return `false`;

All methods have additional overload to add artificial delay - `*WithDelay`, i.e. `FailWithDelay`. This could be helpful to simulate network delay or retry of physical persistence operation within the snapshot store.

When all mentioned above behaviors are not enough, it is always possible to implement custom one by implementing the `ISnapshotStoreInterceptor` interface. An instance of a custom interceptor can be set using the `SetInterceptorAsync` method.# Persistence TestKit

Akka.Net includes a specialized journal and snapshot store to aid in testing persistent actors. Additional functionality can be acquired by installing `Akka.Persistence.TestKit.Xunit2` Nuget package.

Package includes a specialized persistent journal named `TestJournal` and a snapshot store named `TestSnpashotStore` which will allow controlling behavior of all persistence operations to simulate network failures, serialization problems, and other issues.

For convenience, the package includes `PersistenceTestKit` class to aid in writing unit tests. This class has a set of methods to alter different aspects of the journal and snapshot store.

`TestJournal`  is based on `MemoryJournal` and initially works like it. To change its behavior an interceptor must be set. Interceptor must implement the following interface:

``` csharp
public interface IJournalInterceptor
{
    Task InterceptAsync(IPersistentRepresentation message);
}
```

Similarly `TestSnapshotStore` is based on `MemorySnapshotStore` and allows the use of interceptors on its persistence lifecycle methods. Interceptor must implement the following interface:

``` csharp
public interface ISnapshotStoreInterceptor
{
    Task InterceptAsync(string persistenceId, SnapshotSelectionCriteria criteria);
}
```
