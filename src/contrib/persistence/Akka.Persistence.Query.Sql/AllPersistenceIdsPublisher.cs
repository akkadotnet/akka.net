//-----------------------------------------------------------------------
// <copyright file="AllPersistenceIdsPublisher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Persistence.Sql.Common.Journal;
using Akka.Streams.Actors;

namespace Akka.Persistence.Query.Sql
{
    internal sealed class AllPersistenceIdsPublisher : ActorPublisher<string>
    {
        public static Props Props(bool liveQuery, string writeJournalPluginId)
        {
            return Actor.Props.Create(() => new AllPersistenceIdsPublisher(liveQuery, writeJournalPluginId));
        }

        private readonly bool _liveQuery;
        private readonly IActorRef _journalRef;

        private readonly DeliveryBuffer<string> _buffer;

        public AllPersistenceIdsPublisher(bool liveQuery, string writeJournalPluginId)
        {
            _liveQuery = liveQuery;
            _buffer = new DeliveryBuffer<string>(OnNext);
            _journalRef = Persistence.Instance.Apply(Context.System).JournalFor(writeJournalPluginId);
        }

        protected override bool Receive(object message) => message.Match()
            .With<Request>(_ =>
            {
                _journalRef.Tell(SubscribeAllPersistenceIds.Instance);
                Become(Active);
            })
            .With<Cancel>(_ => Context.Stop(Self))
            .WasHandled;

        private bool Active(object message) => message.Match()
            .With<CurrentPersistenceIds>(current =>
            {
                _buffer.AddRange(current.AllPersistenceIds);
                _buffer.DeliverBuffer(TotalDemand);

                if (!_liveQuery && _buffer.IsEmpty)
                    OnCompleteThenStop();
            })
            .With<PersistenceIdAdded>(added =>
            {
                if (_liveQuery)
                {
                    _buffer.Add(added.PersistenceId);
                    _buffer.DeliverBuffer(TotalDemand);
                }
            })
            .With<Request>(_ =>
            {
                _buffer.DeliverBuffer(TotalDemand);
                if (!_liveQuery && _buffer.IsEmpty)
                    OnCompleteThenStop();
            })
            .With<Cancel>(_ => Context.Stop(Self))
            .WasHandled;
    }
}