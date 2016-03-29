//-----------------------------------------------------------------------
// <copyright file="ReceiveTimeoutSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using Xunit;
using Akka.Actor.Dsl;

namespace Akka.Tests.Actor
{
    public class ReceiveTimeoutSpec : AkkaSpec
    {
        public class Tick
        {
        }

        public class TransperentTick : INotInfluenceReceiveTimeout
        {
        }

        [Fact]
        public void Get_timeout()
        {
            var timeoutLatch = new TestLatch();

            var timeoutActor = Sys.ActorOf((act, context) =>
            {
                context.SetReceiveTimeout(TimeSpan.FromMilliseconds(500));

                act.Receive<ReceiveTimeout>((timeout, ctx) =>
                {
                    timeoutLatch.Open();
                });
            });

            timeoutLatch.Ready(TestKitSettings.DefaultTimeout);
            Sys.Stop(timeoutActor);
        }

        [Fact]
        public void Reschedule_timeout_after_regular_receive()
        {
            var timeoutLatch = new TestLatch();

            var timeoutActor = Sys.ActorOf((act, context) =>
            {
                context.SetReceiveTimeout(TimeSpan.FromMilliseconds(500));

                act.Receive<Tick>((timeout, ctx) =>
                {
                    
                });

                act.Receive<ReceiveTimeout>((timeout, ctx) =>
                {
                    timeoutLatch.Open();
                });
            });

            timeoutActor.Tell(new Tick());

            timeoutLatch.Ready(TestKitSettings.DefaultTimeout);
            Sys.Stop(timeoutActor);
        }

        [Fact]
        public void Be_able_to_turn_off_timeout_if_desired()
        {
            int count = 0;
            var timeoutLatch = new TestLatch();

            var timeoutActor = Sys.ActorOf((act, context) =>
            {
                context.SetReceiveTimeout(TimeSpan.FromMilliseconds(500));

                act.Receive<Tick>((timeout, ctx) =>
                {

                });

                act.Receive<ReceiveTimeout>((timeout, ctx) =>
                {
                    Interlocked.Increment(ref count);
                    timeoutLatch.Open();
                    context.SetReceiveTimeout(null);
                });
            });

            timeoutActor.Tell(new Tick());

            timeoutLatch.Ready(TestKitSettings.DefaultTimeout);
            Assert.Equal(1, count);
            Sys.Stop(timeoutActor);
        }

        [Fact]
        public void Not_receive_timeout_message_when_not_specified()
        {
            var timeoutLatch = new TestLatch();

            var timeoutActor = Sys.ActorOf((act, context) =>
            {
                act.Receive<ReceiveTimeout>((timeout, ctx) =>
                {
                    timeoutLatch.Open();
                });
            });

            Assert.Throws<TimeoutException>(() => timeoutLatch.Ready(TimeSpan.FromSeconds(1)));
            Sys.Stop(timeoutActor);
        }

        [Fact]
        public void Get_timeout_while_receiving_NotInfluenceReceiveTimeout_messages()
        {
            var timeoutLatch = CreateTestLatch();

            var timeoutActor = Sys.ActorOf((act, context) =>
            {
                context.SetReceiveTimeout(TimeSpan.FromSeconds(1));

                act.Receive<ReceiveTimeout>((timeout, ctx) =>
                {
                    timeoutLatch.Open();
                });

                act.Receive<TransperentTick>((timeout, ctx) =>
                {

                });
            });

            var ticks = Sys.Scheduler.ScheduleTellRepeatedlyCancelable(TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(100), timeoutActor, new TransperentTick(), TestActor);

            timeoutLatch.Ready(TestKitSettings.DefaultTimeout);
            ticks.Cancel();
            Sys.Stop(timeoutActor);
        }


        [Fact]
        public void Issue469_Cancel_ReceiveTimeout_When_terminated()
        {
            var timeoutLatch = CreateTestLatch();
            Sys.EventStream.Subscribe(TestActor, typeof(DeadLetter));

            IActorRef timeoutActor = Sys.ActorOf((act, context) =>
            {
                context.SetReceiveTimeout(TimeSpan.FromMilliseconds(500));

                act.Receive<ReceiveTimeout>((timeout, ctx) =>
                {
                    timeoutLatch.Open();
                });
            });

            //make sure TestActor gets a notification when timeoutActor terminates
            Watch(timeoutActor);

            // wait for first ReceiveTimeout message, in which the latch is opened
            timeoutLatch.Ready(TimeSpan.FromSeconds(2));

            //Stop and wait for the actor to terminate
            Sys.Stop(timeoutActor);
            ExpectTerminated(timeoutActor);

            //We should not get any messages now. If we get a message now, 
            //it's a DeadLetter with ReceiveTimeout, meaning the receivetimeout wasn't cancelled.
            ExpectNoMsg(TimeSpan.FromSeconds(1));
        }
    }
}

