using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Sdk;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;

namespace Akka.TestKit
{
    public class AkkaSpec : TestKitBase, IDisposable
    {

        public AkkaSpec()
        {

            var config = ConfigurationFactory.ParseString(GetConfig());
            queue = new BlockingCollection<MessageEnvelope>();
            messages = new List<object>();
            System = ActorSystem.Create("test", config);
            testActor = sys.ActorOf(Props.Create(() => new Internals.TestActor(queue)), "test");
            echoActor = sys.ActorOf(Props.Create(() => new EchoActor(testActor)), "echo");
        }

        protected virtual string GetConfig()
        {
            return ""+"";
        }

        public virtual void Dispose()
        {

        }

        protected BlockingCollection<MessageEnvelope> queue;
        protected List<object> messages;
        protected ActorSystem sys { get { return System; } }
        protected ActorRef testActor;
        protected ActorRef echoActor;

        private DateTime end = DateTime.MinValue;
        private bool lastWasNoMsg = false;

        public TimeSpan DefaultTimeout { get { return TestKitSettings.DefaultTimeout; } }

        private MessageEnvelope lastMessage = NullMessageEnvelope.Instance;

        protected Terminated expectTerminated(ActorRef @ref)
        {
            var actual = queue.Take();

            Xunit.Assert.True(actual.Message is Terminated);
            return (Terminated)actual.Message;
        }

        protected Terminated expectTerminated(ActorRef @ref, TimeSpan timeout)
        {
            var cancellationTokenSource = new CancellationTokenSource((int)timeout.TotalMilliseconds);
            var actual = queue.Take(cancellationTokenSource.Token);

            Xunit.Assert.True(actual.Message is Terminated);

            return (Terminated)actual.Message;
        }

        protected object expectMsg(object expected)
        {
            var actual = queue.Take();

            global::System.Diagnostics.Debug.WriteLine("actual: " + actual);
            Xunit.Assert.Equal(expected, actual.Message);
            return actual;
        }

        protected object expectMsg(object expected, TimeSpan timeout)
        {
            MessageEnvelope t;
            bool success = queue.TryTake(out t, timeout);
            Xunit.Assert.True(success, string.Format("exected message {0} but timed out after {1}", expected, timeout));
            Xunit.Assert.Equal(expected, t.Message);

            return t;
        }

        protected object expectMsg(object expected, Func<object, object, bool> comparer)
        {
            var actual = queue.Take();

            Xunit.Assert.True(comparer(expected, actual.Message));
            return actual.Message;

        }

        protected object expectMsg(object expected, Func<object, object, bool> comparer, TimeSpan timeout)
        {
            MessageEnvelope t;
            bool success = queue.TryTake(out t, timeout);
            
            Xunit.Assert.True(success, string.Format("exected message {0} but timed out after {1}", expected, timeout));
            Xunit.Assert.True(comparer(expected, t.Message));

            return t;
        }

        protected void watch(ActorRef @ref)
        {
            var l = testActor as LocalActorRef;
            l.Cell.Watch(@ref);
        }

        protected TMessage expectMsgType<TMessage>()
        {
            var envelope = queue.Take();
            var actual = envelope.Message;
            global::System.Diagnostics.Debug.WriteLine("actual: " + actual);
            Xunit.Assert.True(actual is TMessage, string.Format("expected message of type {0} but received {1} instead", typeof(TMessage), actual.GetType()));
            return (TMessage)actual;
        }

        protected TMessage expectMsgType<TMessage>(TimeSpan timeout)
        {
            MessageEnvelope actual;
            bool success = queue.TryTake(out actual, timeout);
            
            Xunit.Assert.True(success, string.Format("expected message of type {0} but timed out after {1}", typeof(TMessage), timeout));
            Xunit.Assert.True(actual.Message is TMessage, string.Format("expected message of type {0} but received {1} instead", typeof(TMessage), actual.GetType()));

            return default(TMessage);
        }

