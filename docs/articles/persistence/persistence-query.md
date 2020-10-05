---
uid: persistence-query
title: Persistence Query
---
# Persistence Query
Akka persistence query complements Persistence by providing a universal asynchronous stream based query interface that various journal plugins can implement in order to expose their query capabilities.

The most typical use case of persistence query is implementing the so-called query side (also known as "read side") in the popular CQRS architecture pattern - in which the writing side of the application (e.g. implemented using akka persistence) is completely separated from the "query side". Akka Persistence Query itself is not directly the query side of an application, however it can help to migrate data from the write side to the query side database. In very simple scenarios Persistence Query may be powerful enough to fulfill the query needs of your app, however we highly recommend (in the spirit of CQRS) of splitting up the write/read sides into separate datastores as the need arises.

## Design overview
Akka Persistence Query is purposely designed to be a very loosely specified API. This is in order to keep the provided APIs general enough for each journal implementation to be able to expose its best features, e.g. a SQL journal can use complex SQL queries or if a journal is able to subscribe to a live event stream this should also be possible to expose the same API - a typed stream of events.

**Each read journal must explicitly document which types of queries it supports**. Refer to your journal's plugins documentation for details on which queries and semantics it supports.

While Akka Persistence Query does not provide actual implementations of ReadJournals, it defines a number of pre-defined query types for the most common query scenarios, that most journals are likely to implement (however they are not required to).

## Read Journals
In order to issue queries one has to first obtain an instance of a ReadJournal. Read journals are implemented as Community plugins, each targeting a specific datastore (for example Cassandra or ADO.NET databases). For example, given a library that provides a akka.persistence.query.my-read-journal obtaining the related journal is as simple as:

```csharp
var actorSystem = ActorSystem.Create("query");

// obtain read journal by plugin id
var readJournal = PersistenceQuery.Get(actorSystem)
    .ReadJournalFor<SqlReadJournal>("akka.persistence.query.my-read-journal");

// issue query to journal
Source<EventEnvelope, NotUsed> source = readJournal
    .EventsByPersistenceId("user-1337", 0, long.MaxValue);

// materialize stream, consuming events
var mat = ActorMaterializer.Create(actorSystem);
source.RunForeach(envelope =>
{
    Console.WriteLine($"event {envelope}");
}, mat);
```

Journal implementers are encouraged to put this identifier in a variable known to the user, such that one can access it via `ReadJournalFor<SqlReadJournal>(SqlReadJournal.Identifier)`, however this is not enforced.

Read journal implementations are available as Community plugins.

### Predefined queries
Akka persistence query comes with a number of query interfaces built in and suggests Journal implementors to implement them according to the semantics described below. It is important to notice that while these query types are very common a journal is not required to implement all of them - for example because in a given journal such query would be significantly inefficient.

> [!NOTE]
> Refer to the documentation of the ReadJournal plugin you are using for a specific list of supported query types. For example, Journal plugins should document their stream completion strategies.

The predefined queries are:

**AllPersistenceIdsQuery (PersistentIds) and CurrentPersistenceIdsQuery**

`AllPersistenceIds` or `PersistenceIds`, and `CurrentPersistenceIds` in `IPersistenceIdsQuery` is used for retrieving all persistenceIds of all persistent actors.

```csharp
var queries = PersistenceQuery.Get(actorSystem)
    .ReadJournalFor<SqlReadJournal>("akka.persistence.query.my-read-journal");

var mat = ActorMaterializer.Create(actorSystem);
Source<string, NotUsed> src = queries.AllPersistenceIds();
```

The returned event stream is unordered and you can expect different order for multiple executions of the query.

The stream is not completed when it reaches the end of the currently used `PersistenceIds`, but it continues to push new `PersistenceIds` when new persistent actors are created. Corresponding query that is completed when it reaches the end of the currently used `PersistenceIds` is provided by `CurrentPersistenceIds`.

Periodic polling of new `PersistenceIds` are done on the query side by retrieving the events in batches that sometimes can be delayed up to the configured `refresh-interval` or given `RefreshInterval` hint.

The stream is completed with failure if there is a failure in executing the query in the backend journal.


**EventsByPersistenceIdQuery and CurrentEventsByPersistenceIdQuery**

`EventsByPersistenceId` is used for retrieving events for a specific `PersistentActor` identified by `PersistenceId`.

