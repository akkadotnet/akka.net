// -----------------------------------------------------------------------
//  <copyright file="PersistedActor.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Akka.Actor;

namespace Akka.Persistence.Sqlite.Tests
{
    public class PersistedLegacyActor: ReceivePersistentActor
    {
        private Persisted _savedState;
        private readonly List<Persisted> _events = new List<Persisted>();
        private bool _recoveryCompleted;
        private bool _stateRequested;
        private int _persistCount;
        private IActorRef _probe;

        public PersistedLegacyActor(string id, IActorRef probe)
        {
            PersistenceId = id;
            _probe = probe;
            
            Recover<SnapshotOffer>(msg => _savedState = (Persisted) msg.Snapshot);
            Recover<Persisted>(msg => _events.Add(msg));
            
            Command<RecoveryCompleted>(_ =>
            {
                _recoveryCompleted = true;
                if(_stateRequested)
                {
                    _probe.Tell(new CurrentState(PersistenceId, _savedState, _events));
                    _stateRequested = false;
                }
            });
            
            Command<GetState>(_ =>
            {
                _stateRequested = true;
                if(_recoveryCompleted)
                {
                    _probe.Tell(new CurrentState(PersistenceId, _savedState, _events));
                    _stateRequested = false;
                }
            });
            
            Command<Persisted>(msg =>
            {
                Persist(msg, persisted =>
                {
                    _persistCount++;
                    _events.Add(persisted);
                    _probe.Tell(PersistAck.Instance);
                    if(_persistCount == 5)
                        SaveSnapshot(persisted);
                });
            });
            
            Command<SaveSnapshotSuccess>(msg =>
            {
                _savedState = _events.Last();
                DeleteMessages(msg.Metadata.SequenceNr);
            });
            
            Command<DeleteMessagesSuccess>(msg =>
            {
                var deleted = _events.Where(e => e.Payload <= msg.ToSequenceNr).ToArray();
                foreach (var e in deleted)
                    _events.Remove(e);
                _probe.Tell(new SaveSnapshotAck(_savedState, _events));
            });
        }
        
        public override string PersistenceId { get; }
        
        public sealed class Persisted
        {
            public Persisted(int payload)
            {
                Payload = payload;
            }

            public int Payload { get; }
        }
        
        public sealed class PersistAck
        {
            public static readonly PersistAck Instance = new PersistAck();
            private PersistAck() { }
        }
        
        public sealed class SaveSnapshotAck
        {
            public SaveSnapshotAck(Persisted state, List<Persisted> events)
            {
                State = state;
                Events = events;
            }

            public Persisted State { get; }
            public List<Persisted> Events { get; }
        }
        
        public sealed class GetState
        {
            public static readonly GetState Instance = new GetState();
            private GetState() { }
        }
        
        public sealed class CurrentState
        {
            public CurrentState(string pid, Persisted state, List<Persisted> events)
            {
                PersistenceId = pid;
                State = state;
                Events = events;
            }

            public string PersistenceId { get; }
            public Persisted State { get; }
            public List<Persisted> Events { get; }
        }
    }

}