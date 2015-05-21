using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Persistence.Cassandra.Tests
{
    /// <summary>
    /// Some integration tests for Cassandra Journal and Snapshot plugins.
    /// </summary>
    public class CassandraIntegrationSpec : Akka.TestKit.Xunit2.TestKit
    {
        private static readonly Config IntegrationConfig = ConfigurationFactory.ParseString(@"
            akka.persistence.journal.plugin = ""cassandra-journal""
            akka.persistence.snapshot-store.plugin = ""cassandra-snapshot-store""
            akka.persistence.publish-plugin-commands = on
            akka.test.single-expect-default = 10s
            cassandra-journal.partition-size = 5
            cassandra-journal.max-result-size = 3
        ");

        // Static so that each test run gets a different Id number
        private static readonly AtomicCounter ActorIdCounter = new AtomicCounter();

        private readonly string _actorId;

        public CassandraIntegrationSpec()
            : base(IntegrationConfig, "CassandraIntegration")
        {
            TestSetupHelpers.ResetJournalData(Sys);
            TestSetupHelpers.ResetSnapshotStoreData(Sys);

            // Increment actor Id with each test that's run
            int id = ActorIdCounter.IncrementAndGet();
            _actorId = string.Format("p{0}", id);
        }

        [Fact]
        public void Cassandra_journal_should_write_and_replay_messages()
        {
            // Start a persistence actor and write some messages to it
            var actor1 = Sys.ActorOf(Props.Create<PersistentActorA>(_actorId));
            WriteAndVerifyMessages(actor1, 1L, 16L);

            // Now start a new instance (same persistence Id) and it should recover with those same messages
            var actor2 = Sys.ActorOf(Props.Create<PersistentActorA>(_actorId));
            for (long i = 1L; i <= 16L; i++)
            {
                string msg = string.Format("a-{0}", i);
                ExpectHandled(msg, i, true);
            }

            // We should then be able to send that actor another message and have it be persisted
            actor2.Tell("b");
            ExpectHandled("b", 17L, false);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Cassandra_journal_should_not_replay_deleted_messages(bool permanentDelete)
        {
            // Listen for delete messages on the event stream
            TestProbe deleteProbe = CreateTestProbe();
            Sys.EventStream.Subscribe(deleteProbe.Ref, typeof (DeleteMessagesTo));

            var actor1 = Sys.ActorOf(Props.Create<PersistentActorA>(_actorId));
            WriteAndVerifyMessages(actor1, 1L, 16L);

            // Tell the actor to delete some messages and make sure it's finished
            actor1.Tell(new DeleteToCommand(3L, permanentDelete));
            deleteProbe.ExpectMsg<DeleteMessagesTo>();

            // Start a second copy of the actor and verify it starts replaying from the correct spot
            Sys.ActorOf(Props.Create<PersistentActorA>(_actorId));
            for (long i = 4L; i <= 16L; i++)
            {
                string msg = string.Format("a-{0}", i);
                ExpectHandled(msg, i, true);
            }

            // Delete some more messages and wait for confirmation
            actor1.Tell(new DeleteToCommand(7L, permanentDelete));
            deleteProbe.ExpectMsg<DeleteMessagesTo>();

            // Start another copy and verify playback again
            Sys.ActorOf(Props.Create<PersistentActorA>(_actorId));
            for (long i = 8L; i <= 16L; i++)
            {
                string msg = string.Format("a-{0}", i);
                ExpectHandled(msg, i, true);
            }
        }

        [Fact]
        public void Cassandra_journal_should_replay_message_incrementally()
        {
            // Write some messages to a Persistent Actor
            var actor = Sys.ActorOf(Props.Create<PersistentActorA>(_actorId));
            WriteAndVerifyMessages(actor, 1L, 6L);

            TestProbe probe = CreateTestProbe();
            
            // Create a persistent view from the actor that does not do auto-updating
            var view = Sys.ActorOf(Props.Create<ViewA>(_actorId + "-view", _actorId, probe.Ref));
            probe.ExpectNoMsg(200);

            // Tell the view to update and verify we get the messages we wrote earlier replayed
            view.Tell(new Update(true, 3L));
            probe.ExpectMsg("a-1");
            probe.ExpectMsg("a-2");
            probe.ExpectMsg("a-3");
            probe.ExpectNoMsg(200);

            // Update the view again and verify we get the rest of the messages
            view.Tell(new Update(true, 3L));
            probe.ExpectMsg("a-4");
            probe.ExpectMsg("a-5");
            probe.ExpectMsg("a-6");
            probe.ExpectNoMsg(200);
        }

        [Fact]
        public void Persistent_actor_should_recover_from_a_snapshot_with_follow_up_messages()
        {
            // Write a message, snapshot, then another follow-up message
            var actor1 = Sys.ActorOf(Props.Create<PersistentActorC>(_actorId, TestActor));
            actor1.Tell("a");
            ExpectHandled("a", 1, false);
            actor1.Tell("snap");
            ExpectMsg("snapped-a-1");
            actor1.Tell("b");
            ExpectHandled("b", 2, false);

            // Start the actor again and verify we get a snapshot, followed by the message that wasn't in the snapshot
            var actor2 = Sys.ActorOf(Props.Create<PersistentActorC>(_actorId, TestActor));
            ExpectMsg("offered-a-1");
            ExpectHandled("b", 2, true);
        }

        [Fact]
        public void Persistent_actor_should_recover_from_a_snapshot_with_follow_up_messages_and_an_upper_bound()
        {
            // Create an actor and trigger manual recovery so it will accept new messages
            var actor1 = Sys.ActorOf(Props.Create<PersistentActorCWithManualRecovery>(_actorId, TestActor));
            actor1.Tell(new Recover(SnapshotSelectionCriteria.None));

            // Write a message, snapshot, then write some follow-up messages
            actor1.Tell("a");
            ExpectHandled("a", 1, false);
            actor1.Tell("snap");
            ExpectMsg("snapped-a-1");
            WriteSameMessageAndVerify(actor1, "a", 2L, 7L);

            // Create another copy of that actor and manually recover to an upper bound (i.e. past state) and verify
            // we get the expected messages after the snapshot
            var actor2 = Sys.ActorOf(Props.Create<PersistentActorCWithManualRecovery>(_actorId, TestActor));
            actor2.Tell(new Recover(SnapshotSelectionCriteria.Latest, toSequenceNr: 3L));
            ExpectMsg("offered-a-1");
            ExpectHandled("a", 2, true);
            ExpectHandled("a", 3, true);

            // Should continue working after recovery to previous state, but highest sequence number should take into 
            // account other messages that were written but not replayed
            actor2.Tell("d");
            ExpectHandled("d", 8L, false);
        }

        [Fact]
        public void Persistent_actor_should_recover_from_a_snapshot_without_follow_up_messages_inside_a_partition()
        {
            // Write a message, then snapshot, no follow-up messages after snapshot
            var actor1 = Sys.ActorOf(Props.Create<PersistentActorC>(_actorId, TestActor));
            actor1.Tell("a");
            ExpectHandled("a", 1L, false);
            actor1.Tell("snap");
            ExpectMsg("snapped-a-1");

            // Start another copy and verify we recover with the snapshot
            var actor2 = Sys.ActorOf(Props.Create<PersistentActorC>(_actorId, TestActor));
            ExpectMsg("offered-a-1");

            // Write another message to verify
            actor2.Tell("b");
            ExpectHandled("b", 2L, false);
        }

        [Fact]
        public void Persistent_actor_should_recover_from_a_snapshot_without_follow_up_messages_at_a_partition_boundary_where_next_partition_is_invalid()
        {
            // Partition size for tests is 5 (see Config above), so write messages up to partition boundary (but don't write any
            // messages to the next partition)
            var actor1 = Sys.ActorOf(Props.Create<PersistentActorC>(_actorId, TestActor));
            WriteSameMessageAndVerify(actor1, "a", 1L, 5L);

            // Snapshot and verify without any follow-up messages
            actor1.Tell("snap");
            ExpectMsg("snapped-a-5");

            // Create a second copy of that actor and verify it recovers from the snapshot and continues working
            var actor2 = Sys.ActorOf(Props.Create<PersistentActorC>(_actorId, TestActor));
            ExpectMsg("offered-a-5");
            actor2.Tell("b");
            ExpectHandled("b", 6L, false);
        }
        
        /// <summary>
        /// Write messages "a-xxx" where xxx is an index number from start to end and verify that each message returns
        /// a Handled response.
        /// </summary>
        private void WriteAndVerifyMessages(IActorRef persistentActor, long start, long end)
        {
            for (long i = start; i <= end; i++)
            {
                string msg = string.Format("a-{0}", i);
                persistentActor.Tell(msg, TestActor);
                ExpectHandled(msg, i, false);
            }
        }

        /// <summary>
        /// Writes the same message multiple times and verify that we get a Handled response.
        /// </summary>
        private void WriteSameMessageAndVerify(IActorRef persistentActor, string message, long start, long end)
        {
            for (long i = start; i <= end; i++)
            {
                persistentActor.Tell(message, TestActor);
                ExpectHandled(message, i, false);
            }
        }

        private void ExpectHandled(string message, long sequenceNumber, bool isRecovering)
        {
            object msg = ReceiveOne();
            var handledMsg = Assert.IsType<HandledMessage>(msg);
            Assert.Equal(message, handledMsg.Message);
            Assert.Equal(sequenceNumber, handledMsg.SequenceNumber);
            Assert.Equal(isRecovering, handledMsg.IsRecovering);
        }

        #region Test Messages and Actors

        [Serializable]
        public class DeleteToCommand
        {
            public long SequenceNumber { get; private set; }
            public bool Permanent { get; private set; }

            public DeleteToCommand(long sequenceNumber, bool permanent)
            {
                SequenceNumber = sequenceNumber;
                Permanent = permanent;
            }
        }

        [Serializable]
        public class HandledMessage
        {
            public string Message { get; private set; }
            public long SequenceNumber { get; private set; }
            public bool IsRecovering { get; private set; }

            public HandledMessage(string message, long sequenceNumber, bool isRecovering)
            {
                Message = message;
                SequenceNumber = sequenceNumber;
                IsRecovering = isRecovering;
            }
        }

        public class PersistentActorA : PersistentActor
        {
            private readonly string _persistenceId;

            public PersistentActorA(string persistenceId)
            {
                _persistenceId = persistenceId;
            }

            public override string PersistenceId
            {
                get { return _persistenceId; }
            }

            protected override bool ReceiveRecover(object message)
            {
                if (message is string)
                {
                    var payload = (string) message;
                    Handle(payload);
                    return true;
                }

                return false;
            }

            protected override bool ReceiveCommand(object message)
            {
                if (message is DeleteToCommand)
                {
                    var delete = (DeleteToCommand) message;
                    DeleteMessages(delete.SequenceNumber, delete.Permanent);
                    return true;
                }

                if (message is string)
                {
                    var payload = (string) message;
                    Persist(payload, Handle);
                    return true;
                }

                return false;
            }

            private void Handle(string payload)
            {
                Context.Sender.Tell(new HandledMessage(payload, LastSequenceNr, IsRecovering), Self);
            }
        }

        public class PersistentActorC : PersistentActor
        {
            private readonly string _persistenceId;
            private readonly IActorRef _probe;

            private string _last;

            public override string PersistenceId
            {
                get { return _persistenceId; }
            }

            public PersistentActorC(string persistenceId, IActorRef probe)
            {
                _persistenceId = persistenceId;
                _probe = probe;
            }

            protected override bool ReceiveRecover(object message)
            {
                if (message is SnapshotOffer)
                {
                    var offer = (SnapshotOffer) message;
                    _last = (string) offer.Snapshot;
                    _probe.Tell(string.Format("offered-{0}", _last));
                    return true;
                }

                if (message is string)
                {
                    var payload = (string) message;
                    Handle(payload);
                    return true;
                }

                return false;
            }

            protected override bool ReceiveCommand(object message)
            {
                if (message is string)
                {
                    var msg = (string) message;
                    if (msg == "snap")
                        SaveSnapshot(_last);
                    else
                        Persist(msg, Handle);

                    return true;
                }

                if (message is SaveSnapshotSuccess)
                {
                    _probe.Tell(string.Format("snapped-{0}", _last), Context.Sender);
                    return true;
                }

                if (message is DeleteToCommand)
                {
                    var delete = (DeleteToCommand) message;
                    DeleteMessages(delete.SequenceNumber, delete.Permanent);
                    return true;
                }
                
                return false;
            }

            private void Handle(string payload)
            {
                _last = string.Format("{0}-{1}", payload, LastSequenceNr);
                _probe.Tell(new HandledMessage(payload, LastSequenceNr, IsRecovering));
            }
        }

        public class PersistentActorCWithManualRecovery : PersistentActorC
        {
            public PersistentActorCWithManualRecovery(string persistenceId, IActorRef probe)
                : base(persistenceId, probe)
            {
            }

            protected override void PreRestart(Exception reason, object message)
            {
                // Don't do automatic recovery
            }
        }

        public class ViewA : PersistentView
        {
            private readonly string _viewId;
            private readonly string _persistenceId;
            private readonly IActorRef _probe;

            public override string ViewId
            {
                get { return _viewId; }
            }

            public override string PersistenceId
            {
                get { return _persistenceId; }
            }

            public override bool IsAutoUpdate
            {
                get { return false; }
            }

            public override long AutoUpdateReplayMax
            {
                get { return 0L; }
            }
            
            public ViewA(string viewId, string persistenceId, IActorRef probe)
            {
                _viewId = viewId;
                _persistenceId = persistenceId;
                _probe = probe;
            }

            protected override bool Receive(object message)
            {
                // Just forward messages to the test probe
                _probe.Tell(message, Context.Sender);
                return true;
            }
        }

        #endregion
    }
}