```csharp
var queries = PersistenceQuery.Get(actorSystem)
    .ReadJournalFor<SqlReadJournal>("akka.persistence.query.my-read-journal");

var mat = ActorMaterializer.Create(actorSystem);
var src = queries.EventsByPersistenceId("some-persistence-id", 0L, long.MaxValue);
Source<object, NotUsed> events = src.Select(c => c.Event);
```

You can retrieve a subset of all events by specifying `FromSequenceNr` and `ToSequenceNr` or use `0L` and `long.MaxValue` respectively to retrieve all events. Note that the corresponding sequence number of each event is provided in the `EventEnvelope`, which makes it possible to resume the stream at a later point from a given sequence number.

The returned event stream is ordered by sequence number, i.e. the same order as the `PersistentActor` persisted the events. The same prefix of stream elements (in same order) are returned for multiple executions of the query, except for when events have been deleted.

The stream is not completed when it reaches the end of the currently stored events, but it continues to push new events when new events are persisted. Corresponding query that is completed when it reaches the end of the currently stored events is provided by `CurrentEventsByPersistenceId`.

The write journal is notifying the query side as soon as events are persisted, but for efficiency reasons the query side retrieves the events in batches that sometimes can be delayed up to the configured `refresh-interval` or given `RefreshInterval` hint.

The stream is completed with failure if there is a failure in executing the query in the backend journal.

**EventsByTag and CurrentEventsByTag**

`EventsByTag` allows querying events regardless of which `PersistenceId` they are associated with. This query is hard to implement in some journals or may need some additional preparation of the used data store to be executed efficiently. The goal of this query is to allow querying for all events which are "tagged" with a specific tag. That includes the use case to query all domain events of an Aggregate Root type. Please refer to your read journal plugin's documentation to find out if and how it is supported.

Some journals may support tagging of events via an Event Adapters that wraps the events in a `Akka.Persistence.Journal.Tagged` with the given tags. The journal may support other ways of doing tagging - again, how exactly this is implemented depends on the used journal. Here is an example of such a tagging event adapter:

```csharp
public class MyTaggingEventAdapter : IWriteEventAdapter
{
    private ImmutableHashSet<string> colors = ImmutableHashSet.Create("green", "black", "blue");

    public string Manifest(object evt)
    {
        return string.Empty;
    }

    public object ToJournal(object evt)
    {
        var str = evt as string;
        if (str != null)
        {
            var tags = colors.Aggregate(
                ImmutableHashSet<string>.Empty,
                (acc, c) => str.Equals(c) ? acc.Add(c): acc);
            if (tags.IsEmpty)
                return evt;
            else
                return new Tagged(evt, tags);
        }
        else
        {
            return evt;
        }
    }
}
```

> [!NOTE]
> A very important thing to keep in mind when using queries spanning multiple `PersistenceIds`, such as `EventsByTag` is that the order of events at which the events appear in the stream rarely is guaranteed (or stable between materializations).

Journals may choose to opt for strict ordering of the events, and should then document explicitly what kind of ordering guarantee they provide - for example "ordered by timestamp ascending, independently of `PersistenceId`" is easy to achieve on relational databases, yet may be hard to implement efficiently on plain key-value datastores.

In the example below we query all events which have been tagged (we assume this was performed by the write-side using an EventAdapter, or that the journal is smart enough that it can figure out what we mean by this tag - for example if the journal stored the events as json it may try to find those with the field tag set to this value etc.).

```csharp
// assuming journal is able to work with numeric offsets we can:
Source<EventEnvelope, NotUsed> blueThings = readJournal.EventsByTag("blue", 0L);

// find top 10 blue things:
Task<ImmutableHashSet<object>> top10BlueThings = blueThings
    .Select(c => c.Event)
    .Take(10) // cancels the query stream after pulling 10 elements
    .RunAggregate(
        ImmutableHashSet<object>.Empty,
        (acc, c) => acc.Add(c),
        mat);

// start another query, from the known offset
var furtherBlueThings = readJournal.EventsByTag("blue", offset: 10);
```

As you can see, we can use all the usual stream combinators available from Akka Streams on the resulting query stream, including for example taking the first 10 and cancelling the stream. It is worth pointing out that the built-in `EventsByTag` query has an optionally supported offset parameter (of type Long) which the journals can use to implement resumable-streams. For example a journal may be able to use a WHERE clause to begin the read starting from a specific row, or in a datastore that is able to order events by insertion time it could treat the Long as a timestamp and select only older events.

