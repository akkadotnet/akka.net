//-----------------------------------------------------------------------
// <copyright file="JournalSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.TCK.Serialization;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.TCK.Journal
{
    public abstract class JournalSpec : PluginSpec
    {
        protected static readonly Config Config =
            ConfigurationFactory.ParseString(@"akka.persistence.publish-plugin-commands = on
            akka.actor{
                serializers{
                    persistence-tck-test=""Akka.Persistence.TCK.Serialization.TestSerializer,Akka.Persistence.TCK""
                }
                serialization-bindings {
                    ""Akka.Persistence.TCK.Serialization.TestPayload,Akka.Persistence.TCK"" = persistence-tck-test
                }
            }");

        private static readonly string _specConfigTemplate = @"
            akka.persistence {{
                publish-plugin-commands = on
                journal {{
                    plugin = ""akka.persistence.journal.testspec""
                    testspec {{
                        class = ""{0}""
                        plugin-dispatcher = ""akka.actor.default-dispatcher""
                    }}
                }}
            }}
        ";

        private TestProbe _senderProbe;
        private TestProbe _receiverProbe;

        protected JournalSpec(Config config = null, string actorSystemName = null, ITestOutputHelper output = null)
            : base(FromConfig(config).WithFallback(Config), actorSystemName ?? "JournalSpec", output)
        {
        }

        protected override bool SupportsSerialization => true;

        /// <summary>
        /// Initializes a journal with set o predefined messages.
        /// </summary>
        protected IEnumerable<AtomicWrite> Initialize()
        {
            _senderProbe = CreateTestProbe();
            _receiverProbe = CreateTestProbe();
            PreparePersistenceId(Pid);
            return WriteMessages(1, 5, Pid, _senderProbe.Ref, WriterGuid);
        }

        protected JournalSpec(Type journalType, string actorSystemName = null, ITestOutputHelper output = null)
            : base(ConfigFromTemplate(journalType), actorSystemName, output)
        {
        }

        /// <summary>
        /// Overridable hook that is called before populating the journal for the next test case.
        /// <paramref name="pid"/> is the persistenceId that will be used in the test.
        /// This method may be needed to clean pre-existing events from the log.
        /// </summary>
        /// <param name="pid"></param>
        protected virtual void PreparePersistenceId(string pid)
        {
        }

        /// <summary>
        /// Implementation may override and return false if it does not support
        /// atomic writes of several events, as emitted by
        /// <see cref="Eventsourced.PersistAll{TEvent}(IEnumerable{TEvent},Action{TEvent})"/>.
        /// </summary>
        protected virtual bool SupportsAtomicPersistAllOfSeveralEvents { get { return true; } }

        /// <summary>
        /// When true enables tests which check if the Journal properly rejects
        /// writes of objects which are not serializable.
        /// </summary>
        protected virtual bool SupportsRejectingNonSerializableObjects { get { return true; } }

        protected IActorRef Journal { get { return Extension.JournalFor(null); } }

        private static Config ConfigFromTemplate(Type journalType)
        {
            var config = string.Format(_specConfigTemplate, journalType.AssemblyQualifiedName);
            return ConfigurationFactory.ParseString(config);
        }

        protected bool IsReplayedMessage(ReplayedMessage message, long seqNr, bool isDeleted = false)
        {
            var p = message.Persistent;
            return p.IsDeleted == isDeleted
                   && p.Payload.ToString() == "a-" + seqNr
                   && p.PersistenceId == Pid
                   && p.SequenceNr == seqNr;
        }

        private AtomicWrite[] WriteMessages(int from, int to, string pid, IActorRef sender, string writerGuid)
        {
            Func<long, Persistent> persistent = i => new Persistent("a-" + i, i, pid, string.Empty, false, sender, writerGuid);
            var messages = (SupportsAtomicPersistAllOfSeveralEvents
                ? Enumerable.Range(from, to - 1)
                    .Select(
                        i =>
                            i == to - 1
                                ? new AtomicWrite(
                                    new[] {persistent(i), persistent(i + 1)}.ToImmutableList<IPersistentRepresentation>())
                                : new AtomicWrite(persistent(i)))
                : Enumerable.Range(from, to).Select(i => new AtomicWrite(persistent(i))))
                .ToArray();
            var probe = CreateTestProbe();

            Journal.Tell(new WriteMessages(messages, probe.Ref, ActorInstanceId));

            probe.ExpectMsg<WriteMessagesSuccessful>();
            for (int i = from; i <= to; i++)
            {
                var n = i;
                probe.ExpectMsg<WriteMessageSuccess>(m =>
                        m.Persistent.Payload.ToString() == ("a-" + n) && m.Persistent.SequenceNr == (long)n &&
                        m.Persistent.PersistenceId == Pid);
            }

            return messages;
        }

        [Fact]
        public void Journal_should_replay_all_messages()
        {
            Journal.Tell(new ReplayMessages(1, long.MaxValue, long.MaxValue, Pid, _receiverProbe.Ref));
            for (int i = 1; i <= 5; i++) _receiverProbe.ExpectMsg<ReplayedMessage>(m => IsReplayedMessage(m, i));
            _receiverProbe.ExpectMsg<RecoverySuccess>();
        }

        [Fact]
        public void Journal_should_replay_messages_using_a_lower_sequence_number_bound()
        {
            Journal.Tell(new ReplayMessages(3, long.MaxValue, long.MaxValue, Pid, _receiverProbe.Ref));
            for (int i = 3; i <= 5; i++) _receiverProbe.ExpectMsg<ReplayedMessage>(m => IsReplayedMessage(m, i));
            _receiverProbe.ExpectMsg<RecoverySuccess>();
        }

        [Fact]
        public void Journal_should_replay_messages_using_an_upper_sequence_number_bound()
        {
            Journal.Tell(new ReplayMessages(1, 3, long.MaxValue, Pid, _receiverProbe.Ref));
            for (int i = 1; i <= 3; i++) _receiverProbe.ExpectMsg<ReplayedMessage>(m => IsReplayedMessage(m, i));
            _receiverProbe.ExpectMsg<RecoverySuccess>();
        }

        [Fact]
        public void Journal_should_replay_messages_using_a_count_limit()
        {
            Journal.Tell(new ReplayMessages(1, long.MaxValue, 3, Pid, _receiverProbe.Ref));
            for (int i = 1; i <= 3; i++) _receiverProbe.ExpectMsg<ReplayedMessage>(m => IsReplayedMessage(m, i));
            _receiverProbe.ExpectMsg<RecoverySuccess>();
        }

        [Fact]
        public void Journal_should_replay_messages_using_lower_and_upper_sequence_number_bound()
        {
            Journal.Tell(new ReplayMessages(2, 3, long.MaxValue, Pid, _receiverProbe.Ref));
            for (int i = 2; i <= 3; i++) _receiverProbe.ExpectMsg<ReplayedMessage>(m => IsReplayedMessage(m, i));
            _receiverProbe.ExpectMsg<RecoverySuccess>();
        }

        [Fact]
        public void Journal_should_replay_messages_using_lower_and_upper_sequence_number_bound_and_count_limit()
        {
            Journal.Tell(new ReplayMessages(2, 5, 2, Pid, _receiverProbe.Ref));
            for (int i = 2; i <= 3; i++) _receiverProbe.ExpectMsg<ReplayedMessage>(m => IsReplayedMessage(m, i));
            _receiverProbe.ExpectMsg<RecoverySuccess>();
        }

        [Fact]
        public void Journal_should_replay_a_single_if_lower_sequence_number_bound_equals_upper_sequence_number_bound()
        {
            Journal.Tell(new ReplayMessages(2, 2, long.MaxValue, Pid, _receiverProbe.Ref));
            _receiverProbe.ExpectMsg<ReplayedMessage>(m => IsReplayedMessage(m, 2));
            _receiverProbe.ExpectMsg<RecoverySuccess>();
        }

        [Fact]
        public void Journal_should_replay_a_single_message_if_count_limit_is_equal_one()
        {
            Journal.Tell(new ReplayMessages(2, 4, 1, Pid, _receiverProbe.Ref));
            _receiverProbe.ExpectMsg<ReplayedMessage>(m => IsReplayedMessage(m, 2));
            _receiverProbe.ExpectMsg<RecoverySuccess>();
        }

        [Fact]
        public void Journal_should_not_replay_messages_if_count_limit_equals_zero()
        {
            Journal.Tell(new ReplayMessages(2, 4, 0, Pid, _receiverProbe.Ref));
            _receiverProbe.ExpectMsg<RecoverySuccess>();
        }

        [Fact]
        public void Journal_should_not_replay_messages_if_lower_sequence_number_bound_is_greater_than_upper_sequence_number_bound()
        {
            Journal.Tell(new ReplayMessages(3, 2, long.MaxValue, Pid, _receiverProbe.Ref));
            _receiverProbe.ExpectMsg<RecoverySuccess>();
        }

        [Fact]
        public void Journal_should_not_replay_messages_if_the_persistent_actor_has_not_yet_written_messages()
        {
            Journal.Tell(new ReplayMessages(0, long.MaxValue, long.MaxValue, "non-existing-pid", _receiverProbe.Ref));
            _receiverProbe.ExpectMsg<RecoverySuccess>(m => m.HighestSequenceNr == 0L);
        }

        [Fact]
        public void Journal_should_not_replay_permanently_deleted_messages_on_range_deletion()
        {
            var receiverProbe2 = CreateTestProbe();
            var command = new DeleteMessagesTo(Pid, 3, receiverProbe2.Ref);
            var subscriber = CreateTestProbe();

            Subscribe<DeleteMessagesTo>(subscriber.Ref);
            Journal.Tell(command);
            subscriber.ExpectMsg<DeleteMessagesTo>(cmd => cmd.PersistenceId == Pid && cmd.ToSequenceNr == 3);
            receiverProbe2.ExpectMsg<DeleteMessagesSuccess>(m => m.ToSequenceNr == command.ToSequenceNr);

            Journal.Tell(new ReplayMessages(1, long.MaxValue, long.MaxValue, Pid, _receiverProbe.Ref));
            _receiverProbe.ExpectMsg<ReplayedMessage>(m => IsReplayedMessage(m, 4));
            _receiverProbe.ExpectMsg<ReplayedMessage>(m => IsReplayedMessage(m, 5));

            receiverProbe2.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
        }

        [Fact]
        public void Journal_should_not_reset_HighestSequenceNr_after_message_deletion()
        {
            Journal.Tell(new ReplayMessages(0, long.MaxValue, long.MaxValue, Pid, _receiverProbe.Ref));
            for (int i = 1; i <= 5; i++) _receiverProbe.ExpectMsg<ReplayedMessage>(m => IsReplayedMessage(m, i));
            _receiverProbe.ExpectMsg<RecoverySuccess>(m => m.HighestSequenceNr == 5L);

            Journal.Tell(new DeleteMessagesTo(Pid, 3L, _receiverProbe.Ref));
            _receiverProbe.ExpectMsg<DeleteMessagesSuccess>(m => m.ToSequenceNr == 3L);

            Journal.Tell(new ReplayMessages(0, long.MaxValue, long.MaxValue, Pid, _receiverProbe.Ref));
            for (int i = 4; i <= 5; i++) _receiverProbe.ExpectMsg<ReplayedMessage>(m => IsReplayedMessage(m, i));
            _receiverProbe.ExpectMsg<RecoverySuccess>(m => m.HighestSequenceNr == 5L);
        }

        [Fact]
        public void Journal_should_not_reset_HighestSequenceNr_after_journal_cleanup()
        {
            Journal.Tell(new ReplayMessages(0, long.MaxValue, long.MaxValue, Pid, _receiverProbe.Ref));
            for (int i = 1; i <= 5; i++) _receiverProbe.ExpectMsg<ReplayedMessage>(m => IsReplayedMessage(m, i));
            _receiverProbe.ExpectMsg<RecoverySuccess>(m => m.HighestSequenceNr == 5L);

            Journal.Tell(new DeleteMessagesTo(Pid, long.MaxValue, _receiverProbe.Ref));
            _receiverProbe.ExpectMsg<DeleteMessagesSuccess>(m => m.ToSequenceNr == long.MaxValue);

            Journal.Tell(new ReplayMessages(0, long.MaxValue, long.MaxValue, Pid, _receiverProbe.Ref));
            _receiverProbe.ExpectMsg<RecoverySuccess>(m => m.HighestSequenceNr == 5L);
        }

        [Fact]
        public void Journal_should_serialize_events()
        {
            if (!SupportsSerialization) return;

            var probe = CreateTestProbe();
            var @event = new TestPayload(probe.Ref);

            var aw = new AtomicWrite(
                new Persistent(@event, 6L, Pid, sender: ActorRefs.NoSender, writerGuid: WriterGuid));

            Journal.Tell(new WriteMessages(new []{ aw }, probe.Ref, ActorInstanceId));

            probe.ExpectMsg<WriteMessagesSuccessful>();
            var pid = Pid;
            var writerGuid = WriterGuid;
            probe.ExpectMsg<WriteMessageSuccess>(o =>
            {
                Assertions.AssertEqual(writerGuid, o.Persistent.WriterGuid);
                Assertions.AssertEqual(pid, o.Persistent.PersistenceId);
                Assertions.AssertEqual(6L, o.Persistent.SequenceNr);
                Assertions.AssertTrue(o.Persistent.Sender == ActorRefs.NoSender || o.Persistent.Sender.Equals(Sys.DeadLetters), $"Expected WriteMessagesSuccess.Persistent.Sender to be null or {Sys.DeadLetters}, but found {o.Persistent.Sender}");
                Assertions.AssertEqual(@event, o.Persistent.Payload);
            });

            Journal.Tell(new ReplayMessages(6L, long.MaxValue, long.MaxValue, Pid, _receiverProbe.Ref));

            _receiverProbe.ExpectMsg<ReplayedMessage>(o =>
            {
                Assertions.AssertEqual(writerGuid, o.Persistent.WriterGuid);
                Assertions.AssertEqual(pid, o.Persistent.PersistenceId);
                Assertions.AssertEqual(6L, o.Persistent.SequenceNr);
                Assertions.AssertTrue(o.Persistent.Sender == ActorRefs.NoSender || o.Persistent.Sender.Equals(Sys.DeadLetters), $"Expected WriteMessagesSuccess.Persistent.Sender to be null or {Sys.DeadLetters}, but found {o.Persistent.Sender}");
                Assertions.AssertEqual(@event, o.Persistent.Payload);
            });

            Assertions.AssertEqual(_receiverProbe.ExpectMsg<RecoverySuccess>().HighestSequenceNr, 6L);
        }

#if !CORECLR
        /// <summary>
        /// JSON serializer should fail on this
        /// </summary>
        private class NotSerializableEvent : ISerializable
        {
            public NotSerializableEvent(bool foo) { }

            public void GetObjectData(SerializationInfo info, StreamingContext context)
            {
                throw new NotImplementedException();
            }
        }

        [Fact]
        public void Journal_optionally_may_reject_non_serializable_events()
        {
            if (!SupportsRejectingNonSerializableObjects) return;

            var msgs = Enumerable.Range(6, 3).Select(i =>
            {
                var evt = i == 7 ? (object) new NotSerializableEvent(false) : "b-" + i;
                return new AtomicWrite(new Persistent(evt, i, Pid, sender: ActorRefs.NoSender, writerGuid: WriterGuid));
            }).ToArray();

            var probe = CreateTestProbe();
            Journal.Tell(new WriteMessages(msgs, probe.Ref, ActorInstanceId));
            probe.ExpectMsg<WriteMessagesSuccessful>();

            var pid = Pid;
            var writerGuid = WriterGuid;
            probe.ExpectMsg<WriteMessageSuccess>(m => m.Persistent.SequenceNr == 6L &&
                                                      m.Persistent.PersistenceId.Equals(pid) &&
                                                      m.Persistent.Sender == null &&
                                                      m.Persistent.WriterGuid.Equals(writerGuid) &&
                                                      m.Persistent.Payload.Equals("b-6"));
            probe.ExpectMsg<WriteMessageRejected>(m => m.Persistent.SequenceNr == 7L &&
                                                       m.Persistent.PersistenceId.Equals(pid) &&
                                                       m.Persistent.Sender == null &&
                                                       m.Persistent.WriterGuid.Equals(writerGuid) &&
                                                       m.Persistent.Payload is NotSerializableEvent);
            probe.ExpectMsg<WriteMessageSuccess>(m => m.Persistent.SequenceNr == 8L &&
                                                      m.Persistent.PersistenceId.Equals(pid) &&
                                                      m.Persistent.Sender == null &&
                                                      m.Persistent.WriterGuid.Equals(writerGuid) &&
                                                      m.Persistent.Payload.Equals("b-8"));
        }
#endif
    }
}
