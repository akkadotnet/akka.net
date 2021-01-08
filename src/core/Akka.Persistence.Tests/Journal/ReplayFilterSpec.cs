//-----------------------------------------------------------------------
// <copyright file="ReplayFilterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Pattern;
using Akka.Persistence.Journal;
using Akka.TestKit;
using Xunit;

namespace Akka.Persistence.Tests.Journal
{
    public class ReplayFilterSpec : AkkaSpec
    {
        private const string WriterA = "writer-A";
        private const string WriterB = "writer-B";
        private const string WriterC = "writer-C";

        private readonly ReplayedMessage _n1 =
            new ReplayedMessage(new Persistent("a", 13, "p1", "", writerGuid: Persistent.Undefined));

        private readonly ReplayedMessage _n2 =
            new ReplayedMessage(new Persistent("b", 14, "p1", "", writerGuid: Persistent.Undefined));

        private readonly ReplayedMessage _m1 =
            new ReplayedMessage(new Persistent("a", 13, "p1", "", writerGuid: WriterA));

        private readonly ReplayedMessage _m2 =
            new ReplayedMessage(new Persistent("b", 14, "p1", "", writerGuid: WriterA));

        private readonly ReplayedMessage _m3 =
            new ReplayedMessage(new Persistent("c", 15, "p1", "", writerGuid: WriterA));

        private readonly ReplayedMessage _m4 =
            new ReplayedMessage(new Persistent("d", 16, "p1", "", writerGuid: WriterA));

        private readonly RecoverySuccess _successMsg = new RecoverySuccess(15);

        private static IPersistentRepresentation WithWriter(IPersistentRepresentation p, string writer)
        {
            return p.Update(p.SequenceNr, p.PersistenceId, p.IsDeleted, p.Sender, writer);

        }

        [Fact]
        public void ReplayFilter_in_RepairByDiscardOld_mode_should_pass_on_all_replayed_messages_and_then_stop()
        {
            var filter = Sys.ActorOf(ReplayFilter.Props(TestActor, ReplayFilterMode.RepairByDiscardOld, 2, 10, false));
            filter.Tell(_m1);
            filter.Tell(_m2);
            filter.Tell(_m3);
            filter.Tell(_successMsg);

            ExpectMsg(_m1);
            ExpectMsg(_m2);
            ExpectMsg(_m3);
            ExpectMsg(_successMsg);

            Watch(filter);
            ExpectTerminated(filter);
        }

        [Fact]
        public void ReplayFilter_in_RepairByDiscardOld_mode_should_pass_on_all_replayed_messages_when_previously_no_writer_id_was_given_but_now_is_and_then_stop()
        {
            var filter = Sys.ActorOf(ReplayFilter.Props(TestActor, ReplayFilterMode.RepairByDiscardOld, 2, 10, true));
            filter.Tell(_n1);
            filter.Tell(_n2);
            filter.Tell(_m3);
            filter.Tell(_successMsg);

            ExpectMsg(_n1);
            ExpectMsg(_n2);
            ExpectMsg(_m3);
            ExpectMsg(_successMsg);

            Watch(filter);
            ExpectTerminated(filter);
        }

        [Fact]
        public void ReplayFilter_in_RepairByDiscardOld_mode_should_pass_on_all_replayed_messages_when_switching_writer()
        {
            var filter = Sys.ActorOf(ReplayFilter.Props(TestActor, ReplayFilterMode.RepairByDiscardOld, 100, 10, false));
            filter.Tell(_m1);
            filter.Tell(_m2);
            var m32 = new ReplayedMessage(WithWriter(_m3.Persistent, WriterB));
            filter.Tell(m32);
            filter.Tell(_successMsg);

            ExpectMsg(_m1);
            ExpectMsg(_m2);
            ExpectMsg(m32);
            ExpectMsg(_successMsg);
        }

        [Fact]
        public void ReplayFilter_in_RepairByDiscardOld_mode_should_discard_message_with_same_SequenceNo_from_old_overlapping_writer()
        {
            var filter = Sys.ActorOf(ReplayFilter.Props(TestActor, ReplayFilterMode.RepairByDiscardOld, 100, 10, false));
            EventFilter.Warning(start: "Invalid replayed event").ExpectOne(() =>
            {
                filter.Tell(_m1);
                filter.Tell(_m2);
                filter.Tell(_m3);
                var m3B = new ReplayedMessage(WithWriter(_m3.Persistent, WriterB));
                filter.Tell(m3B); // same SequenceNo as m3, but from WriterB
                filter.Tell(_successMsg);

                ExpectMsg(_m1);
                ExpectMsg(_m2);
                ExpectMsg(m3B); // discard m3, because same SequenceNo from new writer
                ExpectMsg(_successMsg);
            });
        }

        [Fact]
        public void ReplayFilter_in_RepairByDiscardOld_mode_should_discard_messages_from_old_writer_after_switching_writer()
        {
            var filter = Sys.ActorOf(ReplayFilter.Props(TestActor, ReplayFilterMode.RepairByDiscardOld, 100, 10, false));
            EventFilter.Warning(start: "Invalid replayed event").Expect(2, () =>
            {
                filter.Tell(_m1);
                filter.Tell(_m2);
                var m3B = new ReplayedMessage(WithWriter(_m3.Persistent, WriterB));
                filter.Tell(m3B);
                filter.Tell(_m3);
                filter.Tell(_m4);
                filter.Tell(_successMsg);

                ExpectMsg(_m1);
                ExpectMsg(_m2);
                ExpectMsg(m3B);
                // discard m3, m4
                ExpectMsg(_successMsg);
            });
        }

