using Microsoft.VisualStudio.TestTools.UnitTesting;
using Akka.Actor;
using System;
using System.Threading;

namespace Akka.Tests.Actor
{
    [TestClass]
    public class AskSpec : AkkaSpec
    {
        public class SomeActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                if (message.Equals("timeout"))
                {
                    Thread.Sleep(5000);
                }
                if (message.Equals("answer"))
                {
                    Sender.Tell("answer");
                }
            }
        }

        public class WaitActor : UntypedActor
        {
            public WaitActor(ActorRef replyActor, ActorRef testActor)
            {
                _replyActor = replyActor;
                _testActor = testActor;
            }

            private ActorRef _replyActor;

            private ActorRef _testActor;

            protected override void OnReceive(object message)
            {
                if (message.Equals("ask"))
                {
                    var result = _replyActor.Ask("foo");
                    result.Wait(TimeSpan.FromSeconds(2));
                    _testActor.Tell(result.Result);
                }
            }
        }

        public class ReplyActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                if (message.Equals("foo"))
                {
                    Sender.Tell("bar");
                }
            }
        }

        [TestMethod]
        public void CanAskActor()
        {
            var actor = sys.ActorOf<SomeActor>();
            actor.Ask<string>("answer").Result.ShouldBe("answer");
        }

        [TestMethod]
        public void CanAskActorWithTimeout()
        {
            var actor = sys.ActorOf<SomeActor>();
            actor.Ask<string>("answer",TimeSpan.FromSeconds(10)).Result.ShouldBe("answer");
        }

        [TestMethod]
        [ExpectedException(typeof(AggregateException))]
        public void CanGetTimeoutWhenAskingActor()
        {
            var actor = sys.ActorOf<SomeActor>();
            actor.Ask<string>("timeout", TimeSpan.FromSeconds(3)).Wait();
        }

        /// <summary>
        /// Tests to ensure that if we wait on the result of an Ask inside an actor's receive loop
        /// that we don't deadlock
        /// </summary>
        [TestMethod]
        public void CanAskActorInsideReceiveLoop()
        {
            var replyActor = sys.ActorOf<ReplyActor>();
            var waitActor = sys.ActorOf(Props.Create(() => new WaitActor(replyActor, testActor)));
            waitActor.Tell("ask");
            expectMsg("bar");
        }
    }
}
