//-----------------------------------------------------------------------
// <copyright file="OptimizedRecoverySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Xunit;

namespace Akka.Persistence.Tests
{
    public class OptimizedRecoverySpec : PersistenceSpec
    {

        #region Internal test classes

        internal class TakeSnapshot
        { }

        internal class Save
        {
            public string Data { get; }

            public Save(string data)
            {
                Data = data;
            }
        }

        internal class Saved : IEquatable<Saved>
        {
            public string Data { get; }
            public long SeqNr { get; }

            public Saved(string data, long seqNr)
            {
                Data = data;
                SeqNr = seqNr;
            }

            public bool Equals(Saved other)
            {
                return other != null && Data.Equals(other.Data) && SeqNr.Equals(other.SeqNr);
            }
        }

        internal sealed class PersistFromRecoveryCompleted
        {
            public static PersistFromRecoveryCompleted Instance { get; } = new PersistFromRecoveryCompleted();
            private PersistFromRecoveryCompleted() { }
        }

        internal class TestPersistentActor : NamedPersistentActor
        {
            private readonly Recovery _recovery;
            private readonly IActorRef _probe;
            private string state = string.Empty;

            public override Recovery Recovery => _recovery;

            public TestPersistentActor(string name, Recovery recovery, IActorRef probe)
                : base(name)
            {
                _recovery = recovery;
                _probe = probe;
            }

            protected override bool ReceiveCommand(object message)
            {
                switch (message)
                {
                    case TakeSnapshot _:
                        SaveSnapshot(state);
                        return true;
                    case SaveSnapshotSuccess s:
                        _probe.Tell(s);
                        return true;
                    case GetState _:
                        _probe.Tell(state);
                        return true;
                    case Save s:
                        Persist(new Saved(s.Data, LastSequenceNr + 1), evt =>
                        {
                            state = state + evt.Data;
                            _probe.Tell(evt);
                        });
                        return true;
                }
                return false;
            }

            protected override bool ReceiveRecover(object message)
            {
                switch (message)
                {
                    case SnapshotOffer s:
                        _probe.Tell(s);
                        state = s.Snapshot.ToString();
                        return true;
                    case Saved evt:
                        state = state + evt.Data;
                        _probe.Tell(evt);
                        return true;
                    case RecoveryCompleted _:
                        if (IsRecovering) throw new InvalidOperationException($"Expected !IsRecovering in RecoveryCompleted");
                        _probe.Tell(RecoveryCompleted.Instance);
                        // Verify that persist can be used here
                        Persist(PersistFromRecoveryCompleted.Instance, _ => _probe.Tell(PersistFromRecoveryCompleted.Instance));
                        return true;
                }
                return false;
            }
        }

        #endregion

        private string persistenceId = "p1";

        public OptimizedRecoverySpec() : base(Configuration("OptimizedRecoverySpec"))
        {
            var pref = ActorOf(Props.Create(() => new TestPersistentActor(persistenceId, Recovery.Default, TestActor)));
            ExpectMsg<RecoveryCompleted>();
            ExpectMsg<PersistFromRecoveryCompleted>();
            pref.Tell(new Save("a"));
            pref.Tell(new Save("b"));
            ExpectMsg(new Saved("a", 2));
            ExpectMsg(new Saved("b", 3));
            pref.Tell(new TakeSnapshot());
            ExpectMsg<SaveSnapshotSuccess>();
            pref.Tell(new Save("c"));
            ExpectMsg(new Saved("c", 4));
            pref.Tell(GetState.Instance);
            ExpectMsg("abc");
        }

        [Fact]
        public void Persistence_must_get_RecoveryCompleted_but_no_SnapshotOffer_and_events_when_Recovery_none()
        {
            var pref = ActorOf(Props.Create(() => new TestPersistentActor(persistenceId, Recovery.None, TestActor)));
            ExpectMsg<RecoveryCompleted>();
            ExpectMsg<PersistFromRecoveryCompleted>();

            // and highest sequence number should be used, PersistFromRecoveryCompleted is 5
            pref.Tell(new Save("d"));
            ExpectMsg(new Saved("d", 6));
            pref.Tell(GetState.Instance);
            ExpectMsg("d");
        }
    }
}
