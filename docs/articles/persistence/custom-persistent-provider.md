# Writing A Custom Akka.Persistence Provider

## Implementing Akka.Persistence Journal

* Discuss design goals
* Discuss Journal responsibility

Akka.Persistence journal is responsible for storing all events for playback in the event of a recovery.

All of the code examples in this documentation will assume a Sqlite database and the `Journal` table schema we will be using are:

```sqlite
CREATE TABLE IF NOT EXISTS event_journal (
    ordering INTEGER PRIMARY KEY NOT NULL,
    persistence_id VARCHAR(255) NOT NULL,
    sequence_nr INTEGER(8) NOT NULL,
    is_deleted INTEGER(1) NOT NULL,
    manifest VARCHAR(255) NULL,
    timestamp INTEGER NOT NULL,
    payload BLOB NOT NULL,
    serializer_id INTEGER(4),
    UNIQUE (persistence_id, sequence_nr));

CREATE TABLE IF NOT EXISTS journal_metadata (
    persistence_id VARCHAR(255) NOT NULL,
    sequence_nr INTEGER(8) NOT NULL,
    PRIMARY KEY (persistence_id, sequence_nr));
```

### Required Method Implementations

#### ReplayMessagesAsync

```c#
Task ReplayMessagesAsync(
    IActorContext context, 
    string persistenceId, 
    long fromSequenceNr, 
    long toSequenceNr, 
    long max, 
    Action<IPersistentRepresentation> recoveryCallback)
```

* **`context`**: The contextual information about the actor processing the replayed messages.
* **`persistenceId`**: Persistent actor identifier
* **`fromSequenceNr`**: Inclusive sequence number where replay should start
* **`toSequenceNr`**: Inclusive sequence number where replay should end
* **`max`**: Maximum number of messages to be replayed
* **`recoveryCallback`**: Called to replay a message, may be called from any thread.

This method should asynchronously replay persistent messages:

* Implementations replay a message by calling `recoveryCallback`.
* The returned `Task` must be completed when all messages (matching the sequence number bounds) have been replayed.
* The `Task` must be completed with a failure if any of the persistent messages could not be replayed.

The `toSequenceNr` is the lowest of what was returned by `ReadHighestSequenceNrAsync` and what the user specified as the recovery `Recovery` parameter. This does imply that this call is always preceded by reading the highest sequence number for the given `persistenceId`.

This call is NOT protected with a circuit-breaker because it may take a long time to replay all events. The plugin implementation itself must protect against an unresponsive backend store and make sure that the returned `Task` is completed with success or failure within reasonable time. It is not allowed to ignore completing the `Task`.

`ByPersistenceIdSql` in this example refers to this SQL query statement:

```sqlite
SELECT e.persistence_id as PersistenceId,
        e.sequence_nr as SequenceNr,
        e.timestamp as Timestamp,
        e.is_deleted as IsDeleted,
        e.manifest as Manifest,
        e.payload as Payload,
        e.serializer_id as SerializerId
    FROM event_journal e
    WHERE e.persistence_id = @PersistenceId
    AND e.sequence_nr BETWEEN @FromSequenceNr AND @ToSequenceNr
    ORDER BY sequence_nr ASC;
```

Semi pseudo-code example:

```c#
public override async Task ReplayMessagesAsync(
    IActorContext context, 
    string persistenceId, 
    long fromSequenceNr, 
    long toSequenceNr, 
    long max,
    Action<IPersistentRepresentation> recoveryCallback)
{
    // Create a new DbConnection instance
    using (DbConnection connection = CreateDbConnection())
    {
        await connection.OpenAsync();
        // This CancellationTokenSource will act as our timeout mechanism
        using (var cancellationToken = new CancellationTokenSource(_timeout))
        // Create new DbCommand instance
        using (var command = GetCommand(connection, ByPersistenceIdSql))
        {
            // Populate the SQL parameters
            AddParameter(command, "@PersistenceId", DbType.String, persistenceId);
            AddParameter(command, "@FromSequenceNr", DbType.Int64, fromSequenceNr);
            AddParameter(command, "@ToSequenceNr", DbType.Int64, toSequenceNr);
            
            // Create a DbDataReader to sequentially read the returned query result
            using (var reader = await command.ExecuteReaderAsync(CommandBehavior.Default, cancellationToken))
            {
                var i = 0L;
                while (i++ < max && await reader.ReadAsync(cancellationToken))
                {
                    var persistenceId = reader.GetString(0);
                    var sequenceNr = reader.GetInt64(1);
                    var timestamp = reader.GetInt64(2);
                    var isDeleted = reader.GetBoolean(3);
                    var manifest = reader.GetString(4);
                    var payload = reader[5];
                    var serializerId = reader.GetInt32(6);
                    
                    // Deserialize the persistent payload using the data we read from the reader
                    var deserialized = Serialization.Deserialize((byte[])payload, serializerId, manifest);
                    
                    // Call the recovery callback with the deserialized data from the database
                    recoveryCallback(new Persistent(
                        deserialized, 
                        sequenceNr,
                        persistenceId,
                        manifest, 
                        isDeleted, 
                        ActorRefs.NoSender, 
                        null, 
                        timestamp));
                }
                command.Cancel();
            }
        }
    }
}
```

