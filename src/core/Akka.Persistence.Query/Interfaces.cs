//-----------------------------------------------------------------------
// <copyright file="Interfaces.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Streams.Dsl;

namespace Akka.Persistence.Query
{
    /// <summary>
    /// API for reading persistent events and information derived
    /// from stored persistent events.
    /// <para>
    /// The purpose of the API is not to enforce compatibility between different
    /// journal implementations, because the technical capabilities may be very different.
    /// The interface is very open so that different journals may implement specific queries.
    /// </para>
    /// There are a few pre-defined queries that a query implementation may implement,
    /// such as <see cref="IEventsByPersistenceIdQuery"/>, <see cref="IPersistenceIdsQuery"/> and <see cref="IEventsByTagQuery"/>
    /// Implementation of these queries are optional and query (journal) plugins may define
    /// their own specialized queries by implementing other methods.
    /// <example>
    /// var journal = PersistenceQuery.Get(system).ReadJournalFor&lt;SomeCoolReadJournal&gt;(queryPluginConfigPath)
    /// var events = journal.Query(new EventsByTag("mytag", 0L))
    /// </example>
    /// </summary>
    public interface IReadJournal
    {
    }

    /// <summary>
    /// A plugin may optionally support this query by implementing this trait.
    /// </summary>
    public interface IEventsByTagQuery : IReadJournal
    {
        /// <summary>
        /// Query events that have a specific tag. A tag can for example correspond to an
        /// aggregate root type (in DDD terminology).
        /// <para>
        /// The consumer can keep track of its current position in the event stream by storing the
        /// <paramref name="offset"/> and restart the query from a given <paramref name="offset"/> after a crash/restart.
        /// </para>
        /// The exact meaning of the <paramref name="offset"/> depends on the journal and must be documented by the
        /// read journal plugin. It may be a sequential id number that uniquely identifies the
        /// position of each event within the event stream. Distributed data stores cannot easily
        /// support those semantics and they may use a weaker meaning. For example it may be a
        /// timestamp (taken when the event was created or stored). Timestamps are not unique and
        /// not strictly ordered, since clocks on different machines may not be synchronized.
        /// <para>
        /// The returned event stream should be ordered by <paramref name="offset"/> if possible, but this can also be
        /// difficult to fulfill for a distributed data store. The order must be documented by the
        /// read journal plugin.
        /// </para>
        /// The stream is not completed when it reaches the end of the currently stored events,
        /// but it continues to push new events when new events are persisted.
        /// Corresponding query that is completed when it reaches the end of the currently
        /// stored events is provided by <see cref="ICurrentEventsByTagQuery.CurrentEventsByTag"/>.
        /// </summary>
        Source<EventEnvelope, NotUsed> EventsByTag(string tag, Offset offset);
    }

    /// <summary>
    /// A plugin may optionally support this query by implementing this trait.
    /// </summary>
    public interface ICurrentEventsByTagQuery : IReadJournal
    {
        /// <summary>
        /// Same type of query as <see cref="IEventsByTagQuery.EventsByTag"/> but the event stream
        /// is completed immediately when it reaches the end of the "result set". Events that are
        /// stored after the query is completed are not included in the event stream.
        /// </summary>
        Source<EventEnvelope, NotUsed> CurrentEventsByTag(string tag, Offset offset);
    }

    public interface ICurrentAllEventsQuery : IReadJournal
    {
        Source<EventEnvelope, NotUsed> CurrentAllEvents(Offset offset);
    }

    public interface IAllEventsQuery : IReadJournal
    {
        Source<EventEnvelope, NotUsed> AllEvents(Offset offset);
    }

    /// <summary>
    /// A plugin may optionally support this query by implementing this trait.
    /// </summary>
    public interface IEventsByPersistenceIdQuery : IReadJournal
    {
        /// <summary>
        /// Query events for a specific <see cref="PersistentActor"/> identified by <paramref name="persistenceId"/>.
        /// <para>
        /// You can retrieve a subset of all events by specifying <paramref name="fromSequenceNr"/> and <paramref name="toSequenceNr"/>
        /// or use <c>0L</c> and <see cref="long.MaxValue"/> respectively to retrieve all events.
        /// </para>
        /// The returned event stream should be ordered by sequence number.
        /// <para>
        /// The stream is not completed when it reaches the end of the currently stored events,
        /// but it continues to push new events when new events are persisted.
        /// Corresponding query that is completed when it reaches the end of the currently
        /// stored events is provided by <see cref="ICurrentEventsByPersistenceIdQuery.CurrentEventsByPersistenceId"/>.
        /// </para>
        /// </summary>
        Source<EventEnvelope, NotUsed> EventsByPersistenceId(string persistenceId, long fromSequenceNr, long toSequenceNr);
    }

    /// <summary>
    /// A plugin may optionally support this query by implementing this trait.
    /// </summary>
    public interface ICurrentEventsByPersistenceIdQuery : IReadJournal
    {
        /// <summary>
        /// Same type of query as <see cref="IEventsByPersistenceIdQuery.EventsByPersistenceId"/>
        /// but the event stream is completed immediately when it reaches the end of
        /// the "result set". Events that are stored after the query is completed are
        /// not included in the event stream.
        /// </summary>
        Source<EventEnvelope, NotUsed> CurrentEventsByPersistenceId(string persistenceId, long fromSequenceNr, long toSequenceNr);
    }

    /// <summary>
    /// A plugin may optionally support this query by implementing this trait.
    /// </summary>
    public interface ICurrentPersistenceIdsQuery : IReadJournal
    {
        /// <summary>
        /// Same type of query as <see cref="IPersistenceIdsQuery.PersistenceIds"/> but the stream
        /// is completed immediately when it reaches the end of the "result set". Persistent
        /// actors that are created after the query is completed are not included in the stream.
        /// </summary>
        Source<string, NotUsed> CurrentPersistenceIds();
    }

    public interface IPersistenceIdsQuery : IReadJournal
    {
        /// <summary>
        /// Query all <see cref="PersistentActor"/> identifiers, i.e. as defined by the
        /// `persistenceId` of the <see cref="PersistentActor"/>.
        /// 
        /// The stream is not completed when it reaches the end of the currently used `persistenceIds`,
        /// but it continues to push new `persistenceIds` when new persistent actors are created.
        /// Corresponding query that is completed when it reaches the end of the currently
        /// currently used `persistenceIds` is provided by <see cref="ICurrentPersistenceIdsQuery.CurrentPersistenceIds"/>.
        /// </summary>
        Source<string, NotUsed> PersistenceIds();
    }
}
