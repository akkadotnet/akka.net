using Microsoft.VisualStudio.TestTools.UnitTesting;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.Tests
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

        public static void ShouldBe<T>(this IEnumerable<T> self, IEnumerable<T> other)
        {
            if (self.SequenceEqual(other))
            { }
            else
            {
                Assert.Fail("Expected " + other.Select(i => string.Format("'{0}'", i)).Join(",") + " got " + self.Select(i => string.Format("'{0}'", i)).Join(","));

            }
        }

        public static void ShouldBe<T>(this T self, T other)
        {
            Assert.AreEqual(other, self);
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

        protected Terminated expectTerminated(ActorRef @ref)
        {
            var actual = queue.Take();

            Assert.IsTrue(actual is Terminated);

            return (Terminated)actual;
        }

        protected object expectMsg(object expected)
        {
            var actual = queue.Take();

            global::System.Diagnostics.Debug.WriteLine("actual: " + actual);
            Assert.AreEqual(expected, actual);
            return actual;
        }

        protected void watch(ActorRef @ref)
        {
            var l = testActor as ActorRefWithCell;
            l.Cell.Watch(@ref);
        }

        protected TMessage expectMsgType<TMessage>()
        {
            var actual = queue.Take();

            global::System.Diagnostics.Debug.WriteLine("actual: " + actual);
            Assert.IsTrue(actual is TMessage);
            return (TMessage)actual;
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

        //protected Tuple<T1> T<T1>(T1 item1)
        //{
        //    return Tuple.Create(item1);
        //}

        //protected Tuple<T1,T2> T<T1,T2>(T1 item1,T2 item2)
        //{
        //    return Tuple.Create(item1,item2);
        //}

        //protected Tuple<T1, T2,T3> T<T1, T2,T3>(T1 item1, T2 item2,T3 item3)
        //{
        //    return Tuple.Create(item1, item2, item3);
        //}

        //protected Tuple<T1, T2, T3, T4> T<T1, T2, T3, T4>(T1 item1, T2 item2, T3 item3, T4 item4)
        //{
        //    return Tuple.Create(item1, item2, item3, item4);
        //}
    }
}
