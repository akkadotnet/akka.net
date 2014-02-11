using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pigeon.Actor;
using Pigeon.Event;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pigeon.Tests
{    
    public class AkkaSpec
    {
        [TestInitialize]
        public void Setup()
        {
            queue = new BlockingCollection<Tuple<string, string, int>>();
            system = ActorSystem.Create("test");
            testActor = system.ActorOf(Props.Create(() => new TestActor(queue)));            
        }
        protected BlockingCollection<Tuple<string, string, int>> queue;
        protected ActorSystem system;
        protected ActorRef testActor;

        protected void expectMsg(string expectedText, string expectedId, int expectedGeneration)
        {
            var t = queue.Take();
            var actualText = t.Item1;
            var actualId = t.Item2;
            var actualGeneration = t.Item3;
            Debug.WriteLine(actualText);

            Assert.AreEqual(expectedText, actualText);
            Assert.AreEqual(expectedGeneration, actualGeneration);
            Assert.AreEqual(expectedId, actualId);
        }

        protected void expectNoMsg(TimeSpan duration)
        {
            Tuple<string,string,int> t;
            if (queue.TryTake(out t,duration))
            {
                Assert.Fail("Expected no messages during the duration");
            }
        }

        public class TestActor : UntypedActor
        {
            private BlockingCollection<Tuple<string, string, int>> queue;
            public TestActor(BlockingCollection<Tuple<string, string, int>> queue)
            {
                this.queue = queue;
            }
            protected override void OnReceive(object message)
            {
                if (message is Tuple<string, string, int>)
                {
                    queue.Add((Tuple<string, string, int>)message);
                }
            }
        }

        protected void EventFilter<T>(string message,int occurances, Action intercept) where T:Exception
        {
            var events = new BlockingCollection<EventMessage>();
            system.EventStream.Subscribe(new BlockingCollectionSubscriber(events));
            intercept();
            for(int i = 0;i<occurances;i++)
            {
                var res = events.Take();
                var error = (Error)res;

                Assert.AreEqual(typeof(T), error.Cause.GetType());
                Assert.AreEqual(message, error.ErrorMessage);                
            }
        }
    }
}
