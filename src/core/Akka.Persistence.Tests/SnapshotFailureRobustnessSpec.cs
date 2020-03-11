//-----------------------------------------------------------------------
// <copyright file="SnapshotFailureRobustnessSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence.Snapshot;
using Akka.TestKit.TestEvent;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;

namespace Akka.Persistence.Tests
{
    public class SnapshotFailureRobustnessSpec : PersistenceSpec
    {
        public class Cmd
        {
            public string Payload { get; private set; }

            public Cmd(string payload)
            {
                Payload = payload;
            }
        }

        public class DeleteSnapshot
        {
            public int SequenceNr { get; private set; }

            public DeleteSnapshot(int sequenceNr)
            {
                SequenceNr = sequenceNr;
            }
        }

        public class DeleteSnapshots
        {
            public SnapshotSelectionCriteria Criteria { get; private set; }

            public DeleteSnapshots(SnapshotSelectionCriteria criteria)
            {
                Criteria = criteria;
            }
        }

        internal class SaveSnapshotTestActor : NamedPersistentActor
        {
            private readonly IActorRef _probe;

            public SaveSnapshotTestActor(string name, IActorRef probe) : base(name)
            {
                _probe = probe;
            }

            protected override bool ReceiveRecover(object message)
            {
                _probe.Tell(message);
                return true;
            }

            protected override bool ReceiveCommand(object message)
            {
                if (message is Cmd cmd)
                {
                    Persist(cmd.Payload, _ => SaveSnapshot(cmd.Payload));
                }
                else if (message is SaveSnapshotSuccess success)
                    _probe.Tell(success.Metadata.SequenceNr);
                else
                    _probe.Tell(message);
                return true;
            }
        }

        internal class DeleteSnapshotTestActor : NamedPersistentActor
        {
            private readonly IActorRef _probe;

            public DeleteSnapshotTestActor(string name, IActorRef probe) : base(name)
            {
                _probe = probe;
                SnapshotPluginId = "akka.persistence.snapshot-store.local-delete-fail";
            }

            protected override bool ReceiveRecover(object message)
            {
                _probe.Tell(message);
                return true;
            }

            protected override bool ReceiveCommand(object message)
            {
                if (message is Cmd)
                {
                    var cmd = (Cmd) message;
                    Persist(cmd.Payload, _ => SaveSnapshot(cmd.Payload));
                }
                else if (message is DeleteSnapshot)
                    DeleteSnapshot(((DeleteSnapshot)message).SequenceNr);
                else if (message is DeleteSnapshots)
                    DeleteSnapshots(((DeleteSnapshots)message).Criteria);
                else if (message is SaveSnapshotSuccess)
                    _probe.Tell(((SaveSnapshotSuccess)message).Metadata.SequenceNr);
                else
                    _probe.Tell(message);
                return true;
            }
        }

        internal class LoadSnapshotTestActor : NamedPersistentActor
        {
            private readonly IActorRef _probe;

            public LoadSnapshotTestActor(string name, IActorRef probe) : base(name)
            {
                _probe = probe;
            }

            protected override bool ReceiveRecover(object message)
            {
                if (message is string)
                    _probe.Tell(message + "-" + LastSequenceNr);
                else
                    _probe.Tell(message);
                return true;
            }

            protected override bool ReceiveCommand(object message)
            {
                if (message is Cmd)
                {
                    var cmd = (Cmd) message;
                    Persist(cmd.Payload, _ => SaveSnapshot(cmd.Payload));
                }
                else if (message is DeleteSnapshot)
                    DeleteSnapshot(((DeleteSnapshot)message).SequenceNr);
                else if (message is DeleteSnapshots)
                    DeleteSnapshots(((DeleteSnapshots)message).Criteria);
                else if (message is SaveSnapshotSuccess)
                    _probe.Tell(((SaveSnapshotSuccess)message).Metadata.SequenceNr);
                else
                    _probe.Tell(message);
                return true;
            }
        }

        internal class FailingLocalSnapshotStore : LocalSnapshotStore
        {
            protected override void Save(SnapshotMetadata metadata, object payload)
            {
                if (metadata.SequenceNr == 2 || payload.Equals("boom"))
                {
                    var bytes = Encoding.UTF8.GetBytes("b0rk");
                    var tempFile = WithOutputStream(metadata, stream => stream.Write(bytes, 0, bytes.Length));
					tempFile.MoveTo(GetSnapshotFileForWrite(metadata, "").FullName);
                }
                else base.Save(metadata, payload);
            }
        }

        internal class DeleteFailingLocalSnapshotStore : LocalSnapshotStore
        {
            protected override Task DeleteAsync(SnapshotMetadata metadata)
            {
                base.DeleteAsync(metadata); // we actually delete it properly, but act as if it failed
                var promise = new TaskCompletionSource<object>();
                promise.SetException(new InvalidOperationException("Failed to delete snapshot for some reason."));
                return promise.Task;
            }

            protected override Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria)
            {
                base.DeleteAsync(persistenceId, criteria); // we actually delete it properly, but act as if it failed
                var promise = new TaskCompletionSource<object>();
                promise.SetException(new InvalidOperationException("Failed to delete snapshot for some reason."));
                return promise.Task;
            }
        }

