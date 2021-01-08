//-----------------------------------------------------------------------
// <copyright file="ActorModelSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;
using Akka.TestKit;
using Akka.Util;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Tests.Actor.Dispatch
{
    public abstract class ActorModelSpec : AkkaSpec
    {
        protected ActorModelSpec(Config hocon) : base(hocon) { }

        interface IActorModelMessage : INoSerializationVerificationNeeded { }

        sealed class TryReply : IActorModelMessage
        {
            public TryReply(object expect)
            {
                Expect = expect;
            }

            public object Expect { get; }
        }

        sealed class Reply : IActorModelMessage
        {
            public Reply(object expect)
            {
                Expect = expect;
            }

            public object Expect { get; }
        }

        sealed class Forward : IActorModelMessage
        {
            public Forward(IActorRef to, object msg)
            {
                To = to;
                Msg = msg;
            }

            public IActorRef To { get; }
            public object Msg { get; }
        }

        protected sealed class CountDown : IActorModelMessage
        {
            public CountDown(CountdownEvent latch)
            {
                Latch = latch;
            }

            public CountdownEvent Latch { get; }
        }

        sealed class Increment : IActorModelMessage
        {
            public Increment(AtomicCounterLong counter)
            {
                Counter = counter;
            }

            public AtomicCounterLong Counter { get; }
        }

        sealed class AwaitLatch : IActorModelMessage
        {
            public AwaitLatch(CountdownEvent latch)
            {
                Latch = latch;
            }

            public CountdownEvent Latch { get; }
        }

        protected sealed class Meet : IActorModelMessage
        {
            public Meet(CountdownEvent acknowledge, CountdownEvent waitFor)
            {
                Acknowledge = acknowledge;
                WaitFor = waitFor;
            }

            public CountdownEvent Acknowledge { get; }

            public CountdownEvent WaitFor { get; }
        }

        sealed class CountDownNStop : IActorModelMessage
        {
            public CountDownNStop(CountdownEvent latch)
            {
                Latch = latch;
            }

            public CountdownEvent Latch { get; }
        }

        sealed class Wait : IActorModelMessage
        {
            public Wait(int time)
            {
                Time = time;
            }

            public long Time { get; }
        }

        sealed class WaitAck : IActorModelMessage
        {
            public WaitAck(long time, CountdownEvent latch)
            {
                Time = time;
                Latch = latch;
            }

            public long Time { get; }
            public CountdownEvent Latch { get; }
        }

        sealed class Interrupt : IActorModelMessage
        {
            private Interrupt() { }

            public static readonly Interrupt Instance = new Interrupt();
        }

        sealed class InterruptNicely : IActorModelMessage
        {
            public InterruptNicely(object expect)
            {
                Expect = expect;
            }

            public object Expect { get; }
        }

        sealed class Restart : IActorModelMessage
        {
            private Restart() { }

            public static readonly Restart Instance = new Restart();
        }

        sealed class DoubleStop : IActorModelMessage
        {
            private DoubleStop() { }

            public static readonly DoubleStop Instance = new DoubleStop();
        }

        sealed class ThrowException : IActorModelMessage
        {
            public ThrowException(Exception e)
            {
                E = e;
            }

            public Exception E { get; }
        }

        const string Ping = "Ping";
        const string Pong = "Pong";

        class DispatcherActor : ReceiveActor
        {
            private Switch _busy = new Switch(false);

            private MessageDispatcherInterceptor _interceptor = Context.Dispatcher.AsInstanceOf<MessageDispatcherInterceptor>();

            private void Ack()
            {
                if (!_busy.SwitchOn())
                {
                    throw new InvalidOperationException("isolation violated!");
                }
                else
                {
                    _interceptor.GetStats(Self).MsgsProcessed.IncrementAndGet();
                }
            }

            protected override void PostRestart(Exception reason)
            {
                _interceptor.GetStats(Self).Restarts.IncrementAndGet();

            }

            public DispatcherActor()
            {
                Receive<AwaitLatch>(latch => { Ack(); latch.Latch.Wait(); _busy.SwitchOff(); });
                Receive<Meet>(meet => { Ack(); meet.Acknowledge.Signal(); meet.WaitFor.Wait(); _busy.SwitchOff(); });
                Receive<Wait>(wait => { Ack(); Thread.Sleep((int)wait.Time); _busy.SwitchOff(); });
                Receive<WaitAck>(waitAck => { Ack(); Thread.Sleep((int)waitAck.Time); waitAck.Latch.Signal(); _busy.SwitchOff(); });
                Receive<Reply>(reply => { Ack(); Sender.Tell(reply.Expect); _busy.SwitchOff(); });
                Receive<TryReply>(tryReply => { Ack(); Sender.Tell(tryReply.Expect, null); _busy.SwitchOff(); });
                Receive<Forward>(forward => { Ack(); forward.To.Forward(forward.Msg); _busy.SwitchOff(); });
                Receive<CountDown>(countDown => { Ack(); countDown.Latch.Signal(); _busy.SwitchOff(); });
                Receive<Increment>(increment => { Ack(); increment.Counter.IncrementAndGet(); _busy.SwitchOff(); });
                Receive<CountDownNStop>(countDown => { Ack(); countDown.Latch.Signal(); Context.Stop(Self); _busy.SwitchOff(); });
                Receive<Restart>(restart => { Ack(); _busy.SwitchOff(); throw new Exception("restart requested"); }, restart => true); // had to add predicate for compiler magic
                Receive<Interrupt>(interrupt => { Ack(); Sender.Tell(new Status.Failure(new ActorInterruptedException(cause: new Exception(Ping)))); _busy.SwitchOff(); throw new Exception(Ping); }, interrupt => true);
                Receive<InterruptNicely>(interrupt => { Ack(); Sender.Tell(interrupt.Expect); _busy.SwitchOff(); });
                Receive<ThrowException>(throwEx => { Ack(); _busy.SwitchOff(); throw throwEx.E; }, throwEx => true);
                Receive<DoubleStop>(doubleStop => { Ack(); Context.Stop(Self); Context.Stop(Self); _busy.SwitchOff(); });
            }
        }

        public class InterceptorStats
        {
            public readonly AtomicCounterLong Suspensions = new AtomicCounterLong(0L);
            public readonly AtomicCounterLong Resumes = new AtomicCounterLong(0L);
            public readonly AtomicCounterLong Registers = new AtomicCounterLong(0L);
            public readonly AtomicCounterLong Unregisters = new AtomicCounterLong(0L);
            public readonly AtomicCounterLong MsgsReceived = new AtomicCounterLong(0L);
            public readonly AtomicCounterLong MsgsProcessed = new AtomicCounterLong(0L);
            public readonly AtomicCounterLong Restarts = new AtomicCounterLong(0L);

            public override string ToString()
            {
                return $"InterceptorStats(susp={Suspensions}, res={Resumes}, reg={Registers}, unreg={Unregisters}," +
                       $"recv={MsgsReceived}, proc={MsgsProcessed}, Restart={Restarts})";
            }
        }

        public class MessageDispatcherInterceptor : Dispatcher
        {
            public readonly ConcurrentDictionary<IActorRef, InterceptorStats> Stats = new ConcurrentDictionary<IActorRef, InterceptorStats>();
            public readonly AtomicCounterLong Stops = new AtomicCounterLong(0L);

            public MessageDispatcherInterceptor(MessageDispatcherConfigurator configurator, string id, int throughput, long? throughputDeadlineTime, ExecutorServiceFactory executorServiceFactory, TimeSpan shutdownTimeout) : base(configurator, id, throughput, throughputDeadlineTime, executorServiceFactory, shutdownTimeout)
            {
            }

            public InterceptorStats GetStats(IActorRef actorRef)
            {
                var iS = new InterceptorStats();
                return Stats.GetOrAdd(actorRef, iS);
            }

            internal override void Register(ActorCell actor)
            {
                GetStats(actor.Self).Registers.IncrementAndGet();
                base.Register(actor);
            }

            internal override void Unregister(ActorCell actor)
            {
                GetStats(actor.Self).Unregisters.IncrementAndGet();
                base.Unregister(actor);
            }

            internal override void Resume(ActorCell actorCell)
            {
                GetStats(actorCell.Self).Resumes.IncrementAndGet();
                base.Resume(actorCell);
            }

            public override void Dispatch(ActorCell cell, Envelope envelope)
            {
                GetStats(cell.Self).MsgsReceived.IncrementAndGet();
                base.Dispatch(cell, envelope);
            }

            internal override void Suspend(ActorCell actorCell)
            {
                GetStats(actorCell.Self).Suspensions.IncrementAndGet();
                base.Suspend(actorCell);
            }

            protected override void Shutdown()
            {
                Stops.IncrementAndGet();
                base.Shutdown();
            }
        }

        protected class MessageDispatcherInterceptorConfigurator : MessageDispatcherConfigurator
        {
            private readonly MessageDispatcherInterceptor _instance;

            public MessageDispatcherInterceptorConfigurator(Config config, IDispatcherPrerequisites prerequisites) : base(config, prerequisites)
            {
                if (config.IsNullOrEmpty())
                    throw ConfigurationException.NullOrEmptyConfig<MessageDispatcherInterceptorConfigurator>();

                _instance = new MessageDispatcherInterceptor(this,
                    config.GetString("id", null),
                    config.GetInt("throughput", 0),
                    config.GetTimeSpan("throughput-deadline-time", null).Ticks,
                    ConfigureExecutor(),
                    Config.GetTimeSpan("shutdown-timeout", null));
            }

            public override MessageDispatcher Dispatcher()
            {
                return _instance;
            }
        }

        protected void AssertDispatcher(MessageDispatcherInterceptor dispatcher, long stops)
        {
            var deadline = MonotonicClock.GetMilliseconds() + (long)(dispatcher.ShutdownTimeout.TotalMilliseconds * 5);
            try
            {
                Await(deadline, () => stops == dispatcher.Stops.Current);
            }
            catch (Exception ex)
            {
                Sys.EventStream.Publish(new Error(ex, dispatcher.ToString(), dispatcher.GetType(), $"actual: stops={dispatcher.Stops.Current}, required: stops={stops}"));
                throw;
            }
        }

        protected void AssertCountdown(CountdownEvent latch, int wait, string hint)
        {
            Assert.True(latch.Wait(wait), $"Failed to count down within {wait} milliseconds." + hint);
        }

        protected void AssertNoCountdown(CountdownEvent latch, int wait, string hint)
        {
            Assert.False(latch.Wait(wait), $"Expected count down to fail after {wait} milliseconds." + hint);
        }

        protected InterceptorStats StatsFor(IActorRef actorRef, MessageDispatcher dispatcher = null)
        {
            return dispatcher?.AsInstanceOf<MessageDispatcherInterceptor>().GetStats(actorRef);
        }

        protected void AssertRefDefaultZero(IActorRef actorRef, MessageDispatcher dispatcher = null, long suspensions = 0, long resumes = 0, long registers = 0,
            long unregisters = 0, long msgsReceived = 0, long msgsProcessed = 0, long restarts = 0)
        {
            AssertRef(actorRef, suspensions, resumes,
                registers, unregisters, msgsReceived, msgsProcessed, restarts, dispatcher);
        }

        protected void AssertRef(IActorRef actorRef, MessageDispatcher dispatcher = null)
        {
            AssertRef(actorRef, StatsFor(actorRef, dispatcher).Suspensions.Current,
                StatsFor(actorRef, dispatcher).Resumes.Current,
                StatsFor(actorRef, dispatcher).Registers.Current,
                StatsFor(actorRef, dispatcher).Unregisters.Current,
                StatsFor(actorRef, dispatcher).MsgsReceived.Current,
                StatsFor(actorRef, dispatcher).MsgsProcessed.Current,
                StatsFor(actorRef, dispatcher).Restarts.Current,
                dispatcher);

        }


        protected void AssertRef(IActorRef actorRef, long suspensions,
            long resumes, long registers, long unregisters, long msgsReceived,
            long msgsProcessed, long restarts, MessageDispatcher dispatcher = null)
        {
            var deadline = MonotonicClock.GetMilliseconds() + 1000;
            var stats = StatsFor(actorRef, dispatcher);
            try
            {
                Await(deadline, () => stats.Suspensions.Current == suspensions);
                Await(deadline, () => stats.Resumes.Current == resumes);
                Await(deadline, () => stats.Registers.Current == registers);
                Await(deadline, () => stats.Unregisters.Current == unregisters);
                Await(deadline, () => stats.MsgsReceived.Current == msgsReceived);
                Await(deadline, () => stats.MsgsProcessed.Current == msgsProcessed);
                Await(deadline, () => stats.Restarts.Current == restarts);
            }
            catch (Exception ex)
            {
                Sys.EventStream.Publish(new Error(ex, dispatcher?.ToString(),
                    dispatcher?.GetType() ?? this.GetType(),
                    $"actual: {stats}, required: InterceptorStats(susp={suspensions}," +
                    $"res={resumes}, reg={registers}, unreg={unregisters}, recv={msgsReceived}, " +
                    $"proc={msgsProcessed}, restart={restarts})"));
                throw;
            }
        }

        private static void Await(long until, Func<bool> condition)
        {
            var spinWait = new SpinWait();
            var done = false;
            while (MonotonicClock.GetMilliseconds() <= until && !done)
            {
                done = condition();
                if (!done)
                    spinWait.SpinOnce();
            }

            if (!done)
                throw new Exception("Await failed");
        }

        protected abstract MessageDispatcherInterceptor InterceptedDispatcher();
        protected abstract string DispatcherType { get; }

        protected IActorRef NewTestActor(string dispatcher)
        {
            return Sys.ActorOf(Props.Create<DispatcherActor>().WithDispatcher(dispatcher));
        }

        void AwaitStarted(IActorRef actorRef)
        {
            AwaitCondition(() =>
            {
                if (actorRef is RepointableActorRef)
                    return actorRef.AsInstanceOf<RepointableActorRef>().IsStarted;
                return true;
            }, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10));
        }

        [Fact]
        public void A_dispatcher_must_dynamically_handle_its_own_lifecycle()
        {
            var dispatcher = InterceptedDispatcher();
            AssertDispatcher(dispatcher, 0);
            var a = NewTestActor(dispatcher.Id);
            AssertDispatcher(dispatcher, 0);
            Sys.Stop(a);
            AssertDispatcher(dispatcher, 1);
            AssertRef(a, suspensions: 0,
                resumes: 0,
                registers: 1,
                unregisters: 1,
                msgsProcessed: 0,
                msgsReceived: 0,
                restarts: 0,
                dispatcher: dispatcher);

            /* we don't run tasks directly on the dispatcher... */
            var a2 = NewTestActor(dispatcher.Id);
            Sys.Stop(a2);
            AssertDispatcher(dispatcher, 2);
        }

        [Fact]
        public void A_dispatcher_must_process_messages_one_at_a_time()
        {
            var dispatcher = InterceptedDispatcher();
            var start = new CountdownEvent(1);
            var oneAtTime = new CountdownEvent(1);
            var a = NewTestActor(dispatcher.Id);
            AwaitStarted(a);

            a.Tell(new CountDown(start));
            AssertCountdown(start, (int)Dilated(TimeSpan.FromSeconds(3.0)).TotalMilliseconds, "Should process first message within 3 seconds");
            AssertRefDefaultZero(a, registers: 1, msgsReceived: 1, msgsProcessed: 1, dispatcher: dispatcher);

            a.Tell(new Wait(1000));
            a.Tell(new CountDown(oneAtTime));
            // in case of serialization violation, restart would happen instead of countdown
            AssertCountdown(oneAtTime, (int)Dilated(TimeSpan.FromSeconds(1.5)).TotalMilliseconds, "Should process message when allowed");
            AssertRefDefaultZero(a, registers: 1, msgsReceived: 3, msgsProcessed: 3, dispatcher: dispatcher);

            Sys.Stop(a);
            AssertRefDefaultZero(a, registers: 1, msgsReceived: 3, msgsProcessed: 3, unregisters: 1, dispatcher: dispatcher);
        }

        [Fact(Skip = "Racy on Azure DevOps")]
        public void A_dispatcher_must_handle_queuing_from_multiple_threads()
        {
            var dispatcher = InterceptedDispatcher();
            var counter = new CountdownEvent(200);
            var a = NewTestActor(dispatcher.Id);

            foreach (var i in Enumerable.Range(1, 10))
            {
                Task.Run(() =>
                {
                    foreach (var c in Enumerable.Range(1, 20))
                    {
                        a.Tell(new WaitAck(1, counter));
                    }
                });
            }

            AssertCountdown(counter, (int)Dilated(TimeSpan.FromSeconds(3.0)).TotalMilliseconds, "Should process 200 messages");
            AssertRefDefaultZero(a, dispatcher, registers: 1, msgsReceived: 200, msgsProcessed: 200);
            Sys.Stop(a);
        }

        [Fact]
        public void A_dispatcher_should_not_process_messages_for_a_suspended_actor()
        {
            var dispatcher = InterceptedDispatcher();
            var a = NewTestActor(dispatcher.Id).AsInstanceOf<IInternalActorRef>();
            AwaitStarted(a);
            var done = new CountdownEvent(1);
            a.Suspend();
            a.Tell(new CountDown(done));
            AssertNoCountdown(done, 1000, "Should not process messages while suspended");
            AssertRefDefaultZero(a, dispatcher, registers: 1, msgsReceived: 1, suspensions: 1);

            a.Resume(causedByFailure: null);
            AssertCountdown(done, (int)Dilated(TimeSpan.FromSeconds(3.0)).TotalMilliseconds, "Should resume processing of messages when resumed");
            AssertRefDefaultZero(a, dispatcher, registers: 1, msgsReceived: 1, msgsProcessed: 1, suspensions: 1, resumes: 1);

            Sys.Stop(a);
            AssertRefDefaultZero(a, dispatcher, registers: 1, unregisters: 1, msgsReceived: 1, msgsProcessed: 1, suspensions: 1, resumes: 1);
        }

        [Fact]
        public void A_dispatcher_must_handle_waves_of_actors()
        {
            var dispatcher = InterceptedDispatcher();
            var props = Props.Create(() => new DispatcherActor()).WithDispatcher(dispatcher.Id);

            Action<int> flood = num =>
            {
                var cachedMessage = new CountDownNStop(new CountdownEvent(num));
                var stopLatch = new CountdownEvent(num);
                var keepAliveLatch = new CountdownEvent(1);
                var waitTime = (int)Dilated(TimeSpan.FromSeconds(20)).TotalMilliseconds;
                Action<IActorDsl> bossActor = c =>
                {
                    c.Receive<string>(str => str.Equals("run"), (s, context) =>
                    {
                        for (var i = 1; i <= num; i++)
                        {
                            context.Watch(context.ActorOf(props)).Tell(cachedMessage);
                        }
                    });

                    c.Receive<Terminated>((terminated, context) =>
                    {
                        stopLatch.Signal();
                    });
                };
                var boss = Sys.ActorOf(Props.Create(() => new Act(bossActor)).WithDispatcher("boss"));

                try
                {
                    // this future is meant to keep the dispatcher alive until the end of the test run even if
                    // the boss doesn't create children fast enough to keep the dispatcher from becoming empty
                    // and it needs to be on a separate thread to not deadlock the calling thread dispatcher
                    dispatcher.Schedule(() =>
                    {
                        keepAliveLatch.Wait(waitTime);
                    });
                    boss.Tell("run");
                    try
                    {
                        AssertCountdown(cachedMessage.Latch, waitTime, "Counting down from " + num);
                    }
                    catch (Exception ex)
                    {
                        // TODO balancing dispatcher
                        throw;
                    }
                    AssertCountdown(stopLatch, waitTime, "Expected all children to stop.");
                }
                finally
                {
                    keepAliveLatch.Signal();
                    Sys.Stop(boss);
                }
            };

            for (var i = 1; i <= 3; i++)
            {
                flood(50000);
                AssertDispatcher(dispatcher, i);
            }
        }

        /* @Aaronontheweb: Left out the thread interrupt specs, because I don't think they behave the same way in .NET / Windows */

        [Fact]
        public void A_dispatcher_must_continue_to_process_messages_when_exception_is_thrown()
        {
            EventFilter.Exception<IndexOutOfRangeException>().And.Exception<InvalidComObjectException>().Expect(2,
                () =>
                {
                    var dispatcher = InterceptedDispatcher();
                    var a = NewTestActor(dispatcher.Id);
                    var f1 = a.Ask(new Reply("foo"));
                    var f2 = a.Ask(new Reply("bar"));
                    var f3 = a.Ask(new ThrowException(new IndexOutOfRangeException("IndexOutOfRangeException")));
                    var f4 = a.Ask(new Reply("foo2"));
                    var f5 = a.Ask(new ThrowException(new InvalidComObjectException("InvalidComObjectException")));
                    var f6 = a.Ask(new Reply("bar2"));

                    Assert.True(f1.Wait(GetTimeoutOrDefault(null)));
                    f1.Result.ShouldBe("foo");
                    Assert.True(f2.Wait(GetTimeoutOrDefault(null)));
                    f2.Result.ShouldBe("bar");
                    Assert.True(f4.Wait(GetTimeoutOrDefault(null)));
                    f4.Result.ShouldBe("foo2");
                    Assert.True(f6.Wait(GetTimeoutOrDefault(null)));
                    f6.Result.ShouldBe("bar2");
                    Assert.False(f3.IsCompleted);
                    Assert.False(f5.IsCompleted);
                });
        }

        [Fact]
        public void A_dispatcher_must_not_double_deregister()
        {
            var dispatcher = InterceptedDispatcher();
            for (var i = 1; i <= 1000; i++)
            {
                Sys.ActorOf(Props.Empty);
            }
            var a = NewTestActor(dispatcher.Id);
            a.Tell(DoubleStop.Instance);
            AwaitCondition(() => StatsFor(a, dispatcher).Registers.Current == 1);
            AwaitCondition(() => StatsFor(a, dispatcher).Unregisters.Current == 1);
        }
    }

    /// <summary>
    /// Tests the default dispatcher
    /// </summary>
    public class DispatcherModelSpec : ActorModelSpec
    {
        private static readonly Config DispatcherHocon = @"my-test-dispatcher{
            type=""" + typeof(MessageDispatcherInterceptorConfigurator).AssemblyQualifiedName + @"""
            executor = default-executor
        }
        
        boss {
            executor = fork-join-executor
            type = PinnedDispatcher
        }

        ";

        public DispatcherModelSpec() : base(DispatcherHocon) { }

        protected override MessageDispatcherInterceptor InterceptedDispatcher()
        {
            // use new id for each test, since the MessageDispatcherInterceptor holds state
            return
                Sys.Dispatchers.Lookup("my-test-dispatcher")
                    .AsInstanceOf<MessageDispatcherInterceptor>();
        }

        protected override string DispatcherType => "Dispatcher";

        [Fact]
        public void A_dispatcher_must_process_messages_in_parallel()
        {
            var dispatcher = InterceptedDispatcher();
            var aStart = new CountdownEvent(1);
            var aStop = new CountdownEvent(1);
            var bParallel = new CountdownEvent(1);

            var a = NewTestActor(dispatcher.Id);
            var b = NewTestActor(dispatcher.Id);

            a.Tell(new Meet(aStart, aStop));
            AssertCountdown(aStart, (int)Dilated(TimeSpan.FromSeconds(3)).TotalMilliseconds, "Should process first message within 3 seconds");

            b.Tell(new CountDown(bParallel));
            AssertCountdown(bParallel, (int)Dilated(TimeSpan.FromSeconds(3)).TotalMilliseconds, "Should process other actors in parallel");

            aStop.Signal();
            Sys.Stop(a);
            Sys.Stop(b);

            SpinWait.SpinUntil(() => a.AsInstanceOf<IInternalActorRef>().IsTerminated && b.AsInstanceOf<IInternalActorRef>().IsTerminated);

            AssertRefDefaultZero(a, dispatcher, registers:1, unregisters:1, msgsReceived:1, msgsProcessed:1);
            AssertRefDefaultZero(b, dispatcher, registers: 1, unregisters: 1, msgsReceived: 1, msgsProcessed: 1);
        }
    }

    // TODO: add support for balancing dispatcher
}

