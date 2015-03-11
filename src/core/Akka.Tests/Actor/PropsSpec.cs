using Akka.Actor;
using Akka.TestKit;
using System;
using Xunit;

namespace Akka.Tests.Actor
{
    public class PropsSpec : AkkaSpec
    {
        [Fact]
        public void Props_must_create_actor_from_type()
        {
            var props = Props.Create<PropsTestActor>();
            TestActorRef<PropsTestActor> actor = new TestActorRef<PropsTestActor>(Sys, props);
            Assert.IsType<PropsTestActor>(actor.UnderlyingActor);
        }

        [Fact]
        public void Props_must_create_actor_by_expression()
        {
            var props = Props.Create(() => new PropsTestActor());
            ActorRef actor = Sys.ActorOf(props);
            Assert.NotNull(actor);
        }

        [Fact]
        public void Props_must_create_actor_by_producer()
        {
            TestLatch latchProducer = new TestLatch(Sys);
            TestLatch latchActor = new TestLatch(Sys);
            var props = Props.CreateBy<TestProducer>(latchProducer, latchActor);
            ActorRef actor = Sys.ActorOf(props);
            latchActor.Ready(TimeSpan.FromSeconds(1));
        }

        private class TestProducer : IndirectActorProducer
        {
            TestLatch latchActor;

            public TestProducer(TestLatch lp, TestLatch la)
            {
                latchActor = la;
                lp.Reset();
                lp.CountDown();
            }

            public ActorBase Produce()
            {
                latchActor.CountDown();
                return new PropsTestActor();
            }

            public Type ActorType
            {
                get { return typeof(PropsTestActor); }
            }


            public void Release(ActorBase actor)
            {
                actor = null;
            }
        }

        private class PropsTestActor : ActorBase
        {
            protected override bool Receive(object message)
            {
                return true;
            }
        }
    }
}