        public SnapshotFailureRobustnessSpec() : base(Configuration("SnapshotFailureRobustnessSpec", serialization: "off",
            extraConfig: @"
akka.persistence.snapshot-store.local.class = ""Akka.Persistence.Tests.SnapshotFailureRobustnessSpec+FailingLocalSnapshotStore, Akka.Persistence.Tests""
akka.persistence.snapshot-store.local-delete-fail.class = ""Akka.Persistence.Tests.SnapshotFailureRobustnessSpec+DeleteFailingLocalSnapshotStore, Akka.Persistence.Tests""
"))
        {
        }

        [Fact]
        public void PersistentActor_with_a_failing_snapshot_should_recover_state_starting_from_the_most_recent_complete_snapshot()
        {
            var spref = Sys.ActorOf(Props.Create(() => new SaveSnapshotTestActor(Name, TestActor)));
            var persistenceId = Name;

            ExpectMsg<RecoveryCompleted>();
            spref.Tell(new Cmd("blahonga"));
            ExpectMsg(1L);
            spref.Tell(new Cmd("kablama"));
            ExpectMsg(2L);
            // var filter = EventFilter.Error(start: "Error loading snapshot").Mute(); // TODO for some reason filtering doesn't work
            Sys.EventStream.Subscribe(TestActor, typeof (Error));
            try
            {
                var lpref = Sys.ActorOf(Props.Create(() => new LoadSnapshotTestActor(Name, TestActor)));
                ExpectMsg<Error>(m => m.Message.ToString().StartsWith("Error loading snapshot"));
                ExpectMsg<SnapshotOffer>(m => m.Metadata.PersistenceId.Equals(persistenceId) &&
                                              m.Metadata.SequenceNr == 1 && m.Metadata.Timestamp > SnapshotMetadata.TimestampNotSpecified &&
                                              m.Snapshot.Equals("blahonga"));
                ExpectMsg("kablama-2");
                ExpectMsg<RecoveryCompleted>();
                ExpectNoMsg(1.Seconds());
            }
            finally
            {
                Sys.EventStream.Unsubscribe(TestActor, typeof (Error));
                //filter.Unmute();
            }
        }

        [Fact]
        public void PersistentActor_with_a_failing_snapshot_should_fail_recovery_and_stop_actor_when_no_snapshot_could_be_loaded()
        {
            var spref = Sys.ActorOf(Props.Create(() => new SaveSnapshotTestActor(Name, TestActor)));

            ExpectMsg<RecoveryCompleted>();
            spref.Tell(new Cmd("Ok"));
            ExpectMsg(1L);
            spref.Tell(new Cmd("boom"));
            ExpectMsg(2L);
            spref.Tell(new Cmd("boom"));
            ExpectMsg(3L);
            spref.Tell(new Cmd("boom"));
            ExpectMsg(4L);
            // var filter = EventFilter.Error(start: "Error loading snapshot").Mute(); // TODO for some reason filtering doesn't work
            // var filter2 = EventFilter.Error(start: "Persistence failure").Mute(); // TODO for some reason filtering doesn't work
            Sys.EventStream.Subscribe(TestActor, typeof(Error));
            try
            {
                var lpref = Sys.ActorOf(Props.Create(() => new LoadSnapshotTestActor(Name, TestActor)));
                Enumerable.Range(1, 3).ForEach(_ =>
                {
                    ExpectMsg<Error>(m => m.Message.ToString().StartsWith("Error loading snapshot"));
                });
                ExpectMsg<Error>(m => m.Message.ToString().StartsWith("Persistence failure"));
                Watch(lpref);
                ExpectTerminated(lpref);
            }
            finally
            {
                Sys.EventStream.Unsubscribe(TestActor, typeof(Error));
                //filter.Unmute();
            }
        }

        [Fact]
        public void PersistentActor_with_a_failing_snapshot_should_receive_failure_message_when_deleting_a_single_snapshot_fails()
        {
            var pref = Sys.ActorOf(Props.Create(() => new DeleteSnapshotTestActor(Name, TestActor)));
            var persistenceId = Name;

            ExpectMsg<RecoveryCompleted>();
            pref.Tell(new Cmd("hello"));
            ExpectMsg(1L);
            pref.Tell(new DeleteSnapshot(1));
            ExpectMsg<DeleteSnapshotFailure>(m => m.Metadata.PersistenceId.Equals(persistenceId) &&
                                          m.Metadata.SequenceNr == 1 &&
                                          m.Cause.Message.Contains("Failed to delete"));
        }

        [Fact]
        public void PersistentActor_with_a_failing_snapshot_should_receive_failure_message_when_bulk_deleting_snapshots_fails()
        {
            var pref = Sys.ActorOf(Props.Create(() => new DeleteSnapshotTestActor(Name, TestActor)));

            ExpectMsg<RecoveryCompleted>();
            pref.Tell(new Cmd("hello"));
            ExpectMsg(1L);
            pref.Tell(new Cmd("hola"));
            ExpectMsg(2L);
            var criteria = new SnapshotSelectionCriteria(maxSequenceNr: 10);
            pref.Tell(new DeleteSnapshots(criteria));
            ExpectMsg<DeleteSnapshotsFailure>(m => m.Criteria.Equals(criteria) &&
                                          m.Cause.Message.Contains("Failed to delete"));
        }
    }
}
