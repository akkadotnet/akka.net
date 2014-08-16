using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Xunit;
using Akka.TestKit;
using Akka.Tests;


namespace Akka.Actor
{
    public class ReceiveTimeoutSpec : AkkaSpec
    {
        private static readonly object Tick = new object();

        public class TimeoutActor : ActorBase
        {
            private TestLatch _timeoutLatch;

            public TimeoutActor(TestLatch timeoutLatch)
            {
                _timeoutLatch = timeoutLatch;
                Context.SetReceiveTimeout(TimeSpan.FromMilliseconds(500));
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
        public void GetTimeout()
        {
            var timeoutLatch = new TestLatch(Sys);
            var timeoutActor = Sys.ActorOf(Props.Create(() => new TimeoutActor(timeoutLatch)));

            timeoutLatch.Ready(TestLatch.DefaultTimeout);
            Sys.Stop(timeoutActor);
        }

        //TODO: how does this prove that there was a reschedule?? see ReceiveTimeoutSpec.scala 
        [Fact(DisplayName = "An actor with receive timeout must reschedule timeout after regular receive")]
        public void RescheduleTimeout()
        {
            var timeoutLatch = new TestLatch(Sys);
            var timeoutActor = Sys.ActorOf(Props.Create(() => new TimeoutActor(timeoutLatch)));
            timeoutActor.Tell(Tick);
            timeoutLatch.Ready(TestLatch.DefaultTimeout);
            Sys.Stop(timeoutActor);
        }

        [Fact(DisplayName = "An actor with receive timeout must not receive timeout message when not specified")]
        public void NotGetTimeout()
        {
            var timeoutLatch = new TestLatch(Sys);
            var timeoutActor = Sys.ActorOf(Props.Create(() => new NoTimeoutActor(timeoutLatch)));
            Assert.Throws<TimeoutException>(() => timeoutLatch.Ready(TestLatch.DefaultTimeout));
            Sys.Stop(timeoutActor);
        }
    }
}
