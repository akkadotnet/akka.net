//-----------------------------------------------------------------------
// <copyright file="AllPersistenceIdsPublisher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Persistence.Sql.Common.Journal;
using Akka.Streams.Actors;

namespace Akka.Persistence.Query.Sqlite
{
    internal sealed class AllPersistenceIdsPublisher : ActorPublisher<string>
    {
        [Serializable]
        private sealed class Continue
        {
            public static readonly Continue Instance = new Continue();
            private Continue() { }
        }

        public static Props Props(bool liveQuery, int maxBufferSize, string writeJournalPluginId)
        {
            return Actor.Props.Create(() => new AllPersistenceIdsPublisher(liveQuery, maxBufferSize, writeJournalPluginId));
        }

        private readonly bool _liveQuery;
        private readonly int _maxBufferSize;
        private readonly IActorRef _journalRef;

        private ImmutableArray<string> _buffer = ImmutableArray<string>.Empty;

        private AllPersistenceIdsPublisher(bool liveQuery, int maxBufferSize, string writeJournalPluginId)
        {
            _liveQuery = liveQuery;
            _maxBufferSize = maxBufferSize;
            _journalRef = Persistence.Instance.Apply(Context.System).JournalFor(writeJournalPluginId);
        }

        protected override bool Receive(object message)
        {
            return Init(message);
        }

        private bool Init(object message)
        {
            return message.Match()
                .With<Request>(_ =>
                {
                    _journalRef.Tell(SubscribeAllPersistenceIds.Instance);
                    Become(Active);
                })
                .With<Cancel>(_ => Context.Stop(Self))
                .WasHandled;
        }

        private bool Active(object message)
        {
            return message.Match()
                .With<CurrentPersistenceIds>(current =>
                {
                    _buffer = _buffer.AddRange(current.AllPersistenceIds);
                    DeliverBuffer();

                    if (!_liveQuery && _buffer.IsEmpty)
                        OnCompleteThenStop();
                })
                .With<PersistenceIdAdded>(added =>
                {
                    if (_liveQuery)
                    {
                        _buffer = _buffer.Add(added.PersistenceId);
                        DeliverBuffer();
                    }
                })
                .With<Request>(_ =>
                {
                    DeliverBuffer();
                    if (!_liveQuery && _buffer.IsEmpty)
                        OnCompleteThenStop();
                })
                .With<Cancel>(_ => Context.Stop(Self))
                .WasHandled;
        }

        private void DeliverBuffer()
        {
            if (!_buffer.IsEmpty && TotalDemand > 0)
            {
                var totalDemand = (int)TotalDemand;
                if (_buffer.Length == 1)
                {
                    // optimize for this common case
                    OnNext(_buffer[0]);
                    _buffer = ImmutableArray<string>.Empty;
                }
                else if (totalDemand <= int.MaxValue)
                {
                    for (var i = 0; i < totalDemand; i++)
                        OnNext(_buffer[i]);

                    _buffer = _buffer.RemoveRange(0, totalDemand);
                }
                else
                {
                    foreach (var element in _buffer)
                        OnNext(element);

                    _buffer = ImmutableArray<string>.Empty;
                }
            }
        }
    }
}