        protected T AwaitResult<T>(Task<object> ask, TimeSpan timeout)
        {
            ask.Wait(timeout);
            return (T)ask.Result;
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
            MessageEnvelope envelope;
            var success = queue.TryTake(out envelope, timeout);

            Xunit.Assert.True(success, string.Format("expected message of type {0} but timed out after {1}", typeof(TMessage), timeout));
            var message = envelope.Message;
            Xunit.Assert.True(message is TMessage, string.Format("expected message of type {0} but received {2} (type {1}) instead", typeof(TMessage), message.GetType(), message));
            var tMessage = (TMessage) message;
            Xunit.Assert.True(isMessage(tMessage), string.Format("expected {0} but got {1} instead", hint, tMessage));
            return tMessage;
        }

        protected T expectMsgPF<T>(TimeSpan duration, string hint, Func<object, T> pf)
        {
            MessageEnvelope t;
            bool success = queue.TryTake(out t, duration);

            Xunit.Assert.True(success, string.Format("timeout {0} during expectMsg: {1}", duration, hint));
            Xunit.Assert.True(t.Message != null, string.Format("expected {0} but got null message", hint));
            Xunit.Assert.True(pf.Method.GetParameters().Any(x => x.ParameterType.IsInstanceOfType(t.Message)), string.Format("expected {0} but got {1} instead", hint, t.Message));
            return pf.Invoke(t.Message);
        }

        protected T expectMsgPF<T>(string hint, Func<object, T> pf)
        {
            MessageEnvelope t = queue.Take();
            Xunit.Assert.True(pf.Method.GetParameters().Any(x => x.ParameterType.IsInstanceOfType(t.Message)), string.Format("expected {0} but got {1} instead", hint, t.Message));
            return pf.Invoke(t.Message);
        }

        protected void expectNoMsg(TimeSpan duration)
        {
            MessageEnvelope t;
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
            MessageEnvelope t;

            if (duration.Milliseconds < 0) return null;
            object msg=null;
            if (queue.TryTake(out t, duration))
            {
                t.Match()
                    .With<RealMessageEnvelope>(r =>
                    {
                        msg = r.Message;
                        lastMessage = r;
                    })
                    .With<NullMessageEnvelope>(n =>
                    {
                        msg = null;
                        lastMessage = n;
                    });
                lastWasNoMsg = false;
            }

            return msg;
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

        protected void FilterEvents(ActorSystem system, EventFilter[] eventFilters, Action action)
        {
            sys.EventStream.Publish(new Mute(eventFilters));
            try
            {
                action();

                var leeway = TestKitSettings.TestEventFilterLeeway;
                var failed = eventFilters
                    .Where(x => !x.AwaitDone(leeway))
                    .Select(x => string.Format("Timeout {0} waiting for {1}", leeway, x))
                    .ToArray();

                if (failed.Any())
                {
                    throw new AssertException("Filter completion error: " + string.Join("\n", failed));
                }
            }
            finally
            {
                sys.EventStream.Publish(new Unmute(eventFilters));
            }
        }

        protected void EventFilter<T>(string message, int occurances, Action intercept) where T : Exception
        {
            sys.EventStream.Subscribe(testActor, typeof(Error));
            intercept();
            for (int i = 0; i < occurances; i++)
            {
                var res = queue.Take();
                var error = (Error)res.Message;

                Xunit.Assert.Equal(typeof(T), error.Cause.GetType());
                Xunit.Assert.Equal(message, error.Message);
            }
        }

        protected void EventFilterLog<T>(string message, int occurences, Action intercept) where T : LogEvent
        {
            sys.EventStream.Subscribe(testActor, typeof(T));
            intercept();
            for (int i = 0; i < occurences; i++)
            {
                var res = queue.Take();
                var error = (LogEvent)res.Message;

                Xunit.Assert.Equal(typeof(T), error.GetType());
                var match = -1 != error.Message.ToString().IndexOf(message, StringComparison.CurrentCultureIgnoreCase);
                Xunit.Assert.True(match);
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
}
