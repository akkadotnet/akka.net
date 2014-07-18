using System.Collections;
using System.IO;
using Akka.TestKit;
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
        public static void Then<T>(this T self, Action<T> body)
        {
            body(self);
        }

        public static void Then<T>(this T self, Action<T, T> body, T other)
        {
            body(other, self);
        }

        public static void ShouldBe<T>(this IEnumerable<T> self, IEnumerable<T> other)
        {
            Xunit.Assert.True(self.SequenceEqual(other), "Expected " + other.Select(i => string.Format("'{0}'", i)).Join(",") + " got " + self.Select(i => string.Format("'{0}'", i)).Join(","));
        }

        public static void ShouldBe<T>(this T actual, T expected, string message = null)
        {
            Xunit.Assert.Equal(expected, actual);
        }

        public static void ShouldOnlyContainInOrder<T>(this IEnumerable<T> actual, params T[] expected)
        {
            ShouldBe(actual, (IEnumerable<T>)expected);
        }

        public static async Task ThrowsAsync<TException>(Func<Task> func)
        {
            var expected = typeof(TException);
            Type actual = null;
            try
            {
                await func();
            }
            catch (Exception e)
            {
                actual = e.GetType();
            }

            Xunit.Assert.Equal(expected, actual);
        }
    }

    public abstract class Message
    {
        public abstract object Msg { get; }

        public abstract ActorRef Sender { get; }
    }

    public class RealMessage : Message
    {
        public RealMessage(object msg, ActorRef sender)
        {
            _msg = msg;
            _sender = sender;
        }

        private object _msg;
        public override object Msg { get { return _msg; } }

        private ActorRef _sender;

        public override ActorRef Sender
        {
            get { return _sender; }
        }
    }

    public class NullMessage : Message
    {
        public override object Msg
        {
            get { throw new IllegalActorStateException("last receive did not dequeue a message"); }
        }

        public override ActorRef Sender
        {
            get { throw new IllegalActorStateException("last receive did not dequeue a message"); }
        }
    }

    public abstract class TestKitBase
    {
        private ActorSystem _system;
        private TestKitSettings _testKitSettings;

        protected ActorSystem System
        {
            get { return _system; }
            set
            {
                if(_system!=value)
                {
                    _system = value;
                    _testKitSettings = TestKitExtension.For(_system);
                }
            }
        }

        public TestKitSettings TestKitSettings { get { return _testKitSettings; } }
    }

    public class AkkaSpec : TestKitBase, IDisposable
    {

        public AkkaSpec()
        {
            var config = ConfigurationFactory.ParseString(GetConfig());
            queue = new BlockingCollection<object>();
            messages = new List<object>();
            System = ActorSystem.Create("test", config);
            testActor = sys.ActorOf(Props.Create(() => new TestActor(queue, messages)), "test");
            echoActor = sys.ActorOf(Props.Create(() => new EchoActor(testActor)), "echo");
        }

        protected virtual string GetConfig()
        {
            return "";
        }

        public virtual void Dispose()
        {

        }

        protected BlockingCollection<object> queue;
        protected List<object> messages;
        protected ActorSystem sys { get { return System; } }
        protected ActorRef testActor;
        protected ActorRef echoActor;

        private DateTime end = DateTime.MinValue;
        private bool lastWasNoMsg = false;

        public TimeSpan DefaultTimeout { get { return TestKitSettings.DefaultTimeout; } }

        private Message lastMessage = new NullMessage();

        protected Terminated expectTerminated(ActorRef @ref)
        {
            var actual = queue.Take();

            Xunit.Assert.True(actual is Terminated);

            return (Terminated)actual;
        }

        protected Terminated expectTerminated(ActorRef @ref, TimeSpan timeout)
        {
            var cancellationTokenSource = new CancellationTokenSource((int)timeout.TotalMilliseconds);
            var actual = queue.Take(cancellationTokenSource.Token);

            Xunit.Assert.True(actual is Terminated);

            return (Terminated)actual;
        }

        protected object expectMsg(object expected)
        {
            var actual = queue.Take();

            global::System.Diagnostics.Debug.WriteLine("actual: " + actual);
            Xunit.Assert.Equal(expected, actual);
            return actual;
        }

        protected object expectMsg(object expected, TimeSpan timeout)
        {
            object t;
            bool success = queue.TryTake(out t, timeout);
            Xunit.Assert.True(success, string.Format("exected message {0} but timed out after {1}", expected, timeout));
            Xunit.Assert.Equal(expected, t);

            return t;
        }

        protected object expectMsg(object expected, Func<object, object, bool> comparer)
        {
            var actual = queue.Take();

            Xunit.Assert.True(comparer(expected, actual));
            return actual;

        }

        protected object expectMsg(object expected, Func<object, object, bool> comparer, TimeSpan timeout)
        {
            object t;
            bool success = queue.TryTake(out t, timeout);
            
            Xunit.Assert.True(success, string.Format("exected message {0} but timed out after {1}", expected, timeout));
            Xunit.Assert.True(comparer(expected, t));

            return t;
        }

        protected void watch(ActorRef @ref)
        {
            var l = testActor as LocalActorRef;
            l.Cell.Watch(@ref);
        }

        protected TMessage expectMsgType<TMessage>()
        {
            var actual = queue.Take();

            global::System.Diagnostics.Debug.WriteLine("actual: " + actual);
            Xunit.Assert.True(actual is TMessage, string.Format("expected message of type {0} but received {1} instead", typeof(TMessage), actual.GetType()));
            return (TMessage)actual;
        }

        protected TMessage expectMsgType<TMessage>(TimeSpan timeout)
        {
            object actual;
            bool success = queue.TryTake(out actual, timeout);
            
            Xunit.Assert.True(success, string.Format("expected message of type {0} but timed out after {1}", typeof(TMessage), timeout));
            Xunit.Assert.True(actual is TMessage, string.Format("expected message of type {0} but received {1} instead", typeof(TMessage), actual.GetType()));

            return default(TMessage);
        }

        /// <summary>
        /// Uses an epsilon value to compare between floating point numbers.
        /// Uses a default epsilon value of 0.001d
        /// </summary>
        protected void ShouldBe(double actual, double expected, double epsilon = 0.001d)
        {
            Xunit.Assert.True(Math.Abs(actual - expected) <= epsilon, string.Format("Expected {0} but received {1}", expected, actual));
        }

        protected TMessage ExpectMsgPF<TMessage>(TimeSpan timeout, Predicate<TMessage> isMessage, string hint = "")
        {
            object actual;
            bool success = queue.TryTake(out actual, timeout);

            Xunit.Assert.True(success, string.Format("expected message of type {0} but timed out after {1}", typeof(TMessage), timeout));
            Xunit.Assert.True(actual is TMessage, string.Format("expected message of type {0} but received {1} instead", typeof(TMessage), actual.GetType()));
            var message = (TMessage) actual;
            Xunit.Assert.True(isMessage(message), string.Format("expected {0} but got {1} instead", hint, message));
            return message;
        }

        protected T expectMsgPF<T>(TimeSpan duration, string hint, Func<object, T> pf)
        {
            object t;
            bool success = queue.TryTake(out t, duration);

            Xunit.Assert.True(success, string.Format("timeout {0} during expectMsg: {1}", duration, hint));
            Xunit.Assert.True(t != null, string.Format("expected {0} but got null message", hint));
            Xunit.Assert.True(pf.Method.GetParameters().Any(x => x.ParameterType.IsInstanceOfType(t)), string.Format("expected {0} but got {1} instead", hint, t));
            return pf.Invoke(t);
        }

        protected T expectMsgPF<T>(string hint, Func<object, T> pf)
        {
            object t = queue.Take();
            Xunit.Assert.True(pf.Method.GetParameters().Any(x => x.ParameterType.IsInstanceOfType(t)), string.Format("expected {0} but got {1} instead", hint, t));
            return pf.Invoke(t);
        }

        protected void expectNoMsg(TimeSpan duration)
        {
            object t;
            bool success = queue.TryTake(out t, duration);
            Xunit.Assert.False(success, string.Format("Expected no messages during the duration, instead we received {0}", t));
        }

        /// <summary>
        /// Execute code block while bounding its execution time between <see cref="min"/> and <see cref="max"/>.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <param name="function"></param>
        /// <returns></returns>
        protected T Within<T>(TimeSpan min, TimeSpan max, Func<T> function)
        {
            var start = DateTime.UtcNow;
            var rem = end == DateTime.MinValue ? TimeSpan.MaxValue : end - start;
            Xunit.Assert.True(rem >= min, string.Format("Required min time {0} not possible, only {1} left", min, rem));

            lastWasNoMsg = false;

            var max_diff = Min(max, rem);
            var prev_end = end;
            end = start + max_diff;

            T ret;
            try
            {
                ret = function();
            }
            finally
            {
                end = prev_end;
            }

            var diff = DateTime.UtcNow - start;
            Xunit.Assert.True(min <= diff, string.Format("block took {0}, should have at least been {1}", diff, min));
            if (!lastWasNoMsg)
            {
                Xunit.Assert.True(diff <= max_diff, string.Format("block took {0}, exceeding {1}", diff, max_diff));
            }

            return ret;
        }

        /// <summary>
        /// Wait until a given condition evaluates to true or timeout, whichever happens first
        /// </summary>
        public static async Task<bool> AwaitCond(Func<bool> evaluator, TimeSpan max, bool noThrow = false)
        {
            return await AwaitCond(evaluator, max, TimeSpan.FromMilliseconds(100), noThrow);
        }

        /// <summary>
        /// Wait until a given condition evaluates to true or timeout, whichever happens first
        /// </summary>
        public static async Task<bool> AwaitCond(Func<bool> evaluator, TimeSpan max, TimeSpan interval, bool noThrow = false)
        {
            var stop = DateTime.UtcNow + max;
            return await Task.Run(() =>
            {
                while (!evaluator())
                {
                    var sleep = stop - DateTime.UtcNow;
                    if (sleep <= TimeSpan.Zero)
                    {
                        if (noThrow) return false;
                        else Xunit.Assert.True(false, string.Format("timeout {0} expired", max));
                    }
                    else
                    {
                        Thread.Sleep(Min(sleep, interval));
                    }
                }

                return true;
            });
        }

        protected T Within<T>(TimeSpan max, Func<T> function)
        {
            return Within(TimeSpan.FromSeconds(0), max, function);
        }

        /// <summary>
        /// Receive one message from the internal queue of the <see cref="TestActor"/>.
        /// </summary>
        /// <param name="duration">The amount of time to wait for a message before timing out and returning null</param>
        /// <returns>the first available message from the queue, or null in the event of a timeout</returns>
        protected object receiveOne(TimeSpan duration)
        {
            object t;

            if (duration.Milliseconds < 0) return null;

            if (queue.TryTake(out t, duration))
            {
                t.Match()
                    .With<RealMessage>(r =>
                    {
                        t = r.Msg;
                        lastMessage = r;
                    })
                    .With<NullMessage>(n =>
                    {
                        t = null;
                        lastMessage = n;
                    });
                lastWasNoMsg = false;
            }

            return t;
        }

        protected IList<T> receiveWhile<T>(TimeSpan max, Func<object, T> filter,
            int msgs = int.MaxValue)
        {
            return receiveWhile(max, TimeSpan.MaxValue, filter, msgs);
        }

        protected IList<T> receiveWhile<T>(TimeSpan max, TimeSpan idle, Func<object, T> filter, int msgs = int.MaxValue)
        {
            var stop = DateTime.UtcNow + max;

      //      Func<IList<T>, int, IEnumerable<T>> accumulatorFunc = null;
            var count = 0;
            var acc = new List<T>();

            while (count < msgs)
            {
                var obj = receiveOne(Min((stop - DateTime.UtcNow), idle));
                if (obj != null)
                {
                    var fr = filter(obj);
                    if (fr != null)
                    {
                        acc.Add(fr);
                        count++;
                    }
               //     var debug2 = true;
                    continue;
                }

           //     var debug1 = true;
                break;
            }

            lastWasNoMsg = true;
            return acc;
        }

        protected static TimeSpan Min(TimeSpan t1, TimeSpan t2)
        {
            if (t1 > t2)
                return t2;
            else
                return t1;
        }

        public class TestActor : UntypedActor
        {
            private BlockingCollection<object> queue;
            private List<object> messages;
            public TestActor(BlockingCollection<object> queue, List<object> messages)
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
                _testActor.Forward(message);
            }
        }

        //protected void Within(TimeSpan duration,Action body)
        //{
        //    var now = DateTime.Now;
        //    body();

        //}

        protected void intercept<T>(Action intercept) where T : Exception
        {
            try
            {
                intercept();
            }
            catch (AggregateException ex) //need to flatten AggregateExceptions
            {
                if (ex.Flatten().InnerExceptions.Any(x => x is T)) return;
            }
            catch (Exception x)
            {
                if (x is T)
                {
                    return;
                }
            }
            Xunit.Assert.True(false, "Expected exception of type " + typeof(T).Name);
        }

        protected void EventFilter<T>(string message, int occurances, Action intercept) where T : Exception
        {
            sys.EventStream.Subscribe(testActor, typeof(Error));
            intercept();
            for (int i = 0; i < occurances; i++)
            {
                var res = queue.Take();
                var error = (Error)res;

                Xunit.Assert.Equal(typeof(T), error.Cause.GetType());
                Xunit.Assert.Equal(message, error.Message);
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

    // ReSharper disable once InconsistentNaming
    public interface ImplicitSender
    {
        ActorRef Self { get; }
    }
}