        [Fact]
        public void ReplayFilter_in_RepairByDiscardOld_mode_should_discard_messages_from_several_old_writers()
        {
            var filter = Sys.ActorOf(ReplayFilter.Props(TestActor, ReplayFilterMode.RepairByDiscardOld, 100, 10, false));
            EventFilter.Warning(start: "Invalid replayed event").Expect(3, () =>
            {
                filter.Tell(_m1);
                var m2B = new ReplayedMessage(WithWriter(_m2.Persistent, WriterB));
                filter.Tell(m2B);
                var m3C = new ReplayedMessage(WithWriter(_m3.Persistent, WriterC));
                filter.Tell(m3C);
                filter.Tell(_m2);
                filter.Tell(_m3);
                var m3B = new ReplayedMessage(WithWriter(_m3.Persistent, WriterB));
                filter.Tell(m3B);
                var m4C = new ReplayedMessage(WithWriter(_m3.Persistent, WriterC));
                filter.Tell(m4C);
                filter.Tell(_successMsg);

                ExpectMsg(_m1);
                ExpectMsg(m2B);
                ExpectMsg(m3C);
                // discard m2, m3, m3B
                ExpectMsg(m4C);
                ExpectMsg(_successMsg);
            });
        }

        [Fact]
        public void ReplayFilter_in_Fail_mode_should_fail_when_message_with_same_SequenceNo_from_old_overlapping_writer()
        {
            var filter = Sys.ActorOf(ReplayFilter.Props(TestActor, ReplayFilterMode.Fail, 100, 10, false));
            EventFilter.Error(start: "Invalid replayed event").ExpectOne(() =>
            {
                filter.Tell(_m1);
                filter.Tell(_m2);
                filter.Tell(_m3);
                var m3B = new ReplayedMessage(WithWriter(_m3.Persistent, WriterB));
                filter.Tell(m3B); // same as SequenceNo as m3, but from WriterB
                filter.Tell(_successMsg);

                ExpectMsg<ReplayMessagesFailure>(m => m.Cause is IllegalStateException);
            });
        }

        [Fact]
        public void ReplayFilter_in_Fail_mode_should_fail_when_messages_from_old_writer_after_switching_writer()
        {
            var filter = Sys.ActorOf(ReplayFilter.Props(TestActor, ReplayFilterMode.Fail, 100, 10, false));
            EventFilter.Error(start: "Invalid replayed event").ExpectOne(() =>
            {
                filter.Tell(_m1);
                filter.Tell(_m2);
                var m3B = new ReplayedMessage(WithWriter(_m3.Persistent, WriterB));
                filter.Tell(m3B);
                filter.Tell(_m3);
                filter.Tell(_m4);
                filter.Tell(_successMsg);

                ExpectMsg<ReplayMessagesFailure>(m => m.Cause is IllegalStateException);
            });
        }

        [Fact]
        public void ReplayFilter_in_Warn_mode_should_warn_about_message_with_same_SequenceNo_from_old_overlapping_writer()
        {
            var filter = Sys.ActorOf(ReplayFilter.Props(TestActor, ReplayFilterMode.Warn, 100, 10, false));
            EventFilter.Warning(start: "Invalid replayed event").ExpectOne(() =>
            {
                filter.Tell(_m1);
                filter.Tell(_m2);
                filter.Tell(_m3);
                var m3B = new ReplayedMessage(WithWriter(_m3.Persistent, WriterB));
                filter.Tell(m3B); // same as SequenceNo as m3, but from WriterB
                filter.Tell(_successMsg);

                ExpectMsg(_m1);
                ExpectMsg(_m2);
                ExpectMsg(_m3);
                ExpectMsg(m3B);
                ExpectMsg(_successMsg);
            });
        }

        [Fact]
        public void ReplayFilter_in_Warn_mode_should_warn_about_message_from_old_writer_after_switching_writer()
        {
            var filter = Sys.ActorOf(ReplayFilter.Props(TestActor, ReplayFilterMode.Warn, 100, 10, false));
            EventFilter.Warning(start: "Invalid replayed event").Expect(2, () =>
            {
                filter.Tell(_m1);
                filter.Tell(_m2);
                var m3B = new ReplayedMessage(WithWriter(_m3.Persistent, WriterB));
                filter.Tell(m3B);
                filter.Tell(_m3);
                filter.Tell(_m4);
                filter.Tell(_successMsg);

                ExpectMsg(_m1);
                ExpectMsg(_m2);
                ExpectMsg(m3B);
                ExpectMsg(_m3);
                ExpectMsg(_m4);
                ExpectMsg(_successMsg);
            });
        }

        [Fact]
        public void ReplayFilter_in_Warn_mode_should_warn_about_messages_from_several_old_writers()
        {
            var filter = Sys.ActorOf(ReplayFilter.Props(TestActor, ReplayFilterMode.Warn, 100, 10, false));
            EventFilter.Warning(start: "Invalid replayed event").Expect(3, () =>
            {
                filter.Tell(_m1);
                var m2B = new ReplayedMessage(WithWriter(_m2.Persistent, WriterB));
                filter.Tell(m2B);
                var m3C = new ReplayedMessage(WithWriter(_m3.Persistent, WriterC));
                filter.Tell(m3C);
                filter.Tell(_m2);
                filter.Tell(_m3);
                var m3B = new ReplayedMessage(WithWriter(_m3.Persistent, WriterB));
                filter.Tell(m3B);
                var m4C = new ReplayedMessage(WithWriter(_m3.Persistent, WriterC));
                filter.Tell(m4C);
                filter.Tell(_successMsg);

                ExpectMsg(_m1);
                ExpectMsg(m2B);
                ExpectMsg(m3C);
                ExpectMsg(_m2);
                ExpectMsg(_m3);
                ExpectMsg(m3B);
                ExpectMsg(m4C);
                ExpectMsg(_successMsg);
            });
        }
    }
}