If your usage does not require a live stream, you can use the `CurrentEventsByTag` query.

**AllEvents and CurrentAllEvents**

`AllEvents` allows replaying and monitoring all events regardless of which `PersistenceId` they are associated with. The goal of this query is to allow replaying and monitoring for all events that are stored inside a journal, regardless of its source.Please refer to your read journal plugin's documentation to find out if and how it is supported.

The stream is not completed when it reaches the last event recorded, but it continues to push new events when new event are persisted. Corresponding query that is completed when it reaches the end of the last event persisted when the query id called is provided by `CurrentAllEvents`.

The write journal is notifying the query side as soon as new events are created and there is no periodic polling or batching involved in this query.

> [!NOTE]
> A very important thing to keep in mind when using queries spanning multiple `PersistenceIds`, such as `AllEvents` is that the order of events at which the events appear in the stream rarely is guaranteed (or stable between materializations).

Journals may choose to opt for strict ordering of the events, and should then document explicitly what kind of ordering guarantee they provide - for example "ordered by timestamp ascending, independently of `PersistenceId`" is easy to achieve on relational databases, yet may be hard to implement efficiently on plain key-value datastores.

In the example below we query all events which have been stored inside the journal.

```csharp
// assuming journal is able to work with numeric offsets we can:
Source<EventEnvelope, NotUsed> allEvents = readJournal.AllEvents(offset: 0L);

// replay the first 10 things stored:
Task<ImmutableHashSet<object>> first10Things = allEvents
    .Select(c => c.Event)
    .Take(10) // cancels the query stream after pulling 10 elements
    .RunAggregate(
        ImmutableHashSet<object>.Empty,
        (acc, c) => acc.Add(c),
        mat);

// start another query, from the known offset
var next10Things = readJournal.AllEvents(offset: 10);
```

As you can see, we can use all the usual stream combinators available from Akka Streams on the resulting query stream, including for example taking the first 10 and cancelling the stream. It is worth pointing out that the built-in `AllEvents` query has an optionally supported offset parameter (of type Long) which the journals can use to implement resumable-streams. For example a journal may be able to use a WHERE clause to begin the read starting from a specific row, or in a datastore that is able to order events by insertion time it could treat the Long as a timestamp and select only older events.

If your usage does not require a live stream, you can use the `CurrentEventsByTag` query.

### Materialized values of queries
Journals are able to provide additional information related to a query by exposing materialized values, which are a feature of Akka Streams that allows to expose additional values at stream materialization time.

More advanced query journals may use this technique to expose information about the character of the materialized stream, for example if it's finite or infinite, strictly ordered or not ordered at all. The materialized value type is defined as the second type parameter of the returned `Source`, which allows journals to provide users with their specialised query object, as demonstrated in the sample below:

```csharp
public class RichEvent
{
    public RichEvent(ISet<string> tags, object payload)
    {
        Tags = tags;
        Payload = payload;
    }

    public ISet<string> Tags { get; }
    public object Payload { get; }
}

public class QueryMetadata
{
    public QueryMetadata(bool deterministicOrder, bool infinite)
    {
        DeterministicOrder = deterministicOrder;
        Infinite = infinite;
    }

    public bool DeterministicOrder { get; }
    public bool Infinite { get; }
}
```

```csharp
public Source<RichEvent, QueryMetadata> ByTagsWithMeta(ISet<string> tags) { }
```

```csharp
var query = readJournal.ByTagsWithMeta(ImmutableHashSet.Create("red", "blue"));
query
    .MapMaterializedValue(meta =>
    {
        Console.WriteLine(
            $"The query is: ordered deterministically: {meta.DeterministicOrder}, infinite: {meta.Infinite}");
        return meta;
    })
    .Select(evt =>
    {
        Console.WriteLine($"Event payload: {evt.Payload}");
        return evt;
    })
    .RunWith(Sink.Ignore<RichEvent>(), mat);
```

