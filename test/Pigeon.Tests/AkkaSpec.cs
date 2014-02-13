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
            queue = new BlockingCollection<object>();
            system = ActorSystem.Create("test");
            testActor = system.ActorOf(Props.Create(() => new TestActor(queue)));            
        }
        protected BlockingCollection<object> queue;
        protected ActorSystem system;
        protected ActorRef testActor;

        protected void expectMsg(object expected)
        {
            var actual = queue.Take();

            global::System.Diagnostics.Debug.WriteLine(actual);
            Assert.AreEqual(expected, actual);            
        }

        protected void expectNoMsg(TimeSpan duration)
        {
            object t;
            if (queue.TryTake(out t,duration))
            {
                Assert.Fail("Expected no messages during the duration");
            }
        }

        public class TestActor : UntypedActor
        {
            private BlockingCollection<object> queue;
            public TestActor(BlockingCollection<object> queue)
            {
                this.queue = queue;
            }
            protected override void OnReceive(object message)
            {
                queue.Add(message);
            }
        }

        //protected void Within(TimeSpan duration,Action body)
        //{
        //    var now = DateTime.Now;
        //    body();

        //}

        protected void intercept<T>(Action intercept) where T:Exception
        {
            try
            {
                intercept();
            }
            catch(Exception x)
            {
                if(x is T)
                {
                    return;
                }
            }
            Assert.Fail("Expected exception of type " + typeof(T).Name);
        }

        protected void EventFilter<T>(string message,int occurances, Action intercept) where T:Exception
        {
            system.EventStream.Subscribe(testActor,typeof(object));
            intercept();
            for(int i = 0;i<occurances;i++)
            {
                var res = queue.Take();
                var error = (Error)res;

                Assert.AreEqual(typeof(T), error.Cause.GetType());
                Assert.AreEqual(message, error.ErrorMessage);                
            }
        }
    }
}
