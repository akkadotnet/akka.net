# Akka.Persistence.Hosting

## Akka.Persistence Extension Method

### WithJournal() Method

Used to configure a specific Akka.Persistence.Journal instance, primarily to support [Event Adapters](https://getakka.net/articles/persistence/event-adapters.html). 

```csharp
public static AkkaConfigurationBuilder WithJournal(
    this AkkaConfigurationBuilder builder,
    string journalId, 
    Action<AkkaPersistenceJournalBuilder> journalBuilder);
```

### Parameters

* `journalId` __string__

  The id of the journal. i.e. if you want to apply this adapter to the `akka.persistence.journal.sql` journal, just use `"sql"`.

* `journalBuilder` __Action\<AkkaPersistenceJournalBuilder\>__

  Configuration method for configuring the journal.

### WithInMemoryJournal() Method

Add an in-memory journal to the `ActorSystem`, usually for testing purposes.

```csharp
public static AkkaConfigurationBuilder WithInMemoryJournal(
    this AkkaConfigurationBuilder builder);
```

```csharp
public static AkkaConfigurationBuilder WithInMemoryJournal(
    this AkkaConfigurationBuilder builder,
    Action<AkkaPersistenceJournalBuilder> journalBuilder);
```

### Parameters

* `journalBuilder` __Action\<AkkaPersistenceJournalBuilder\>__

  Configuration method for configuring the journal.

### WithInMemorySnapshotStore() Method

Add an in-memory snapshot store to the `ActorSystem`, usually for testing purposes.

```csharp
public static AkkaConfigurationBuilder WithInMemorySnapshotStore(
    this AkkaConfigurationBuilder builder);
```
