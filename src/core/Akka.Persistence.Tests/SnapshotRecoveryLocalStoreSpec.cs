//-----------------------------------------------------------------------
// <copyright file="SnapshotRecoveryLocalStoreSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Xunit;

namespace Akka.Persistence.Tests
{
    public class SnapshotRecoveryLocalStoreSpec : PersistenceSpec
    {
        private const string PersistenceId = "europe";
        private const string ExtendedName = PersistenceId + "italy";

        public sealed class TakeSnapshot
        {
            private TakeSnapshot() {}
            public static readonly TakeSnapshot Instance = new TakeSnapshot();
        }

        internal class SaveSnapshotTestPersistentActor : NamedPersistentActor
        {
            private readonly IActorRef _probe;
            private readonly string _state;

            public SaveSnapshotTestPersistentActor(string name, IActorRef probe) : base(name)
            {
                _probe = probe;
                _state = "State for actor " + name;
            }


            protected override bool ReceiveRecover(object message)
            {
                return false;
            }

            protected override bool ReceiveCommand(object message)
            {
                if (message is TakeSnapshot)
                    SaveSnapshot(_state);
                else if (message is SaveSnapshotSuccess)
                    _probe.Tell(((SaveSnapshotSuccess)message).Metadata.SequenceNr);
                else if (message is GetState)
                    _probe.Tell(_state);
                else return false;
                return true;
            }
        }

        internal class LoadSnapshotTestPersistentActor : NamedPersistentActor
        {
            private readonly IActorRef _probe;

            public LoadSnapshotTestPersistentActor(string name, IActorRef probe) : base(name)
            {
                _probe = probe;
            }

            public override Recovery Recovery => new Recovery(SnapshotSelectionCriteria.Latest, 0);

            protected override bool ReceiveRecover(object message)
            {
                _probe.Tell(message);
                return true;
            }

            protected override bool ReceiveCommand(object message)
            {
                return false;
            }
        }

        public SnapshotRecoveryLocalStoreSpec() : base(Configuration("SnapshotRecoveryLocalStoreSpec")) { }

        [Fact]
        public void PersistentActor_which_is_persisted_at_the_same_time_as_another_actor_whose_PersistenceId_is_an_extension_of_the_first_should_recover_state_only_from_its_own_correct_snapshot_file()
        {
            var pref1 = Sys.ActorOf(Props.Create(() => new SaveSnapshotTestPersistentActor(PersistenceId, TestActor)));
            var pref2 = Sys.ActorOf(Props.Create(() => new SaveSnapshotTestPersistentActor(ExtendedName, TestActor)));
            pref1.Tell(TakeSnapshot.Instance);
            pref2.Tell(TakeSnapshot.Instance);
            ExpectMsg(0L);
            ExpectMsg(0L);

            var recoveringActor = Sys.ActorOf(Props.Create(() => new LoadSnapshotTestPersistentActor(PersistenceId, TestActor)));
            ExpectMsg<SnapshotOffer>(m => m.Metadata.PersistenceId.Equals(PersistenceId));
            ExpectMsg<RecoveryCompleted>();
        }
    }
}
