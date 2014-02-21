using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pigeon.Actor;
using Pigeon.Configuration;
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
    public static class AkkaSpecExtensions
    {
        public static void Then<T>(this T self,Action<T> body)
        {
            body(self);
        }

        public static void Then<T>(this T self,Action<T,T> body,T other)
        {
            body(other, self);
        }
    }


    public class AkkaSpec
    {

        protected virtual string GetConfig()
        {
            return "";
        }
        [TestInitialize]
        public void Setup()
        {
            var config = ConfigurationFactory.ParseString(GetConfig());
            queue = new BlockingCollection<object>();
            messages = new List<object>();
            sys = ActorSystem.Create("test",config);
            testActor = sys.ActorOf(Props.Create(() => new TestActor(queue,messages)),"test");
            echoActor = sys.ActorOf(Props.Create(() => new EchoActor(testActor)), "echo");
        }
        protected BlockingCollection<object> queue;
        protected List<object> messages;
        protected ActorSystem sys;
        protected ActorRef testActor;
        protected ActorRef echoActor;

        protected void expectMsg(object expected)
        {
            var actual = queue.Take();

            global::System.Diagnostics.Debug.WriteLine("actual: " + actual);
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
            private List<object> messages;
            public TestActor(BlockingCollection<object> queue,List<object> messages)
            {
                this.queue = queue;
                this.messages = messages;
            }
            protected override void OnReceive(object message)
            {
                global::System.Diagnostics.Debug.WriteLine("testactor received " + message);
                messages.Add(message);
                queue.Add(message);
            }
        }

        /// <summary>
        /// Used for testing Ask / reply behaviors
        /// </summary>
        public class EchoActor : UntypedActor
        {
            private ActorRef _testActor;

            public EchoActor(ActorRef testActorRef)
            {
                _testActor = testActorRef;
            }

            protected override void OnReceive(object message)
            {
                Sender.Tell(message, Self);
                _testActor.Tell(message, Sender);
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
            sys.EventStream.Subscribe(testActor,typeof(Error));
            intercept();
            for(int i = 0;i<occurances;i++)
            {
                var res = queue.Take();
                var error = (Error)res;

                Assert.AreEqual(typeof(T), error.Cause.GetType());
                Assert.AreEqual(message, error.Message);                
            }
        }


        protected TestProbe TestProbe()
        {
            return new TestProbe();
        }
    }
}
