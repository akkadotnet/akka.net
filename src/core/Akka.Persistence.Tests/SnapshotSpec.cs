//-----------------------------------------------------------------------
// <copyright file="SnapshotSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Persistence.Internal;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Persistence.Tests
{
    public class SnapshotSpec : PersistenceSpec
    {
        #region Internal test classes

        internal class TakeSnapshot
        {
            public static readonly TakeSnapshot Instance = new TakeSnapshot();
            private TakeSnapshot()
            {
            }
        }

        internal class SaveSnapshotTestActor : NamedPersistentActor
        {
            private readonly IActorRef _probe;

            protected ImmutableArray<string> State = ImmutableArray<string>.Empty;

            public SaveSnapshotTestActor(string name, IActorRef probe)
                : base(name)
            {
                _probe = probe;
            }

            protected override bool ReceiveRecover(object message)
            {
                return message.Match()
                    .With<SnapshotOffer>(offer =>
                    {
                        State = offer.Snapshot.AsInstanceOf<ImmutableArray<string>>();
                    })
                    .With<string>(m => State = State.AddFirst(m + "-" + LastSequenceNr))
                    .WasHandled;
            }

            protected override bool ReceiveCommand(object message)
            {
                return message.Match()
                    .With<string>(payload => Persist(payload, _ =>
                    {
                        State = State.AddFirst(payload + "-" + LastSequenceNr);
                    }))
                    .With<TakeSnapshot>(_ => SaveSnapshot(State))
                    .With<SaveSnapshotSuccess>(s => _probe.Tell(s.Metadata.SequenceNr))
                    .With<GetState>(_ => _probe.Tell(State.Reverse().ToArray()))
                    .WasHandled;
            }
        }

        internal class LoadSnapshotTestActor : NamedPersistentActor
        {
            private readonly Recovery _recovery;
            private readonly IActorRef _probe;

            public LoadSnapshotTestActor(string name, Recovery recovery, IActorRef probe)
                : base(name)
            {
                _probe = probe;
                _recovery = recovery;
            }

            protected override bool ReceiveRecover(object message)
            {
                return message.Match()
                    .With<string>(payload => _probe.Tell(payload + "-" + LastSequenceNr))
                    .With<SnapshotOffer>(offer => _probe.Tell(offer))
                    .Default(other => _probe.Tell(other))
                    .WasHandled;
            }

            protected override bool ReceiveCommand(object message)
            {
                return message.Match()
                    .With<string>(payload =>
                    {
                        if (payload == "done")
                            _probe.Tell("done");
                        else
                            Persist(payload, _ => _probe.Tell(payload + "-" + LastSequenceNr));
                    })
                    .With<SnapshotOffer>(offer => _probe.Tell(offer))
                    .Default(other => _probe.Tell(other))
                    .WasHandled;
            }

            protected override void PreStart() { }


            public override Recovery Recovery
            {
                get { return _recovery; }
            }
        }

        internal class IgnoringSnapshotTestPersistentActor : NamedPersistentActor
        {
            private readonly Recovery _recovery;
            private readonly IActorRef _probe;

            public IgnoringSnapshotTestPersistentActor(string name, Recovery recovery, IActorRef probe)
                : base(name)
            {
                _probe = probe;
                _recovery = recovery;
            }

            protected override bool ReceiveRecover(object message)
            {
                switch(message)
                {
                   case string payload:
                        _probe.Tell($"{payload}-{LastSequenceNr}");
                        return true;
                    case object other when !(other is SnapshotOffer):
                        _probe.Tell(other);
                        return true;
                }
                return false;
            }

            protected override bool ReceiveCommand(object message)
            {
                switch(message)
                {
                    case string payload when payload == "done":
                        _probe.Tell("done");
                        return true;
                    case string payload:
                        Persist(payload, _ => _probe.Tell($"{payload}-{LastSequenceNr}"));
                        return true;
                    default:
                        _probe.Tell(message);
                        return true;
                }
            }

            public override Recovery Recovery => _recovery; 
        }

        public sealed class DeleteOne
        {
            public DeleteOne(SnapshotMetadata metadata)
            {
                Metadata = metadata;
            }

            public SnapshotMetadata Metadata { get; private set; }
        }

        public sealed class DeleteMany
        {
            public DeleteMany(SnapshotSelectionCriteria criteria)
            {
                Criteria = criteria;
            }

            public SnapshotSelectionCriteria Criteria { get; private set; }
        }

        internal class DeleteSnapshotTestActor : LoadSnapshotTestActor
        {
            public DeleteSnapshotTestActor(string name, Recovery recovery, IActorRef probe)
                : base(name, recovery, probe)
            {
            }

            protected override bool ReceiveCommand(object message)
            {
                return ReceiveDelete(message) || base.ReceiveCommand(message);
            }

            protected bool ReceiveDelete(object message)
            {
                return message.Match()
                    .With<DeleteOne>(d => DeleteSnapshot(d.Metadata.SequenceNr))
                    .With<DeleteMany>(d => DeleteSnapshots(d.Criteria))
                    .WasHandled;
            }
        }

        #endregion

        public SnapshotSpec()
            : base(Configuration("SnapshotSpec"))
        {
            var pref = ActorOf(() => new SaveSnapshotTestActor(Name, TestActor));
            pref.Tell("a");
            pref.Tell(TakeSnapshot.Instance);
            pref.Tell("b");
            pref.Tell(TakeSnapshot.Instance);
            pref.Tell("c");
            pref.Tell("d");
            pref.Tell(TakeSnapshot.Instance);
            pref.Tell("e");
            pref.Tell("f");
            ExpectMsgAllOf(1L, 2L, 4L);
        }

        [Fact]
        public void PersistentActor_should_recover_state_starting_from_the_most_recent_snapshot()
        {
            var pref = ActorOf(() => new LoadSnapshotTestActor(Name, new Recovery(), TestActor));
            var persistenceId = Name;

            var offer = ExpectMsg<SnapshotOffer>(o => o.Metadata.PersistenceId == persistenceId && o.Metadata.SequenceNr == 4);
            (offer.Snapshot as IEnumerable<string>).Reverse().ShouldOnlyContainInOrder("a-1", "b-2", "c-3", "d-4");
            (offer.Metadata.Timestamp > DateTime.MinValue).ShouldBeTrue();

            ExpectMsg("e-5");
            ExpectMsg("f-6");
            ExpectMsg<RecoveryCompleted>();
        }

        [Fact]
        public void PersistentActor_should_recover_completely_if_snapshot_is_not_handled()
        {
            var pref = ActorOf(() => new IgnoringSnapshotTestPersistentActor(Name, new Recovery(), TestActor));
            var persistenceId = Name;

            ExpectMsg("a-1");
            ExpectMsg("b-2");
            ExpectMsg("c-3");
            ExpectMsg("d-4");
            ExpectMsg("e-5");
            ExpectMsg("f-6");
            ExpectMsg<RecoveryCompleted>();
        }

        [Fact]
        public void PersistentActor_should_recover_state_starting_from_the_most_recent_snapshot_matching_an_upper_sequence_number_bound()
        {
            ActorOf(() => new LoadSnapshotTestActor(Name, new Recovery(SnapshotSelectionCriteria.Latest, 3), TestActor));
            var persistenceId = Name;

            var offer = ExpectMsg<SnapshotOffer>(o => o.Metadata.PersistenceId == persistenceId && o.Metadata.SequenceNr == 2);
            (offer.Snapshot as IEnumerable<string>).Reverse().ShouldOnlyContainInOrder("a-1", "b-2");
            (offer.Metadata.Timestamp > DateTime.MinValue).ShouldBeTrue();

            ExpectMsg("c-3");
            ExpectMsg<RecoveryCompleted>();
        }

        [Fact]
        public void PersistentActor_should_recover_state_starting_from_the_most_recent_snapshot_matching_an_upper_sequence_number_bound_without_further_replay()
        {
            var pref = ActorOf(() => new LoadSnapshotTestActor(Name, new Recovery(SnapshotSelectionCriteria.Latest, 4), TestActor));
            var persistenceId = Name;

            pref.Tell("done");

            var offer = ExpectMsg<SnapshotOffer>(o => o.Metadata.PersistenceId == persistenceId && o.Metadata.SequenceNr == 4);
            (offer.Snapshot as IEnumerable<string>).Reverse().ShouldOnlyContainInOrder("a-1", "b-2", "c-3", "d-4");
            (offer.Metadata.Timestamp > DateTime.MinValue).ShouldBeTrue();

            ExpectMsg<RecoveryCompleted>();
            ExpectMsg("done");
        }

        [Fact]
        public void PersistentActor_should_recover_state_starting_from_the_most_recent_snapshot_matching_criteria()
        {
            ActorOf(() => new LoadSnapshotTestActor(Name, new Recovery(new SnapshotSelectionCriteria(2)), TestActor));
            var persistenceId = Name;

            var offer = ExpectMsg<SnapshotOffer>(o => o.Metadata.PersistenceId == persistenceId && o.Metadata.SequenceNr == 2);
            (offer.Snapshot as IEnumerable<string>).Reverse().ShouldOnlyContainInOrder("a-1", "b-2");
            (offer.Metadata.Timestamp > DateTime.MinValue).ShouldBeTrue();

            ExpectMsg("c-3");
            ExpectMsg("d-4");
            ExpectMsg("e-5");
            ExpectMsg("f-6");
            ExpectMsg<RecoveryCompleted>();
        }

        [Fact]
        public void PersistentActor_should_recover_state_starting_from_the_most_recent_snapshot_matching_criteria_and_an_upper_sequence_number_bound()
        {
            ActorOf(() => new LoadSnapshotTestActor(Name, new Recovery(new SnapshotSelectionCriteria(2), 3), TestActor));
            var persistenceId = Name;

            var offer = ExpectMsg<SnapshotOffer>(o => o.Metadata.PersistenceId == persistenceId && o.Metadata.SequenceNr == 2);
            (offer.Snapshot as IEnumerable<string>).Reverse().ShouldOnlyContainInOrder("a-1", "b-2");
            (offer.Metadata.Timestamp > DateTime.MinValue).ShouldBeTrue();

            ExpectMsg("c-3");
            ExpectMsg<RecoveryCompleted>();
        }

        [Fact]
        public void PersistentActor_should_recover_state_from_scratch_if_snapshot_based_recovery_was_disabled()
        {
            ActorOf(() => new LoadSnapshotTestActor(Name, new Recovery(SnapshotSelectionCriteria.None, 3), TestActor));

            ExpectMsg("a-1");
            ExpectMsg("b-2");
            ExpectMsg("c-3");
            ExpectMsg<RecoveryCompleted>();
        }

        [Fact]
        public void PersistentActor_should_support_single_snapshot_deletions()
        {
            var delProbe = CreateTestProbe();
            var pref = ActorOf(() => new DeleteSnapshotTestActor(Name, new Recovery(SnapshotSelectionCriteria.Latest, 4), TestActor));
            var persistenceId = Name;

            Sys.EventStream.Subscribe(delProbe.Ref, typeof(DeleteSnapshot));

            pref.Tell("done");

            var offer = ExpectMsg<SnapshotOffer>(o => o.Metadata.PersistenceId == persistenceId && o.Metadata.SequenceNr == 4);
            (offer.Snapshot as IEnumerable<string>).Reverse().ShouldOnlyContainInOrder("a-1", "b-2", "c-3", "d-4");

            ExpectMsg<RecoveryCompleted>();
            ExpectMsg("done");

            pref.Tell(new DeleteOne(offer.Metadata));
            delProbe.ExpectMsg<DeleteSnapshot>();
            ExpectMsg<DeleteSnapshotSuccess>(m => m.Metadata.PersistenceId == persistenceId && m.Metadata.SequenceNr == 4);

            ActorOf(() => new DeleteSnapshotTestActor(Name, new Recovery(SnapshotSelectionCriteria.Latest, 4), TestActor));

            var offer2 = ExpectMsg<SnapshotOffer>(o => o.Metadata.PersistenceId == persistenceId && o.Metadata.SequenceNr == 2);
            (offer2.Snapshot as IEnumerable<string>).Reverse().ShouldOnlyContainInOrder("a-1", "b-2");

            ExpectMsg("c-3");
            ExpectMsg("d-4");
            ExpectMsg<RecoveryCompleted>();
        }

        [Fact]
        public void PersistentActor_should_support_bulk_snapshot_deletions()
        {
            var delProbe = CreateTestProbe();
            var pref = ActorOf(() => new DeleteSnapshotTestActor(Name, new Recovery(SnapshotSelectionCriteria.Latest, 4), TestActor));
            var persistenceId = Name;

            Sys.EventStream.Subscribe(delProbe.Ref, typeof(DeleteSnapshots));

            // recover persistentActor and the delete first three (= all) snapshots
            pref.Tell(new DeleteMany(new SnapshotSelectionCriteria(4, DateTime.MaxValue)));

            ExpectMsgPf("offer", o =>
            {
                var offer = o as SnapshotOffer;
                if (offer != null)
                {
                    var snapshot = offer.Snapshot as IEnumerable<string>;
                    snapshot.Reverse().ShouldOnlyContainInOrder("a-1", "b-2", "c-3", "d-4");

                    Assert.Equal(persistenceId, offer.Metadata.PersistenceId);
                    Assert.Equal(4, offer.Metadata.SequenceNr);

                    return offer;
                }
                else return null;
            });

            ExpectMsg<RecoveryCompleted>();
            delProbe.ExpectMsg<DeleteSnapshots>();
            ExpectMsg<DeleteSnapshotsSuccess>();

            // recover persistentActor from replayed messages (all snapshots deleted)
            ActorOf(() => new DeleteSnapshotTestActor(Name, new Recovery(SnapshotSelectionCriteria.None, 4), TestActor));
            ExpectMsg("a-1");
            ExpectMsg("b-2");
            ExpectMsg("c-3");
            ExpectMsg("d-4");
            ExpectMsg<RecoveryCompleted>();
        }
    }
}

