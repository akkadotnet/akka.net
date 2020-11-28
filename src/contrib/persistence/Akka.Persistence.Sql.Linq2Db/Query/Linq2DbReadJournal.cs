using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Journal;
using Akka.Persistence.Query;
using Akka.Persistence.Sql.Linq2Db.Config;
using Akka.Persistence.Sql.Linq2Db.Db;
using Akka.Persistence.Sql.Linq2Db.Journal.DAO;
using Akka.Persistence.Sql.Linq2Db.Journal.Types;
using Akka.Persistence.Sql.Linq2Db.Query.Dao;
using Akka.Persistence.Sql.Linq2Db.Query.InternalProtocol;
using Akka.Persistence.Sql.Linq2Db.Utility;
using Akka.Streams;
using Akka.Streams.Dsl;
using LanguageExt;

namespace Akka.Persistence.Sql.Linq2Db.Query
{
    public class Linq2DbReadJournal :
        IPersistenceIdsQuery,
        ICurrentPersistenceIdsQuery,
        IEventsByPersistenceIdQuery,
        ICurrentEventsByPersistenceIdQuery,
        IEventsByTagQuery,
        ICurrentEventsByTagQuery
    {
        public static string Identifier
        {
            get { return "akka.persistence.query.journal.linq2db"; }
        }

        public static Configuration.Config DefaultConfiguration =>
            ConfigurationFactory.FromResource<Linq2DbReadJournal>(
                "Akka.Persistence.Sql.Linq2Db.persistence.conf");

        private IActorRef journalSequenceActor;
        private ActorMaterializer _mat;
        private Source<long, ICancelable> delaySource;
        private ByteArrayReadJournalDao readJournalDao;
        private string writePluginId;
        private EventAdapters eventAdapters;
        private ReadJournalConfig readJournalConfig;
        private ExtendedActorSystem system;

        public Linq2DbReadJournal(ExtendedActorSystem system,
            Configuration.Config config, string configPath)
        {
            writePluginId = config.GetString("write-plugin");
            //IDK Why we need this, but we do.
            system.RegisterExtension(Persistence.Instance);
            var persist = Persistence.Instance.Get(system);
            eventAdapters = persist
                .AdaptersFor(writePluginId);
            readJournalConfig = new ReadJournalConfig(config);
            this.system = system;
            var connFact =
                new AkkaPersistenceDataConnectionFactory(readJournalConfig);
            _mat = Materializer.CreateSystemMaterializer(system,
                ActorMaterializerSettings.Create(system),
                "l2db-query-mat" + configPath);
            readJournalDao = new ByteArrayReadJournalDao(
                system.Scheduler.Advanced, _mat,
                connFact, readJournalConfig,
                new ByteArrayJournalSerializer(readJournalConfig,
                    system.Serialization,
                    readJournalConfig.PluginConfig.TagSeparator));
            journalSequenceActor = system.ActorOf(Props.Create(() =>
                    new JournalSequenceActor(readJournalDao
                        ,
                        readJournalConfig
                            .JournalSequenceRetrievalConfiguration)),
                readJournalConfig.TableConfig.TableName +
                "akka-persistence-linq2db-sequence-actor");
            delaySource = Source.Tick(
                    TimeSpan.FromSeconds(0), readJournalConfig.RefreshInterval,
                    0L)
                .Take(1);
        }

        public Source<string, NotUsed> CurrentPersistenceIds()
        {
            return readJournalDao.AllPersistenceIdsSource(long.MaxValue);
        }

        public Source<string, NotUsed> PersistenceIds()
        {
            return Source.Repeat(0L)
                .ConcatMany<long, string, NotUsed>(_ =>

                    delaySource.MapMaterializedValue(r => NotUsed.Instance)
                        .ConcatMany<long, string, NotUsed>(_ =>
                            CurrentPersistenceIds())
                ).StatefulSelectMany<string, string, NotUsed>(() =>
                {
                    var knownIds = ImmutableHashSet<string>.Empty;

                    IEnumerable<string> next(string id)
                    {
                        var xs =
                            ImmutableHashSet<string>.Empty.Add(id)
                                .Except(knownIds);
                        knownIds = knownIds.Add(id);
                        return xs;
                    }

                    return next;
                });
        }

        private IImmutableList<IPersistentRepresentation> _adaptEvents(
            IPersistentRepresentation persistentRepresentation)
        {
            var adapter =
                eventAdapters.Get(persistentRepresentation.Payload.GetType());
            return adapter
                .FromJournal(persistentRepresentation.Payload,
                    persistentRepresentation.Manifest).Events.Select(e =>
                    persistentRepresentation.WithPayload(e)).ToImmutableList();
        }

        public Source<EventEnvelope, NotUsed> CurrentEventsByPersistenceId(
            string persistenceId, long fromSequenceNr,
            long toSequenceNr)
        {
            return _eventsByPersistenceIdSource(persistenceId, fromSequenceNr,
                toSequenceNr, Akka.Util.Option<(TimeSpan, IScheduler)>.None);
        }

        public Source<EventEnvelope, NotUsed> EventsByPersistenceId(
            string persistenceId, long fromSequenceNr,
            long toSequenceNr)
        {
            return _eventsByPersistenceIdSource(persistenceId, fromSequenceNr,
                toSequenceNr,
                new Akka.Util.Option<(TimeSpan, IScheduler)>((
                    readJournalConfig.RefreshInterval, system.Scheduler)));
        }

        private Source<EventEnvelope, NotUsed> _eventsByPersistenceIdSource(
            string persistenceId, long fromSequenceNr, long toSequenceNr,
            Akka.Util.Option<(TimeSpan, IScheduler)> refreshInterval)
        {
            var batchSize = readJournalConfig.MaxBufferSize;
            return readJournalDao.MessagesWithBatch(persistenceId,
                    fromSequenceNr,
                    toSequenceNr, batchSize, refreshInterval)
                .SelectAsync<Akka.Util.Try<ReplayCompletion>, ReplayCompletion, NotUsed>(1,
                    reprAndOrdNr => Task.FromResult<ReplayCompletion>(reprAndOrdNr.Get()))
                .SelectMany((ReplayCompletion r) => _adaptEvents(r.repr)
                    .Select(p => new {repr = r.repr, ordNr = r.Ordering}))
                .Select(r => new EventEnvelope(new Sequence(r.ordNr),
                    r.repr.PersistenceId, r.repr.SequenceNr, r.repr.Payload));
        }

        public Source<EventEnvelope, NotUsed> CurrentEventsByTag(string tag,
            Offset offset)
        {
            return _currentEventsByTag(tag, (offset as Sequence)?.Value ?? 0);
        }

        private Source<EventEnvelope, NotUsed> _currentJournalEventsByTag(
            string tag, long offset, long max, MaxOrderingId latestOrdering)
        {
            if (latestOrdering.Max < offset)
            {
                return Source.Empty<EventEnvelope>();
            }

            return readJournalDao
                .EventsByTag(tag, offset, latestOrdering.Max, max).SelectAsync(
                    1,
                    r =>
                        Task.FromResult(r.Get())
                ).SelectMany<(IPersistentRepresentation,
                    IImmutableSet<string>, long), EventEnvelope, NotUsed>(
                    (a)
                        =>
                    {
                        return _adaptEvents(a.Item1).Select(r =>
                            new EventEnvelope(new Sequence(a.Item3),
                                r.PersistenceId,
                                r.SequenceNr, r.Payload));
                    });
        }

        private Source<EventEnvelope, NotUsed> _eventsByTag(string tag,
            long offset, long? terminateAfterOffset)
        {
            var askTimeout = readJournalConfig
                .JournalSequenceRetrievalConfiguration.AskTimeout;
            var batchSize = readJournalConfig.MaxBufferSize;
            return Source
                .UnfoldAsync<(long, FlowControl), IImmutableList<EventEnvelope>
                >((offset, FlowControl.Continue.Instance),
                    uf =>
                    {
                        async Task<Akka.Util.Option<((long, FlowControl),
                            IImmutableList<EventEnvelope>)>> retrieveNextBatch()
                        {
                            var queryUntil =
                                await journalSequenceActor.Ask<MaxOrderingId>(
                                    new GetMaxOrderingId(), askTimeout);
                            var xs =
                                await _currentJournalEventsByTag(tag, uf.Item1,
                                    batchSize,
                                    queryUntil).RunWith(
                                    Sink.Seq<EventEnvelope>(),
                                    _mat);
                            var hasMoreEvents = xs.Count == batchSize;
                            FlowControl nextControl = null;
                            if (terminateAfterOffset.HasValue)
                            {
                                if (!hasMoreEvents &&
                                    terminateAfterOffset.Value <=
                                    queryUntil.Max)
                                    nextControl = FlowControl.Stop.Instance;
                                if (xs.Exists(r =>
                                    (r.Offset is Sequence s) &&
                                    s.Value >= terminateAfterOffset.Value))
                                    nextControl = FlowControl.Stop.Instance;
                            }

                            if (nextControl == null)
                            {
                                nextControl = hasMoreEvents
                                    ? (FlowControl) FlowControl.Continue
                                        .Instance
                                    : FlowControl.ContinueDelayed.Instance;
                            }

                            var nextStartingOffset = (xs.Count == 0)
                                ? Math.Max(uf.Item1, queryUntil.Max)
                                : xs.Select(r => r.Offset as Sequence)
                                    .Where(r => r != null).Max(t => t.Value);
                            return new
                                Akka.Util.Option<((long nextStartingOffset,
                                    FlowControl
                                    nextControl), IImmutableList<EventEnvelope>
                                    xs)
                                >((
                                    (nextStartingOffset, nextControl), xs));
                        }

                        switch (uf.Item2)
                        {
                            case FlowControl.Stop _:
                                return
                                    Task.FromResult(Akka.Util
                                        .Option<((long, FlowControl),
                                            IImmutableList<EventEnvelope>)>
                                        .None);
                            case FlowControl.Continue _:
                                return retrieveNextBatch();
                            case FlowControl.ContinueDelayed _:
                                return Akka.Pattern.FutureTimeoutSupport.After(
                                    readJournalConfig.RefreshInterval,
                                    system.Scheduler,
                                    retrieveNextBatch);
                            default:
                                return
                                    Task.FromResult(Akka.Util
                                        .Option<((long, FlowControl),
                                            IImmutableList<EventEnvelope>)>
                                        .None);
                        }
                    }).SelectMany(r => r);
        }

        private Source<EventEnvelope, NotUsed> _currentEventsByTag(string tag,
            long offset)
        {
            return Source.FromTask(readJournalDao.MaxJournalSequence())
                .ConcatMany(
                    maxInDb =>
                    {
                        return _eventsByTag(tag, offset, Some.Create(maxInDb));
                    });
        }

        public Source<EventEnvelope, NotUsed> EventsByTag(string tag,
            Offset offset)
        {
            long theOffset = 0;
            if (offset is Sequence s)
            {
                theOffset = s.Value;
            }

            return _eventsByTag(tag, theOffset, null);
        }
    }
}