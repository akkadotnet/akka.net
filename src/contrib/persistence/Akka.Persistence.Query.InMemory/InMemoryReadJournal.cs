//-----------------------------------------------------------------------
// <copyright file="InMemoryReadJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Configuration;
using Akka.Persistence.Journal;
using Akka.Streams.Dsl;

namespace Akka.Persistence.Query.InMemory
{
    public class InMemoryReadJournal :
        IPersistenceIdsQuery,
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
        
        /// <summary>
        /// <para>
        /// <see cref="PersistenceIds"/> is used for retrieving all `persistenceIds` of all
        /// persistent actors.
        /// </para>
        /// The returned event stream is unordered and you can expect different order for multiple
        /// executions of the query.
        /// <para>
        /// The stream is not completed when it reaches the end of the currently used `persistenceIds`,
        /// but it continues to push new `persistenceIds` when new persistent actors are created.
        /// Corresponding query that is completed when it reaches the end of the currently
        /// currently used `persistenceIds` is provided by <see cref="CurrentPersistenceIds"/>.
        /// </para>
        /// The inmemory write journal is notifying the query side as soon as new `persistenceIds` are
        /// created and there is no periodic polling or batching involved in this query.
        /// <para>
        /// The stream is completed with failure if there is a failure in executing the query in the
        /// backend journal.
        /// </para>
        /// </summary>
        public Source<string, NotUsed> PersistenceIds()
        {
            return Source.ActorPublisher<string>(LivePersistenceIdsPublisher.Props(_refreshInterval, _writeJournalPluginId))
                .MapMaterializedValue(_ => NotUsed.Instance)
                .Named("AllPersistenceIds");
        }

        /// <summary>
        /// Same type of query as <see cref="PersistenceIds"/> but the stream
        /// is completed immediately when it reaches the end of the "result set". Persistent
        /// actors that are created after the query is completed are not included in the stream.
        /// </summary>
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
        /// The InMemory write journal is notifying the query side as soon as events are persisted, but for
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
            Sequence seq;
            switch (offset)
            {
                case NoOffset _:
                case Sequence s when s.Value == 0:
                    seq = new Sequence(0L);
                    break;
                case Sequence s:
                    seq = new Sequence(s.Value + 1); // since offset is exclusive
                    break;
                default:
                    throw new ArgumentException($"InMemoryReadJournal does not support {offset.GetType().Name} offsets");
            }
            
            return Source.ActorPublisher<EventEnvelope>(EventsByTagPublisher.Props(tag, checked((int)seq.Value), int.MaxValue, null, _maxBufferSize, _writeJournalPluginId))
                .MapMaterializedValue(_ => NotUsed.Instance)
                .Named($"CurrentEventsByTag-{tag}");
        }

        /// <summary>
        /// <see cref="EventsByTag"/> is used for retrieving events that were marked with
        /// a given tag, e.g. all events of an Aggregate Root type.
        /// <para></para>
        /// To tag events you create an <see cref="IEventAdapter"/> that wraps the events
        /// in a <see cref="Tagged"/> with the given `tags`.
        /// <para></para>
        /// You can use <see cref="NoOffset"/> to retrieve all events with a given tag or retrieve a subset of all
        /// events by specifying a <see cref="Sequence"/>. The `offset` corresponds to an ordered sequence number for
        /// the specific tag. Note that the corresponding offset of each event is provided in the
        /// <see cref="EventEnvelope"/>, which makes it possible to resume the
        /// stream at a later point from a given offset.
        /// <para></para>
        /// The `offset` is exclusive, i.e. the event with the exact same sequence number will not be included
        /// in the returned stream.This means that you can use the offset that is returned in <see cref="EventEnvelope"/>
        /// as the `offset` parameter in a subsequent query.
        /// <para></para>
        /// In addition to the <paramref name="offset"/> the <see cref="EventEnvelope"/> also provides `persistenceId` and `sequenceNr`
        /// for each event. The `sequenceNr` is the sequence number for the persistent actor with the
        /// `persistenceId` that persisted the event. The `persistenceId` + `sequenceNr` is an unique
        /// identifier for the event.
        /// <para></para>
        /// The returned event stream is ordered by the offset (tag sequence number), which corresponds
        /// to the same order as the write journal stored the events. The same stream elements (in same order)
        /// are returned for multiple executions of the query. Deleted events are not deleted from the
        /// tagged event stream.
        /// <para></para>
        /// The stream is not completed when it reaches the end of the currently stored events,
        /// but it continues to push new events when new events are persisted.
        /// Corresponding query that is completed when it reaches the end of the currently
        /// stored events is provided by <see cref="CurrentEventsByTag"/>.
        /// <para></para>
        /// The InMemory write journal is notifying the query side as soon as tagged events are persisted, but for
        /// efficiency reasons the query side retrieves the events in batches that sometimes can
        /// be delayed up to the configured `refresh-interval`.
        /// <para></para>
        /// The stream is completed with failure if there is a failure in executing the query in the
        /// backend journal.
        /// </summary>
        public Source<EventEnvelope, NotUsed> EventsByTag(string tag, Offset offset)
        {
            Sequence seq;
            switch (offset)
            {
                case NoOffset _:
                case Sequence s when s.Value == 0:
                    seq = new Sequence(0L);
                    break;
                case Sequence s:
                    seq = new Sequence(s.Value + 1); // since offset is exclusive
                    break;
                default:
                    throw new ArgumentException($"InMemoryReadJournal does not support {offset.GetType().Name} offsets");
            }
            
            return Source.ActorPublisher<EventEnvelope>(EventsByTagPublisher.Props(tag, checked((int)seq.Value), int.MaxValue, _refreshInterval, _maxBufferSize, _writeJournalPluginId))
                .MapMaterializedValue(_ => NotUsed.Instance)
                .Named($"EventsByTag-{tag}");
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
                    seq = new Sequence(s.Value + 1); // since offset is exclusive
                    break;
                default:
                    throw new ArgumentException($"InMemoryReadJournal does not support {offset.GetType().Name} offsets");
            }

            return Source.ActorPublisher<EventEnvelope>(AllEventsPublisher.Props(checked((int)seq.Value), _refreshInterval, _maxBufferSize, _writeJournalPluginId))
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
                    seq = new Sequence(s.Value + 1); // since offset is exclusive
                    break;
                default:
                    throw new ArgumentException($"InMemoryReadJournal does not support {offset.GetType().Name} offsets");
            }

            return Source.ActorPublisher<EventEnvelope>(AllEventsPublisher.Props(checked((int)seq.Value), null, _maxBufferSize, _writeJournalPluginId))
                .MapMaterializedValue(_ => NotUsed.Instance)
                .Named("CurrentAllEvents");
        }
    }
}
