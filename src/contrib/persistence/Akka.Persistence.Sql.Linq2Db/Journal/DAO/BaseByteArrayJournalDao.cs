using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence.Sql.Linq2Db.Config;
using Akka.Persistence.Sql.Linq2Db.Db;
using Akka.Persistence.Sql.Linq2Db.Journal.Types;
using Akka.Persistence.Sql.Linq2Db.Serialization;
using Akka.Streams;
using Akka.Streams.Dsl;
using LanguageExt;
using LinqToDB;
using LinqToDB.Data;
using static LanguageExt.Prelude;
using Seq = LanguageExt.Seq;

namespace Akka.Persistence.Sql.Linq2Db.Journal.DAO
{
    public abstract class BaseByteArrayJournalDao :
        BaseJournalDaoWithReadMessages,
        IJournalDaoWithUpdates
    {

        public ISourceQueueWithComplete<WriteQueueEntry> WriteQueue;
        protected JournalConfig _journalConfig;
        protected FlowPersistentReprSerializer<JournalRow> Serializer;

        private Lazy<object> logWarnAboutLogicalDeletionDeprecation =
            new Lazy<object>(() => { return new object(); },
                LazyThreadSafetyMode.None);

        public bool logicalDelete;
        protected readonly ILoggingAdapter _logger;

        protected BaseByteArrayJournalDao(IAdvancedScheduler sched,
            IMaterializer materializerr,
            AkkaPersistenceDataConnectionFactory connectionFactory,
            JournalConfig config, ByteArrayJournalSerializer serializer, ILoggingAdapter logger) : base(
            sched, materializerr, connectionFactory)
        {
            _logger = logger;
            _journalConfig = config;
            logicalDelete = _journalConfig.DaoConfig.LogicalDelete;
            Serializer = serializer;
            //Due to C# rules we have to initialize WriteQueue here
            //Keeping it here vs init function prevents accidental moving of init
            //to where variables aren't set yet.
            WriteQueue = Source
                .Queue<WriteQueueEntry
                >(_journalConfig.DaoConfig.BufferSize,
                    OverflowStrategy.DropNew)
                .BatchWeighted(_journalConfig.DaoConfig.BatchSize,
                    cf=>cf.Rows.Count,
                    r => new WriteQueueSet(
                        new List<TaskCompletionSource<NotUsed>>(new[]
                            {r.TCS}), r.Rows),
                    (oldRows, newRows) =>
                    {
                        oldRows.TCS.Add(newRows.TCS);
                        oldRows.Rows = oldRows.Rows.Concat(newRows.Rows);
                        return oldRows; //.Concat(newRows.Item2).ToList());
                    })
                .SelectAsync(_journalConfig.DaoConfig.Parallelism,
                    async (promisesAndRows) =>
                    {
                        try
                        {
                            await WriteJournalRows(promisesAndRows.Rows);
                            foreach (var taskCompletionSource in promisesAndRows
                                .TCS)
                            {
                                taskCompletionSource.TrySetResult(
                                    NotUsed.Instance);
                            }
                        }
                        catch (Exception e)
                        {
                            foreach (var taskCompletionSource in promisesAndRows
                                .TCS)
                            {
                                taskCompletionSource.TrySetException(e);
                            }
                        }

                        return NotUsed.Instance;
                    }).ToMaterialized(
                    Sink.Ignore<NotUsed>(), Keep.Left).Run(mat);
        }



        private async Task<NotUsed> QueueWriteJournalRows(Seq<JournalRow> xs)
        {
            TaskCompletionSource<NotUsed> promise =
                new TaskCompletionSource<NotUsed>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                    );
            //Send promise and rows into queue. If the Queue takes it,
            //It will write the Promise state when finished writing (or failing)
            var result =
                await WriteQueue.OfferAsync(new WriteQueueEntry(promise, xs));
            {
                switch (result)
                {
                    case QueueOfferResult.Enqueued _:
                        break;
                    case QueueOfferResult.Failure f:
                        promise.TrySetException(
                            new Exception("Failed to write journal row batch",
                                f.Cause));
                        break;
                    case QueueOfferResult.Dropped _:
                        promise.TrySetException(new Exception(
                            $"Failed to enqueue journal row batch write, the queue buffer was full ({_journalConfig.DaoConfig.BufferSize} elements)"));
                        break;
                    case QueueOfferResult.QueueClosed _:
                        promise.TrySetException(new Exception(
                            "Failed to enqueue journal row batch write, the queue was closed."));
                        break;
                }

                return await promise.Task;
            }
        }

