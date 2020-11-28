using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Persistence.Journal;
using Akka.Persistence.Sql.Linq2Db.Config;
using Akka.Persistence.Sql.Linq2Db.Db;
using Akka.Persistence.Sql.Linq2Db.Journal.DAO;
using Akka.Persistence.Sql.Linq2Db.Journal.Types;
using Akka.Persistence.Sql.Linq2Db.Utility;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Persistence.Sql.Linq2Db.Journal
{
    public class DateTimeHelpers
    {
        private static DateTime UnixEpoch = new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc);

        public static long ToUnixEpochMillis(DateTime time)
        {
            long unixTime =
                (long) (time.ToUniversalTime() - UnixEpoch).TotalMilliseconds;
            return unixTime;
        }
        public static long UnixEpochMillis()
        {
            long currentTime =
                (long) (DateTime.UtcNow - UnixEpoch).TotalMilliseconds;
            return currentTime;
        }

        public static DateTime FromUnixEpochMillis(in long unixEpochMillis)
        {
            return UnixEpoch.AddMilliseconds(unixEpochMillis);
        }
    }

    public class Linq2DbWriteJournal : AsyncWriteJournal
    {
        public static Configuration.Config DefaultConfiguration =>
            ConfigurationFactory.FromResource<Linq2DbWriteJournal>(
                "Akka.Persistence.Sql.Linq2Db.persistence.conf");
        
        private ActorMaterializer _mat;
        private JournalConfig _journalConfig;
        private ByteArrayJournalDao _journal;
        public Linq2DbWriteJournal(Configuration.Config config)
        {
            try
            {
                _journalConfig = new JournalConfig(config);
                _mat = Materializer.CreateSystemMaterializer((ExtendedActorSystem)Context.System,
                    ActorMaterializerSettings.Create(Context.System)
                        .WithDispatcher(_journalConfig.MaterializerDispatcher)
                    ,
                    "l2dbWriteJournal"
                );
                
                try
                {
                    _journal = new ByteArrayJournalDao(
                        Context.System.Scheduler.Advanced, _mat,
                        new AkkaPersistenceDataConnectionFactory(
                            _journalConfig),
                        _journalConfig, Context.System.Serialization, Context.GetLogger());
                }
                catch (Exception e)
                {
                    Context.GetLogger().Error(e, "Error Initializing Journal!");
                    throw;
                }

                if (_journalConfig.TableConfig.AutoInitialize)
                {
                    try
                    {
                        _journal.InitializeTables();
                    }
                    catch (Exception e)
                    {
                        Context.GetLogger().Warning(e,
                            "Unable to Initialize Persistence Journal Table!");
                    }

                }
            }
            catch (Exception ex)
            {
                Context.GetLogger().Warning(ex,"Unexpected error initializing journal!");
                throw;
            }
        }

        protected override bool ReceivePluginInternal(object message)
        {
            if (message is WriteFinished wf)
            {
                writeInProgress.Remove(wf.PersistenceId);
            }
            else
            {
                return false;
            }

            return true;
        }

        public override void AroundPreRestart(Exception cause, object message)
        {
            Context.System.Log.Error(cause,
                $"Linq2Db Journal Error on {message?.GetType().ToString() ?? "null"}");
            base.AroundPreRestart(cause, message);
        }

        
        public override async Task ReplayMessagesAsync(IActorContext context, string persistenceId,
            long fromSequenceNr, long toSequenceNr, long max, Action<IPersistentRepresentation> recoveryCallback)
        {
            await _journal.MessagesWithBatch(persistenceId, fromSequenceNr,
                    toSequenceNr, _journalConfig.DaoConfig.ReplayBatchSize,
                    Option<(TimeSpan, IScheduler)>.None)
                .Take(max).SelectAsync(1,
                    t => t.IsSuccess
                        ? Task.FromResult(t.Success.Value)
                        : Task.FromException<ReplayCompletion>(
                            t.Failure.Value))
                .RunForeach(r =>
                {
                    recoveryCallback(r.repr);
                }, _mat);
            
        }

        public override async Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr)
        {
            if (writeInProgress.ContainsKey(persistenceId))
            {
                try
                {
                    await writeInProgress[persistenceId];
                }
                catch (Exception)
                {
                    //We don't have 'Recover' in C# so this is intentionally empty
                    //Basically we just wanted to wait for write to succeed OR fail
                }
                var hsn =await _journal.HighestSequenceNr(persistenceId,
                    fromSequenceNr);
                return hsn;
            }
            return await _journal.HighestSequenceNr(persistenceId, fromSequenceNr);
        }
        private Dictionary<string,Task> writeInProgress = new Dictionary<string, Task>();
        
        protected override async Task<IImmutableList<Exception>>
            WriteMessagesAsync(IEnumerable<AtomicWrite> messages)
        {
            //TODO: CurrentTimeMillis;
            var currentTime = DateTimeHelpers.UnixEpochMillis();
            var persistenceId = messages.Head().PersistenceId;
            var future = _journal.AsyncWriteMessages(messages,currentTime);
            
            writeInProgress.AddOrSet(persistenceId, future);
            var self = Self;
            
            //When we are done, we want to send a 'WriteFinished' so that
            //Sequence Number reads won't block/await/etc.
            future.ContinueWith((p) =>
                    self.Tell(new WriteFinished(persistenceId, future)),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            //But we still want to return the future from `AsyncWriteMessages`
            return await future;

        }

        protected override async Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr)
        {
            await _journal.Delete(persistenceId, toSequenceNr);
        }
    }
}