//-----------------------------------------------------------------------
// <copyright file="InMemoryReadJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Configuration;
using Akka.Streams.Dsl;

namespace Akka.Persistence.Query.InMemory
{
    public class InMemoryReadJournal :
        ICurrentPersistenceIdsQuery,
        ICurrentEventsByPersistenceIdQuery,
        IEventsByPersistenceIdQuery,
        ICurrentEventsByTagQuery,
        IEventsByTagQuery,
        ICurrentAllEventsQuery,
        IAllEventsQuery
    {
        public const string Identifier = "akka.persistence.query.journal.inmem";
        private readonly string _writeJournalPluginId;
        private readonly int _maxBufferSize;
        private readonly TimeSpan _refreshInterval;
        
        public static Config DefaultConfiguration()
        {
            return ConfigurationFactory.FromResource<InMemoryReadJournal>("Akka.Persistence.Query.InMemory.reference.conf");
        }
        
        public InMemoryReadJournal(Config config)
        {
            _writeJournalPluginId = config.GetString("write-plugin", null);
            _maxBufferSize = config.GetInt("max-buffer-size", 0);
            _refreshInterval = config.GetTimeSpan("refresh-interval", TimeSpan.FromSeconds(1));
        }
        
        public Source<string, NotUsed> CurrentPersistenceIds()
        {
            return Source.ActorPublisher<string>(CurrentPersistenceIdsPublisher.Props(_writeJournalPluginId))
                .MapMaterializedValue(_ => NotUsed.Instance)
                .Named("CurrentPersistenceIds");
        }

        /// <summary>
        /// Same type of query as <see cref="EventsByPersistenceId"/> but the event stream
        /// is completed immediately when it reaches the end of the "result set". Events that are
        /// stored after the query is completed are not included in the event stream.
        /// </summary>
        public Source<EventEnvelope, NotUsed> CurrentEventsByPersistenceId(string persistenceId, long fromSequenceNr, long toSequenceNr) =>
            Source.ActorPublisher<EventEnvelope>(EventsByPersistenceIdPublisher.Props(persistenceId, fromSequenceNr, toSequenceNr, null, _maxBufferSize, _writeJournalPluginId))
                .MapMaterializedValue(_ => NotUsed.Instance)
                .Named("CurrentEventsByPersistenceId-" + persistenceId);

        /// <summary>
        /// <see cref="EventsByPersistenceId"/> is used for retrieving events for a specific
        /// <see cref="PersistentActor"/> identified by <see cref="Eventsourced.PersistenceId"/>.
        /// <para>
        /// You can retrieve a subset of all events by specifying <paramref name="fromSequenceNr"/> and <paramref name="toSequenceNr"/>
        /// or use `0L` and <see cref="long.MaxValue"/> respectively to retrieve all events. Note that
        /// the corresponding sequence number of each event is provided in the
        /// <see cref="EventEnvelope"/>, which makes it possible to resume the
        /// stream at a later point from a given sequence number.
        /// </para>
        /// The returned event stream is ordered by sequence number, i.e. the same order as the
        /// <see cref="PersistentActor"/> persisted the events. The same prefix of stream elements (in same order)
        ///  are returned for multiple executions of the query, except for when events have been deleted.
        /// <para>
        /// The stream is not completed when it reaches the end of the currently stored events,
        /// but it continues to push new events when new events are persisted.
        /// Corresponding query that is completed when it reaches the end of the currently
        /// stored events is provided by <see cref="CurrentEventsByPersistenceId"/>.
        /// </para>
        /// The SQLite write journal is notifying the query side as soon as events are persisted, but for
        /// efficiency reasons the query side retrieves the events in batches that sometimes can
        /// be delayed up to the configured `refresh-interval`.
        /// <para></para>
        /// The stream is completed with failure if there is a failure in executing the query in the
        /// backend journal.
        /// </summary>
        public Source<EventEnvelope, NotUsed> EventsByPersistenceId(string persistenceId, long fromSequenceNr, long toSequenceNr) =>
            Source.ActorPublisher<EventEnvelope>(EventsByPersistenceIdPublisher.Props(persistenceId, fromSequenceNr, toSequenceNr, _refreshInterval, _maxBufferSize, _writeJournalPluginId))
                .MapMaterializedValue(_ => NotUsed.Instance)
                .Named("EventsByPersistenceId-" + persistenceId);

        /// <summary>
        /// Same type of query as <see cref="EventsByTag"/> but the event stream
        /// is completed immediately when it reaches the end of the "result set". Events that are
        /// stored after the query is completed are not included in the event stream.
        /// </summary>
        public Source<EventEnvelope, NotUsed> CurrentEventsByTag(string tag, Offset offset)
        {
            offset = offset ?? new Sequence(0L);
            switch (offset)
            {
                case Sequence seq:
                    return Source.ActorPublisher<EventEnvelope>(EventsByTagPublisher.Props(tag, seq.Value == 0 ? seq.Value : seq.Value + 1, long.MaxValue, null, _maxBufferSize, _writeJournalPluginId))
                        .MapMaterializedValue(_ => NotUsed.Instance)
                        .Named($"CurrentEventsByTag-{tag}");
                case NoOffset _:
                    return CurrentEventsByTag(tag, new Sequence(0L));
                default:
                    throw new ArgumentException($"SqlReadJournal does not support {offset.GetType().Name} offsets");
            }
        }

        public Source<EventEnvelope, NotUsed> EventsByTag(string tag, Offset offset)
        {
            offset = offset ?? new Sequence(0L);
            switch (offset)
            {
                case Sequence seq:
                    return Source.ActorPublisher<EventEnvelope>(EventsByTagPublisher.Props(tag, seq.Value == 0 ? seq.Value : seq.Value + 1, long.MaxValue, _refreshInterval, _maxBufferSize, _writeJournalPluginId))
                        .MapMaterializedValue(_ => NotUsed.Instance)
                        .Named($"EventsByTag-{tag}");
                case NoOffset _:
                    return EventsByTag(tag, new Sequence(0L));
                default:
                    throw new ArgumentException($"InMemoryReadJournal does not support {offset.GetType().Name} offsets");
            }
        }
        
        public Source<EventEnvelope, NotUsed> AllEvents(Offset offset = null)
        {
            Sequence seq;
            switch (offset)
            {
                case null:
                case NoOffset _:
                    seq = new Sequence(0L);
                    break;
                case Sequence s:
                    seq = s;
                    break;
                default:
                    throw new ArgumentException($"SqlReadJournal does not support {offset.GetType().Name} offsets");
            }

            return Source.ActorPublisher<EventEnvelope>(AllEventsPublisher.Props(seq.Value == 0 ? seq.Value : seq.Value + 1, _refreshInterval, _maxBufferSize, _writeJournalPluginId))
                .MapMaterializedValue(_ => NotUsed.Instance)
                .Named("AllEvents");
        }

        public Source<EventEnvelope, NotUsed> CurrentAllEvents(Offset offset)
        {
            Sequence seq;
            switch (offset)
            {
                case null:
                case NoOffset _:
                    seq = new Sequence(0L);
                    break;
                case Sequence s:
                    seq = s;
                    break;
                default:
                    throw new ArgumentException($"SqlReadJournal does not support {offset.GetType().Name} offsets");
            }

            return Source.ActorPublisher<EventEnvelope>(AllEventsPublisher.Props(seq.Value == 0 ? seq.Value : seq.Value + 1, null, _maxBufferSize, _writeJournalPluginId))
                .MapMaterializedValue(_ => NotUsed.Instance)
                .Named("CurrentAllEvents");
        }
    }
}