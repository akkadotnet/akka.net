//-----------------------------------------------------------------------
// <copyright file="TestSchedulerTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit.Configs;
using Xunit;

namespace Akka.TestKit.Tests
{
    public class TestSchedulerTests : AkkaSpec
    {
        private IActorRef _testReceiveActor;

        public TestSchedulerTests()
            : base(TestConfigs.TestSchedulerConfig)
        {
            _testReceiveActor = Sys.ActorOf(Props.Create(() => new TestReceiveActor())
                .WithDispatcher(CallingThreadDispatcher.Id));
        }
        
        [Fact]
        public void delivers_message_when_scheduled_time_reached()
        {
            _testReceiveActor.Tell(new ScheduleOnceMessage(TimeSpan.FromSeconds(1)));
            Scheduler.Advance(TimeSpan.FromSeconds(1));
            ExpectMsg<ScheduleOnceMessage>();
        }

        [Fact]
        public void does_not_deliver_message_prematurely()
        {
            _testReceiveActor.Tell(new ScheduleOnceMessage(TimeSpan.FromSeconds(1)));
            Scheduler.Advance(TimeSpan.FromMilliseconds(999));
            ExpectNoMsg(TimeSpan.FromMilliseconds(20));
        }

        [Fact]
        public void delivers_messages_scheduled_for_same_time_in_order_they_were_added()
        {
            _testReceiveActor.Tell(new ScheduleOnceMessage(TimeSpan.FromSeconds(1), 1));
            _testReceiveActor.Tell(new ScheduleOnceMessage(TimeSpan.FromSeconds(1), 2));
            Scheduler.Advance(TimeSpan.FromSeconds(1));
            var firstId = ExpectMsg<ScheduleOnceMessage>().Id;
            var secondId = ExpectMsg<ScheduleOnceMessage>().Id;
            Assert.Equal(1, firstId);
            Assert.Equal(2, secondId);
        }

        [Fact]
        public void keeps_delivering_rescheduled_message()
        {
            _testReceiveActor.Tell(new RescheduleMessage(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)));
            
            for (int i = 0; i < 500; i ++)
            {
                Scheduler.Advance(TimeSpan.FromSeconds(5));
                ExpectMsg<RescheduleMessage>();
            }
        }

        [Fact]
        public void uses_initial_delay_to_schedule_first_rescheduled_message()
        {
            _testReceiveActor.Tell(new RescheduleMessage(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)));
            Scheduler.Advance(TimeSpan.FromSeconds(1));
            ExpectMsg<RescheduleMessage>();
        }

        [Fact]
        public void doesnt_reschedule_cancelled()
        {
            _testReceiveActor.Tell(new CancelableMessage(TimeSpan.FromSeconds(1)));
            Scheduler.Advance(TimeSpan.FromSeconds(1));
            ExpectMsg<CancelableMessage>();
            _testReceiveActor.Tell(new CancelMessage());
            Scheduler.Advance(TimeSpan.FromSeconds(1));
            ExpectNoMsg(TimeSpan.FromMilliseconds(20));
        }


        [Fact]
        public void advance_to_takes_us_to_correct_time()
        {
            _testReceiveActor.Tell(new ScheduleOnceMessage(TimeSpan.FromSeconds(1), 1));
            _testReceiveActor.Tell(new ScheduleOnceMessage(TimeSpan.FromSeconds(2), 2));
            _testReceiveActor.Tell(new ScheduleOnceMessage(TimeSpan.FromSeconds(3), 3));
            Scheduler.AdvanceTo(Scheduler.Now.AddSeconds(2));
            var firstId = ExpectMsg<ScheduleOnceMessage>().Id;
            var secondId = ExpectMsg<ScheduleOnceMessage>().Id;
            ExpectNoMsg(TimeSpan.FromMilliseconds(20));
            Assert.Equal(1, firstId);
            Assert.Equal(2, secondId);
        }

        class TestReceiveActor : ReceiveActor
        {
            private Cancelable _cancelable;

            public TestReceiveActor()
            {
                Receive<ScheduleOnceMessage>(x =>
                {
                    Context.System.Scheduler.ScheduleTellOnce(x.ScheduleOffset, Sender, x, Self);
                });

                Receive<RescheduleMessage>(x =>
                {
                    Context.System.Scheduler.ScheduleTellRepeatedly(x.InitialOffset, x.ScheduleOffset, Sender, x, Self);
                });

                Receive<CancelableMessage>(x =>
                {
                    _cancelable = new Cancelable(Context.System.Scheduler);
                    Context.System.Scheduler.ScheduleTellRepeatedly(x.ScheduleOffset, x.ScheduleOffset, Sender, x, Self, _cancelable);
                });

                Receive<CancelMessage>(x =>
                {
                    _cancelable.Cancel();
                });

            }
        }

        class CancelableMessage
        {
            public TimeSpan ScheduleOffset { get; set; }
            public int Id { get; set; }

            public CancelableMessage(TimeSpan scheduleOffset, int id = 1)
            {
                ScheduleOffset = scheduleOffset;
                Id = id;
            }
        }

        class CancelMessage { }

        class ScheduleOnceMessage
        {
            public TimeSpan ScheduleOffset { get; set; }
            public int Id { get; set; }

            public ScheduleOnceMessage(TimeSpan scheduleOffset, int id = 1)
            {
                ScheduleOffset = scheduleOffset;
                Id = id;
            }
        }

        class RescheduleMessage
        {
            public TimeSpan InitialOffset { get; set; }
            public TimeSpan ScheduleOffset { get; set; }
            public int Id { get; set; }

            public RescheduleMessage(TimeSpan initialOffset, TimeSpan scheduleOffset, int id = 1)
            {
                InitialOffset = initialOffset;
                ScheduleOffset = scheduleOffset;
                Id = id;
            }
        }


        private TestScheduler Scheduler { get {  return (TestScheduler)Sys.Scheduler;} }
    }
}