#### ReadHighestSequenceNrAsync

```c#
Task<long> ReadHighestSequenceNrAsync(
    string persistenceId, 
    long fromSequenceNr)
```

* **`persistenceId`**: Persistent actor identifier
* **`fromSequenceNr`**: Hint where to start searching for the highest sequence number. When a persistent actor is recovering this `fromSequenceNr` will the sequence number of the used snapshot, or `0L` if no snapshot is used.

This method should asynchronously reads the highest stored sequence number for provided `persistenceId`. The persistent actor will use the highest sequence number after recovery as the starting point when persisting new events. This sequence number is also used as `toSequenceNr` in subsequent calls to `ReplayMessagesAsync` unless the user has specified a lower `toSequenceNr`. Journal must maintain the highest sequence number and never decrease it.

This call is protected with a circuit-breaker.

Please also note that requests for the highest sequence number may be made concurrently to writes executing for the same `persistenceId`, in particular it is possible that a restarting actor tries to recover before its outstanding writes have completed.

`HighestSequenceNrSql` in this example refers to this SQL query statement:

```sqlite
SELECT MAX(e.ordering) as Ordering
FROM event_journal e;
```

```c#
public override async Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr)
{
    // Create a new DbConnection instance
    using (var connection = CreateDbConnection())
    {
        await connection.OpenAsync();
        // This CancellationTokenSource will act as our timeout mechanism
        using (var cancellationToken = new CancellationTokenSource(_timeout))
        // Create new DbCommand instance
        using (var command = GetCommand(connection, HighestSequenceNrSql))
        {
            // Populate the SQL parameters
            AddParameter(command, "@PersistenceId", DbType.String, persistenceId);
            
            // Execute the SQL query statement
            var result = await command.ExecuteScalarAsync(cancellationToken);
            
            // Return the result if one is returned by the query result, else return zero
            return result is long ? Convert.ToInt64(result) : 0L;
        }
    }
}
```

#### WriteMessagesAsync

```c#
protected abstract Task<IImmutableList<Exception>> WriteMessagesAsync(
    IEnumerable<AtomicWrite> messages)
```

Asynchronously writes a batch of persistent messages to the journal.

The batch is only for performance reasons, i.e. all messages don't have to be written atomically. Higher throughput can typically be achieved by using batch inserts of many records compared to inserting records one-by-one, but this aspect depends on the underlying data store and a journal implementation can implement it as efficiently as possible. Journals should aim to persist events in-order for a given `persistenceId` as otherwise in case of a failure, the persistent state may end up being inconsistent.

Each `AtomicWrite` message contains the single `Persistent` that corresponds to the event that was passed to the `Eventsourced.Persist<TEvent>(TEvent, Action<TEvent>)` method of the `PersistentActor`, or it contains several `Persistent` that correspond to the events that were passed to the `Eventsourced.PersistAll<TEvent>(IEnumerable<TEvent>, Action<TEvent>)` method of the `PersistentActor`. All `Persistent` of the `AtomicWrite` must be written to the data store atomically, i.e. all or none must be stored. If the journal (data store) cannot support atomic writes of multiple events it should reject such writes with a `NotSupportedException` describing the issue. This limitation should also be documented by the journal plugin.

If there are failures when storing any of the messages in the batch the returned `Task` must be completed with failure. The `Task` must only be completed with success when all messages in the batch have been confirmed to be stored successfully, i.e. they will be readable, and visible, in a subsequent replay. If there is uncertainty about if the messages were stored or not the `Task` must be completed with failure.

Data store connection problems must be signaled by completing the `Task` with failure.

The journal can also signal that it rejects individual messages (`AtomicWrite`) by the returned `Task`. It is possible but not mandatory to reduce the number of allocations by returning null for the happy path, i.e. when no messages are rejected. Otherwise the returned list must have as many elements as the input `messages`. Each result element signals if the corresponding `AtomicWrite` is rejected or not, with an exception describing the problem. Rejecting a message means it was not stored, i.e. it must not be included in a later replay. Rejecting a message is typically done before attempting to store it, e.g. because of serialization error.

