//-----------------------------------------------------------------------
// <copyright file="JournalSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

﻿using System;
﻿using System.Collections.Generic;
﻿using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;

namespace Akka.Persistence.TestKit.Journal
{
    public abstract class JournalSpec : PluginSpec
    {
        protected static readonly Config Config =
            ConfigurationFactory.ParseString("akka.persistence.publish-plugin-commands = on");

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

        protected JournalSpec(Config config = null, string actorSystemName = null, string testActorName = null)
            : base(FromConfig(config).WithFallback(Config), actorSystemName ?? "JournalSpec", testActorName)
        {
        }

        /// <summary>
        /// Initializes a journal with set o predefined messages.
        /// </summary>
        protected IEnumerable<Persistent> Initialize()
        {
            _senderProbe = CreateTestProbe();
            _receiverProbe = CreateTestProbe();
            return WriteMessages(1, 5, Pid, _senderProbe.Ref);
        }

        protected JournalSpec(Type journalType, string actorSystemName = null)
            : base(ConfigFromTemplate(journalType), actorSystemName)
        {
        }

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

        private Persistent[] WriteMessages(int from, int to, string pid, IActorRef sender)
        {
            var messages = Enumerable.Range(from, to).Select(i => new Persistent("a-" + i, i, pid, false, sender)).ToArray();
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
            _receiverProbe.ExpectMsg<ReplayMessagesSuccess>();
        }

        [Fact]
        public void Journal_should_replay_messages_using_a_lower_sequence_number_bound()
        {
            Journal.Tell(new ReplayMessages(3, long.MaxValue, long.MaxValue, Pid, _receiverProbe.Ref));
            for (int i = 3; i <= 5; i++) _receiverProbe.ExpectMsg<ReplayedMessage>(m => IsReplayedMessage(m, i));
            _receiverProbe.ExpectMsg<ReplayMessagesSuccess>();
        }

        [Fact]
        public void Journal_should_replay_messages_using_an_upper_sequence_number_bound()
        {
            Journal.Tell(new ReplayMessages(1, 3, long.MaxValue, Pid, _receiverProbe.Ref));
            for (int i = 1; i <= 3; i++) _receiverProbe.ExpectMsg<ReplayedMessage>(m => IsReplayedMessage(m, i));
            _receiverProbe.ExpectMsg<ReplayMessagesSuccess>();
        }

        [Fact]
        public void Journal_should_replay_messages_using_a_count_limit()
        {
            Journal.Tell(new ReplayMessages(1, long.MaxValue, 3, Pid, _receiverProbe.Ref));
            for (int i = 1; i <= 3; i++) _receiverProbe.ExpectMsg<ReplayedMessage>(m => IsReplayedMessage(m, i));
            _receiverProbe.ExpectMsg<ReplayMessagesSuccess>();
        }

        [Fact]
        public void Journal_should_replay_messages_using_lower_and_upper_sequence_number_bound()
        {
            Journal.Tell(new ReplayMessages(2, 4, long.MaxValue, Pid, _receiverProbe.Ref));
            for (int i = 2; i <= 4; i++) _receiverProbe.ExpectMsg<ReplayedMessage>(m => IsReplayedMessage(m, i));
            _receiverProbe.ExpectMsg<ReplayMessagesSuccess>();
        }

        [Fact]
        public void Journal_should_replay_messages_using_lower_and_upper_sequence_number_bound_and_count_limit()
        {
            Journal.Tell(new ReplayMessages(2, 4, 2, Pid, _receiverProbe.Ref));
            for (int i = 2; i <= 3; i++) _receiverProbe.ExpectMsg<ReplayedMessage>(m => IsReplayedMessage(m, i));
            _receiverProbe.ExpectMsg<ReplayMessagesSuccess>();
        }

        [Fact]
        public void Journal_should_replay_a_single_if_lower_sequence_number_bound_equals_upper_sequence_number_bound()
        {
            Journal.Tell(new ReplayMessages(2, 2, long.MaxValue, Pid, _receiverProbe.Ref));
            _receiverProbe.ExpectMsg<ReplayedMessage>(m => IsReplayedMessage(m, 2));
            _receiverProbe.ExpectMsg<ReplayMessagesSuccess>();
        }

