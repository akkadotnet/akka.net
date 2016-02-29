//-----------------------------------------------------------------------
// <copyright file="ReceiveTimeoutSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using Xunit;


namespace Akka.Tests.Actor
{
    public class ReceiveTimeoutSpec : AkkaSpec
    {
        private static readonly object Tick = new object();

        public class TimeoutActor : ActorBase
        {
            private TestLatch _timeoutLatch;

            public TimeoutActor(TestLatch timeoutLatch)
                : this(timeoutLatch, TimeSpan.FromMilliseconds(500))
            {
            }

            public TimeoutActor(TestLatch timeoutLatch, TimeSpan? timeout)
            {
                _timeoutLatch = timeoutLatch;
                Context.SetReceiveTimeout(timeout.GetValueOrDefault());
            }
            protected override bool Receive(object message)
            {
                if (message is ReceiveTimeout)
                {
                    _timeoutLatch.Open();
                    return true;
                }
                if (message == Tick)
                {
                    return true;
                }
                return false;
            }
        }

        public class NoTimeoutActor : ActorBase
        {
            private TestLatch _timeoutLatch;

            public NoTimeoutActor(TestLatch timeoutLatch)
            {
                _timeoutLatch = timeoutLatch;
            }
            protected override bool Receive(object message)
            {
                if (message is ReceiveTimeout)
                {
                    _timeoutLatch.Open();
                    return true;
                }
                if (message == Tick)
                {
                    return true;
                }
                return false;
            }
        }

        [Fact(DisplayName="An actor with receive timeout must get timeout")]
        public void Get_timeout()
        {
            var timeoutLatch = new TestLatch();
            var timeoutActor = Sys.ActorOf(Props.Create(() => new TimeoutActor(timeoutLatch)));

            timeoutLatch.Ready(TestLatch.DefaultTimeout);
            Sys.Stop(timeoutActor);
        }

        //TODO: how does this prove that there was a reschedule?? see ReceiveTimeoutSpec.scala 
        [Fact(DisplayName = "An actor with receive timeout must reschedule timeout after regular receive")]
        public void Reschedule_timeout()
        {
            var timeoutLatch = new TestLatch();
            var timeoutActor = Sys.ActorOf(Props.Create(() => new TimeoutActor(timeoutLatch)));
            timeoutActor.Tell(Tick);
            timeoutLatch.Ready(TestLatch.DefaultTimeout);
            Sys.Stop(timeoutActor);
        }

        [Fact(DisplayName = "An actor with receive timeout must not receive timeout message when not specified")]
        public void Not_get_timeout()
        {
            var timeoutLatch = new TestLatch();
            var timeoutActor = Sys.ActorOf(Props.Create(() => new NoTimeoutActor(timeoutLatch)));
            Assert.Throws<TimeoutException>(() => timeoutLatch.Ready(TestLatch.DefaultTimeout));
            Sys.Stop(timeoutActor);
        }

        [Fact]
        public void Cancel_ReceiveTimeout_When_terminated()
        {
            //This test verifies that bug #469 "ReceiveTimeout isn't cancelled when actor terminates" has been fixed
            var timeoutLatch = CreateTestLatch();
            Sys.EventStream.Subscribe(TestActor, typeof(DeadLetter));
            var timeoutActor = Sys.ActorOf(Props.Create(() => new TimeoutActor(timeoutLatch, TimeSpan.FromMilliseconds(500))));

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