        private async Task WriteJournalRows(Seq<JournalRow> xs)
        {
            using (var db = _connectionFactory.GetConnection())
            {
                //hot path:
                //If we only have one row, penalty for BulkCopy
                //Isn't worth it due to insert caching/etc.
                if (xs.Count > 1)
                {
                    await db.GetTable<JournalRow>()
                        .BulkCopyAsync(
                            new BulkCopyOptions()
                            {
                                BulkCopyType =
                                    xs.Count > _journalConfig.DaoConfig
                                        .MaxRowByRowSize
                                        ? BulkCopyType.Default
                                        : BulkCopyType.MultipleRows,
                                UseInternalTransaction = true 
                            }, xs);
                }
                else if (xs.Count > 0)
                {
                    await db.InsertAsync(xs.Head);
                }
            }

        }
        
        public async Task<IImmutableList<Exception>> AsyncWriteMessages(
            IEnumerable<AtomicWrite> messages, long timeStamp = 0)
        {

            var serializedTries = Serializer.Serialize(messages, timeStamp);

            //Just a little bit of magic here;
            //.ToList() keeps it all working later for whatever reason
            //while still keeping our allocations in check.
            var rows = Seq(serializedTries.SelectMany(serializedTry =>
                    serializedTry.Success.GetOrElse(new List<JournalRow>(0)))
                .ToList());

        
            
            return await QueueWriteJournalRows(rows).ContinueWith(task =>
                {
                    //We actually are trying to interleave our tasks here...
                    //Basically, if serialization failed our task will likely
                    //Show success
                    //But we instead should display the serialization failure
                    return serializedTries.Select(r =>
                        r.IsSuccess
                            ? (task.IsFaulted
                                ? TryUnwrapException(task.Exception)
                                : null)
                            : r.Failure.Value).ToImmutableList();
                }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        protected static Exception TryUnwrapException(Exception e)
        {
            var aggregateException = e as AggregateException;
            if (aggregateException != null)
            {
                aggregateException = aggregateException.Flatten();
                if (aggregateException.InnerExceptions.Count == 1)
                    return aggregateException.InnerExceptions[0];
            }

            return e;
        }



        public async Task Delete(string persistenceId, long maxSequenceNr)
        {
            if (logicalDelete)
            {
                var obj = logWarnAboutLogicalDeletionDeprecation.Value;
            }
            
            {
                using (var db = _connectionFactory.GetConnection())
                {
                    var transaction =await db.BeginTransactionAsync();
                    try
                    {
                        await db.GetTable<JournalRow>()
                            .Where(r =>
                                r.persistenceId == persistenceId &&
                                (r.sequenceNumber <= maxSequenceNr))
                            .Set(r => r.deleted, true)
                            .UpdateAsync();
                        var maxMarkedDeletion =
                            await MaxMarkedForDeletionMaxPersistenceIdQuery(db,
                                persistenceId).FirstOrDefaultAsync();
                        if (_journalConfig.DaoConfig.SqlCommonCompatibilityMode)
                        {
                            await db.GetTable<JournalMetaData>()
                                .InsertOrUpdateAsync(() => new JournalMetaData()
                                    {
                                        PersistenceId = persistenceId,
                                        SequenceNumber =
                                            maxMarkedDeletion
                                    },
                                    jmd => new JournalMetaData()
                                    {
                                        PersistenceId = persistenceId,
                                        SequenceNumber = maxMarkedDeletion
                                    },
                                    () => new JournalMetaData()
                                    {
                                        PersistenceId = persistenceId,
                                        SequenceNumber = maxMarkedDeletion
                                    });
                        }

                        if (logicalDelete == false)
                        {
                            await db.GetTable<JournalRow>()
                                .Where(r =>
                                    r.persistenceId == persistenceId &&
                                    (r.sequenceNumber <= maxSequenceNr &&
                                     r.sequenceNumber <
                                     maxMarkedDeletion
                                         )).DeleteAsync();
                        }

                        if (_journalConfig.DaoConfig.SqlCommonCompatibilityMode)
                        {
                            await db.GetTable<JournalMetaData>()
                                .Where(r =>
                                    r.PersistenceId == persistenceId &&
                                    r.SequenceNumber <
                                    maxMarkedDeletion)
                                .DeleteAsync();
                        }

                        await transaction.CommitAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex,"Error on delete!");
                        try
                        {
                            await transaction.RollbackAsync();
                        }
                        catch (Exception )
                        {
                            //If rollback fails, Don't throw as it will mask
                            //original exception
                        }
                        
                        throw;
                    }
                }
            }
        }
        
        protected IQueryable<long> MaxMarkedForDeletionMaxPersistenceIdQuery(DataConnection connection,
            string persistenceId)
        {
            return connection.GetTable<JournalRow>()
                    .Where(r => r.persistenceId == persistenceId && r.deleted)
                    .OrderByDescending(r => r.sequenceNumber)
                    .Select(r => r.sequenceNumber).Take(1);
        }

        private IQueryable<long> MaxSeqNumberForPersistenceIdQuery(
            DataConnection db, string persistenceId, long minSequenceNumber = 0)
        {

            var queryable = db.GetTable<JournalRow>()
                .Where(r => r.persistenceId == persistenceId).Select(r =>
                    new
                    {
                        SequenceNumber = r.sequenceNumber,
                        PersistenceId = r.persistenceId
                    });
            if (minSequenceNumber != 0)
            {
                queryable = queryable.Where(r =>
                    r.SequenceNumber > minSequenceNumber);
            }

            if (_journalConfig.DaoConfig.SqlCommonCompatibilityMode)
            {
                var nextQuery = db.GetTable<JournalMetaData>()
                    .Where(r =>
                        r.SequenceNumber > minSequenceNumber &&
                        r.PersistenceId == persistenceId);
                queryable = queryable.Union(nextQuery.Select(md =>
                        new
                        {
                            SequenceNumber = md.SequenceNumber,
                            PersistenceId = md.PersistenceId
                        }));
            }

            return queryable.OrderByDescending(r => r.SequenceNumber)
                .Select(r => r.SequenceNumber).Take(1);
        }

        public async Task<Done> Update(string persistenceId, long sequenceNr,
            object payload)
        {
            var write = new Persistent(payload, sequenceNr, persistenceId);
            var serialize = Serializer.Serialize(write);
            if (serialize.IsSuccess)
            {
                throw new ArgumentException(
                    $"Failed to serialize {write.GetType()} for update of {persistenceId}] @ {sequenceNr}",
                    serialize.Failure.Value);
            }

            using (var db = _connectionFactory.GetConnection())
            {
                await db.GetTable<JournalRow>()
                    .Where(r =>
                        r.persistenceId == persistenceId &&
                        r.sequenceNumber == write.SequenceNr)
                    .Set(r => r.message, serialize.Get().message)
                    .UpdateAsync();
                return Done.Instance;
            }
        }

        public async Task<long> HighestSequenceNr(string persistenceId,
            long fromSequenceNr)
        {
            using (var db = _connectionFactory.GetConnection())
            {
                return await MaxSeqNumberForPersistenceIdQuery(db,
                    persistenceId,
                    fromSequenceNr).FirstOrDefaultAsync();
            }
        }

        

        public override
            async Task<Source<Util.Try<ReplayCompletion>, NotUsed>>
            Messages(DataConnection db, string persistenceId,
                long fromSequenceNr, long toSequenceNr,
                long max)
        {

            {
                IQueryable<JournalRow> query = db.GetTable<JournalRow>()
                    .Where(r =>
                        r.persistenceId == persistenceId &&
                        r.sequenceNumber >= fromSequenceNr &&
                        r.sequenceNumber <= toSequenceNr &&
                        r.deleted == false)
                    .OrderBy(r => r.sequenceNumber);
                if (max <= int.MaxValue)
                {
                    query = query.Take((int) max);
                }

                
                //TODO: Is there a better way to do this async?
                //return Source
                //    .FromObservable(query.AsAsyncEnumerable().ToObservable())
                
                return Source.From(await query.ToListAsync())
                    .Via(
                        Serializer.DeserializeFlow()).Select(sertry =>
                    {
                        if (sertry.IsSuccess)
                        {
                            return new
                                Util.Try<ReplayCompletion>(
                                    new ReplayCompletion()
                                    {
                                        repr = sertry.Success.Value.Item1,
                                        Ordering = sertry.Success.Value.Item3
                                    });
                        }
                        else
                        {
                            return new
                                Util.Try<ReplayCompletion>(
                                    sertry.Failure.Value);
                        }
                    });
            }
        }
    }
}