        [Fact]
        public void Journal_should_replay_a_single_message_if_count_limit_is_equal_one()
        {
            Journal.Tell(new ReplayMessages(2, 4, 1, Pid, _receiverProbe.Ref));
            _receiverProbe.ExpectMsg<ReplayedMessage>(m => IsReplayedMessage(m, 2));
            _receiverProbe.ExpectMsg<ReplayMessagesSuccess>();
        }

        [Fact]
        public void Journal_should_not_replay_messages_if_count_limit_equals_zero()
        {
            Journal.Tell(new ReplayMessages(2, 4, 0, Pid, _receiverProbe.Ref));
            _receiverProbe.ExpectMsg<ReplayMessagesSuccess>();
        }

        [Fact]
        public void Journal_should_not_replay_messages_if_lower_sequence_number_bound_is_greater_than_upper_sequence_number_bound()
        {
            Journal.Tell(new ReplayMessages(3, 2, long.MaxValue, Pid, _receiverProbe.Ref));
            _receiverProbe.ExpectMsg<ReplayMessagesSuccess>();
        }

        [Fact]
        public void Journal_should_not_replay_permanently_deleted_messages_on_range_deletion()
        {
            var command = new DeleteMessagesTo(Pid, 3, true);
            var subscriber = CreateTestProbe();

            Subscribe<DeleteMessagesTo>(subscriber.Ref);
            Journal.Tell(command);
            subscriber.ExpectMsg<DeleteMessagesTo>(cmd => cmd.PersistenceId == Pid && cmd.ToSequenceNr == 3 && cmd.IsPermanent);

            Journal.Tell(new ReplayMessages(1, long.MaxValue, long.MaxValue, Pid, _receiverProbe.Ref));
            _receiverProbe.ExpectMsg<ReplayedMessage>(m => IsReplayedMessage(m, 4));
            _receiverProbe.ExpectMsg<ReplayedMessage>(m => IsReplayedMessage(m, 5));
        }

        [Fact]
        public void Journal_should_replay_logically_deleted_messages_with_deleted_flag_set_on_range_deletion()
        {
            var command = new DeleteMessagesTo(Pid, 3, false);
            var subscriber = CreateTestProbe();

            Subscribe<DeleteMessagesTo>(subscriber.Ref);
            Journal.Tell(command);
            subscriber.ExpectMsg<DeleteMessagesTo>(cmd => cmd.PersistenceId == Pid && cmd.ToSequenceNr == 3 && !cmd.IsPermanent);

            Journal.Tell(new ReplayMessages(1, long.MaxValue, long.MaxValue, Pid, _receiverProbe.Ref, replayDeleted: true));

            _receiverProbe.ExpectMsg<ReplayedMessage>(m => IsReplayedMessage(m, 1, true));
            _receiverProbe.ExpectMsg<ReplayedMessage>(m => IsReplayedMessage(m, 2, true));
            _receiverProbe.ExpectMsg<ReplayedMessage>(m => IsReplayedMessage(m, 3, true));
            _receiverProbe.ExpectMsg<ReplayedMessage>(m => IsReplayedMessage(m, 4));
            _receiverProbe.ExpectMsg<ReplayedMessage>(m => IsReplayedMessage(m, 5));
        }

        [Fact]
        public void Journal_should_return_a_highest_seq_number_greater_than_zero_if_actor_has_already_written_messages_and_message_log_is_not_empty()
        {
            Journal.Tell(new ReadHighestSequenceNr(3L, Pid, _receiverProbe.Ref));
            _receiverProbe.ExpectMsg<ReadHighestSequenceNrSuccess>(m => m.HighestSequenceNr == 5);

            Journal.Tell(new ReadHighestSequenceNr(5L, Pid, _receiverProbe.Ref));
            _receiverProbe.ExpectMsg<ReadHighestSequenceNrSuccess>(m => m.HighestSequenceNr == 5);
        }

        [Fact]
        public void Journal_should_return_a_highest_sequence_number_equal_zero_if_actor_did_not_written_any_messages_yet()
        {
            Journal.Tell(new ReadHighestSequenceNr(0L, "invalid", _receiverProbe.Ref));
            _receiverProbe.ExpectMsg<ReadHighestSequenceNrSuccess>(m => m.HighestSequenceNr == 0);
        }
    }
}