Data store connection problems must not be signaled as rejections.

It is possible but not mandatory to reduce the number of allocations by returning null for the happy path, i.e. when no messages are rejected.

Calls to this method are serialized by the enclosing journal actor. If you spawn work in asynchronous tasks it is alright that they complete the futures in any order, but the actual writes for a specific persistenceId should be serialized to avoid issues such as events of a later write are visible to consumers (query side, or replay) before the events of an earlier write are visible. A `PersistentActor` will not send a new `WriteMessages` request before the previous one has been completed.

Please note that the `IPersistentRepresentation.Sender` of the contained `Persistent` objects has been nulled out (i.e. set to `ActorRefs.NoSender` in order to not use space in the journal for a sender reference that will likely be obsolete during replay.

Please also note that requests for the highest sequence number may be made concurrently to this call executing for the same `persistenceId`, in particular it is possible that a restarting actor tries to recover before its outstanding writes have completed. In the latter case it is highly desirable to defer reading the highest sequence number until all outstanding writes have completed, otherwise the `PersistentActor` may reuse sequence numbers.

This call is protected with a circuit-breaker.

`InsertEventSql` in this example refers to this SQL query statement:

```sqlite
INSERT INTO event_journal (
    persistence_id,
    sequence_nr,
    timestamp,
    is_deleted,
    manifest,
    payload,
    serializer_id
) VALUES (
    @PersistenceId,
    @SequenceNr,
    @Timestamp,
    @IsDeleted,
    @Manifest,
    @Payload,
    @SerializerId
);
```

Semi pseudo-code example:

```c#
protected override async Task<IImmutableList<Exception>> WriteMessagesAsync(
    IEnumerable<AtomicWrite> messages)
{
    // For each of the atomic write request, create an async Task 
    var writeTasks = messages.Select(async message =>
    {
        // Create a new DbConnection instance
        using (var connection = CreateDbConnection())
        {
            await connection.OpenAsync();
            
            // This CancellationTokenSource will act as our timeout mechanism
            using (var cancellationToken = new CancellationTokenSource(_timeout))
            // Create new DbCommand instance
            using (var command = GetCommand(connection, InsertEventSql))
            // Create a new DbTransaction instance for this AtomicWrite
            using (var tx = connection.BeginTransaction())
            {
                command.Transaction = tx;
                
                // Cast the payload to IImmutableList<IPersistentRepresentation>.
                // Note that AtomicWrite.Payload property is declared as an object property,
                // but it is always populated with an IImmutableList<IPersistentRepresentation> instance
                var persistentMessages = ((IImmutableList<IPersistentRepresentation>)message.Payload)
                    .ToArray();
                foreach (var @event in persistentMessages)
                {
                    // Get the serializer associated with the payload type, else use a default serializer
                    var serializer = _actorSystem.Serialization.FindSerializerForType(
                        @event.Payload.GetType(), 
                        DefaultSerializer);
                    
                    // This WithTransport method call is important, it allows for proper local IActorRef
                    // serialization by switching the serialization information context during the
                    // serialization process
                    var (binary, manifest) = Akka.Serialization.Serialization.WithTransport(
                        _actorSystem, 
                        (@event.Payload, serializer), 
                        state =>
                        {
                            var (thePayload, theSerializer) = state;
                            var thisManifest = "";
                            // There are two kinds of serializer when it comes to manifest support,
                            // we have to support both of them for proper payload serialization
                            if (theSerializer is SerializerWithStringManifest stringManifest)
                            {
                                thisManifest = stringManifest.Manifest(thePayload);
                            }
                            else if (theSerializer.IncludeManifest)
                            {
                                thisManifest = thePayload.GetType().TypeQualifiedName();
                            }
                            
                            // Return the serialized byte array and the manifest for the serialized data
                            return (theSerializer.ToBinary(thePayload), thisManifest);
                        });
                    
                    // Populate the SQL parameters
                    AddParameter(command, "@PersistenceId", DbType.String, @event.PersistenceId);
                    AddParameter(command, "@SequenceNr", DbType.Int64, @event.SequenceNr);
                    AddParameter(command, "@Timestamp", DbType.Int64, @event.Timestamp);
                    AddParameter(command, "@IsDeleted", DbType.Boolean, false);
                    AddParameter(command, "@Manifest", DbType.String, manifest);
                    AddParameter(command, "@Payload", DbType.Binary, binary);
                    AddParameter(command, "@SerializerId", DbType.Int32, serializer.Identifier);
                    
                    // Execute the SQL query
                    await command.ExecuteScalarAsync(cancellationToken);
                    
                    // clear the parameters and reuse the DbCommand instance
                    command.Parameters.Clear();
                }
                // Commit the DbTransaction
                tx.Commit();
            }
        }
    }).ToArray();

    // Collect all exceptions raised for each failed writes and return them.
    var result = await Task<IImmutableList<Exception>>.Factory
        .ContinueWhenAll(writeTasks,
            tasks => tasks.Select(t => t.IsFaulted ? TryUnwrapException(t.Exception) : null).ToImmutableList());

    return result;
}
```

#### DeleteMessagesToAsync

```c#
protected abstract Task DeleteMessagesToAsync(
    string persistenceId, 
    long toSequenceNr)
```

Asynchronously deletes all persistent messages up to inclusive `toSequenceNr` bound. This operation has to be done atomically.

This call is protected with a circuit-breaker.

`DeleteBatchSql` in this example refers to this SQL query statement:

```sqlite
DELETE FROM event_journal
    WHERE persistence_id = @PersistenceId AND sequence_nr <= @ToSequenceNr;
DELETE FROM journal_metadata
    WHERE persistence_id = @PersistenceId AND sequence_nr <= @ToSequenceNr;
```

`HighestSequenceNrSql` in this example refers to this SQL query statement:

```sqlite
SELECT MAX(e.ordering) as Ordering
FROM event_journal e;
```

`UpdateSequenceNrSql` in this example refers to this SQL query statement:

```sqlite
INSERT INTO journal_metadata (persistence_id, sequence_nr)
VALUES (@PersistenceId, @SequenceNr);
```

```c#
protected override async Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr)
{
    // Create a new DbConnection instance
    using (var connection = CreateDbConnection())
    {
        await connection.OpenAsync();
        
        // This CancellationTokenSource will act as our timeout mechanism
        using (var cancellationToken = new CancellationTokenSource(_timeout))
        {
            // We will be using two DbCommands to complete this process
            using (var deleteCommand = GetCommand(connection, DeleteBatchSql))
            using (var highestSeqNrCommand = GetCommand(connection, HighestSequenceNrSql))
            {
                // Populate the SQL parameters
                AddParameter(highestSeqNrCommand, "@PersistenceId", DbType.String, persistenceId);

                AddParameter(deleteCommand, "@PersistenceId", DbType.String, persistenceId);
                AddParameter(deleteCommand, "@ToSequenceNr", DbType.Int64, toSequenceNr);

                // Create a DbTransaction instance
                using (var tx = connection.BeginTransaction())
                {
                    deleteCommand.Transaction = tx;
                    highestSeqNrCommand.Transaction = tx;

                    // Execute the HighestSequenceNrSql SQL statement and fetch the current highest
                    // sequence number for the given persistence identifier
                    var res = await highestSeqNrCommand.ExecuteScalarAsync(cancellationToken);
                    var highestSeqNr = res is long ? Convert.ToInt64(res) : 0L;

                    // Delete all events up to toSequenceNr both in the journal and metadata table
                    await deleteCommand.ExecuteNonQueryAsync(cancellationToken);

                    // Update the metadata table to reflect the new highest sequence number, if the
                    // toSequenceNr is higher than our current highest sequence number
                    if (highestSeqNr <= toSequenceNr)
                    {
                        // Create a new DbCommand instance
                        using (var updateCommand = GetCommand(connection, UpdateSequenceNrSql))
                        {
                            // This update is still part of the same transaction as everything else
                            // in this process
                            updateCommand.Transaction = tx;

                            // Populate the SQL parameters
                            AddParameter(updateCommand, "@PersistenceId", DbType.String, persistenceId);
                            AddParameter(updateCommand, "@SequenceNr", DbType.Int64, highestSeqNr);

                            // Execute the update SQL statement
                            await updateCommand.ExecuteNonQueryAsync(cancellationToken);
                            tx.Commit();
                        }
                    }
                    else tx.Commit();
                }
            }
        }
    }
}
```

### Detail Implementation

#### Reading HOCON Settings

#### Pre-Start Requirement

* Making sure all messages are processed during start-up
* Discuss delay during database connection
* Discuss stashing and initialization steps

#### Creating And Processing Custom Commands

* Discuss ReceivePluginInternal and IJournalRequest

#### Best Practices

## Implementing Akka.Persistence SnapshotStore

### Required Method Implementations

#### LoadAsync

* Discuss what this method is for

#### SaveAsync

* Discuss what this method is for

#### DeleteAsync

* Discuss what this method is for

#### DeleteAsync

* Discuss what this method is for

### Detail Implementation

* This should be very similar to Journal

## Unit Testing Journal and SnapshotStore