## Performance and denormalization
When building systems using Event sourcing and CQRS ([Command & Query Responsibility Segregation](https://msdn.microsoft.com/en-us/library/jj554200.aspx)) techniques it is tremendously important to realise that the write-side has completely different needs from the read-side, and separating those concerns into datastores that are optimised for either side makes it possible to offer the best experience for the write and read sides independently.

For example, in a bidding system it is important to "take the write" and respond to the bidder that we have accepted the bid as soon as possible, which means that write-throughput is of highest importance for the write-side – often this means that data stores which are able to scale to accommodate these requirements have a less expressive query side.

On the other hand the same application may have some complex statistics view or we may have analysts working with the data to figure out best bidding strategies and trends – this often requires some kind of expressive query capabilities like for example SQL or writing Spark jobs to analyse the data. Therefore the data stored in the write-side needs to be projected into the other read-optimised datastore.

> [!NOTE]
> When referring to Materialized Views in Akka Persistence think of it as "some persistent storage of the result of a Query". In other words, it means that the view is created once, in order to be afterwards queried multiple times, as in this format it may be more efficient or interesting to query it (instead of the source events directly).

### Materialize view to Reactive Streams compatible datastore
If the read datastore exposes a Reactive Streams interface then implementing a simple projection is as simple as, using the read-journal and feeding it into the databases driver interface, for example like so:

```csharp
var system = ActorSystem.Create("MySystem");
var mat = ActorMaterializer.Create(system);
 
var readJournal =
  PersistenceQuery.Get(system).ReadJournalFor<MyReadJournal>(JournalId)
ISubscriber<IImmutableList<object>> dbBatchWriter =
  new ReactiveStreamsCompatibleDBDriver.BatchWriter();
 
// Using an example (Reactive Streams) Database driver
readJournal
  .EventsByPersistenceId("user-1337")
  .Select(envelope => envelope.Event)
  .Select(ConvertToReadSideTypes) // convert to datatype
  .Grouped(20) // batch inserts into groups of 20
  .RunWith(Sink.FromSubscriber(dbBatchWriter), mat); // write batches to read-side database
```

### Materialize view using SelectAsync
If the target database does not provide a reactive streams Subscriber that can perform writes, you may have to implement the write logic using plain functions or Actors instead.

In case your write logic is state-less and you just need to convert the events from one data type to another before writing into the alternative datastore, then the projection is as simple as:

```csharp
public class ExampleStore
{
    public Task<object> Save(object evt)
    {
        return Task.FromResult(evt);
    }
}
```

```csharp
var store = new ExampleStore();

readJournal
    .EventsByTag("bid", 0L)
    .SelectAsync(1, e => store.Save(e))
    .RunWith(Sink.Ignore<object>(), mat);
```

### Resumable projections
Sometimes you may need to implement "resumable" projections, that will not start from the beginning of time each time when run. In this case you will need to store the sequence number (or offset) of the processed event and use it the next time this projection is started. This pattern is not built-in, however is rather simple to implement yourself.

The example below additionally highlights how you would use Actors to implement the write side, in case you need to do some complex logic that would be best handled inside an Actor before persisting the event into the other datastore:

```csharp
var timeout = new Timeout(TimeSpan.FromSeconds(3));
 
var bidProjection = new MyResumableProjection("bid");
 
var writerProps = Props.Create(typeof(TheOneWhoWritesToQueryJournal), "bid");
var writer = system.ActorOf(writerProps, "bid-projection-writer");
 
readJournal
  .EventsByTag("bid", bidProjection.LatestOffset ?? 0L)
  .SelectAsync(8, envelope => writer.Ask(envelope.Event, timeout).ContinueWith(t => envelope.Offset, TaskContinuationOptions.OnlyOnRanToCompletion))
  .SelectAsync(1, offset => bidProjection.saveProgress(offset))
  .RunWith(Sink.Ignore<object>(), mat);

public class TheOneWhoWritesToQueryJournal(id: String) : ActorBase
{
  public TheOneWhoWritesToQueryJournal(string id) {}

  private DummyStore _store = new DummyStore();
 
  private ComplexState _state = ComplexState();
 
  protected override bool Receive(object message) {
    _state = UpdateState(_state, message);
    if (_state.IsReadyToSave())
      _store.Save(new Record(_state));
    return true;
  }
 
  private ComplexState UpdateState(ComplexState state, object msg)
  {
    // some complicated aggregation logic here ...
    return state;
  }
}
```
## Configuration
Configuration settings can be defined in the configuration section with the absolute path corresponding to the identifier, which is `Akka.Persistence.Query.Journal.Sqlite` for the default `SqlReadJournal.Identifier`.

It can be configured with the following properties:

[!code-json[Main](../../../src/contrib/persistence/Akka.Persistence.Query.Sql/reference.conf)]
