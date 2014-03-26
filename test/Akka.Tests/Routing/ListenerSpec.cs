using System;
using Akka.Actor;
using Akka.Routing;
using Akka.TestKit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Akka.Tests.Routing
{
    [TestClass]
    public class ListenerSpec : AkkaSpec
    {
        [TestMethod]
        public void Listener_must_listen_in()
        {
            //arrange
            var fooLatch = new TestLatch(sys, 2);
            var barLatch = new TestLatch(sys, 2);
            var barCount = new AtomicInteger(0);

            var broadcast = sys.ActorOf<BroadcastActor>();
            var newListenerProps = Props.Create(() => new ListenerActor(fooLatch, barLatch, barCount));
            var a1 = sys.ActorOf(newListenerProps);
            var a2 = sys.ActorOf(newListenerProps);
            var a3 = sys.ActorOf(newListenerProps);

            //act
            broadcast.Tell(new Listen(a1));
            broadcast.Tell(new Listen(a2));
            broadcast.Tell(new Listen(a3));

            broadcast.Tell(new Deafen(a3));

            broadcast.Tell(new WithListeners(a => a.Tell("foo")));
            broadcast.Tell("foo");

            //assert
            barLatch.Ready(TestLatch.DefaultTimeout);
            Assert.AreEqual(2, barCount.Value);

            fooLatch.Ready(TestLatch.DefaultTimeout);
            foreach (var actor in new[] {a1, a2, a3, broadcast})
            {
                actor.Stop();
            }
        }


        #region Test Actors

        public class BroadcastActor : UntypedActor, IListeners
        {
            public BroadcastActor()
            {
                Listeners = new ListenerSupport();
            }

            protected override void OnReceive(object message)
            {
                PatternMatch.Match(message)
                    .With<ListenerMessage>(l => Listeners.ListenerReceive(l))
                    .With<string>(s =>
                    {
                        if (s.Equals("foo"))
                            Listeners.Gossip("bar");
                    });
            }

            public ListenerSupport Listeners { get; private set; }
        }

        public class ListenerActor : UntypedActor, IListeners
        {
            private TestLatch _fooLatch;
            private TestLatch _barLatch;
            private AtomicInteger _barCount;

            public ListenerActor(TestLatch fooLatch, TestLatch barLatch, AtomicInteger barCount)
            {
                _fooLatch = fooLatch;
                _barLatch = barLatch;
                _barCount = barCount;
                Listeners = new ListenerSupport();
            }

            protected override void OnReceive(object message)
            {
                PatternMatch.Match(message)
                    .With<string>(str =>
                    {
                        if (str.Equals("bar"))
                        {
                            _barCount.GetAndIncrement();
                            _barLatch.CountDown();
                        }

                        if (str.Equals("foo"))
                        {
                            _fooLatch.CountDown();
                        }
                    });
            }

            public ListenerSupport Listeners { get; private set; }
        }


        #endregion
    }
}
