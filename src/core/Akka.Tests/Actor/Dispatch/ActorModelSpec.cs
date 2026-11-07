//-----------------------------------------------------------------------
// <copyright file="ActorModelSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        private readonly ITestOutputHelper _testOutputHelper;
        protected ActorModelSpec(Config hocon, ITestOutputHelper output = null) : base(hocon, output)
        {
            _testOutputHelper = output;
        }

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

        protected sealed class Meet : IActorModelMessage
        {
            public Meet(CountdownEvent acknowledge, CountdownEvent waitFor, TimeSpan maxWait)
            {
                Acknowledge = acknowledge;
                WaitFor = waitFor;
                MaxWait = maxWait;
            }

            public CountdownEvent Acknowledge { get; }

            public CountdownEvent WaitFor { get; }

            /// <summary>
            /// Hard upper bound on how long the handler parks the worker thread it is running on.
            /// An unbounded wait here is a process-wide hazard: a test that fails before it releases
            /// <see cref="WaitFor"/> leaves the actor parked on a ThreadPool worker for the rest of the
            /// test host's life, which starves every later spec in the assembly.
            /// </summary>
            public TimeSpan MaxWait { get; }
        }

        sealed class CountDownNStop : IActorModelMessage
        {
            public CountDownNStop(CountdownEvent latch)
            {
                Latch = latch;
            }

            public CountdownEvent Latch { get; }
        }

        sealed class Interrupt : IActorModelMessage
        {
            private Interrupt() { }

            public static readonly Interrupt Instance = new();
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

            public static readonly Restart Instance = new();
        }

        sealed class DoubleStop : IActorModelMessage
        {
            private DoubleStop() { }

            public static readonly DoubleStop Instance = new();
        }

        private class GetStats : IActorModelMessage
        {
            private GetStats(){}

            public static readonly GetStats Instance = new();
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
            private Switch _busy = new(false);
            private readonly ILoggingAdapter _log = Context.GetLogger();
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
                Receive<Meet>(meet => { Ack(); meet.Acknowledge.Signal(); meet.WaitFor.Wait(meet.MaxWait); _busy.SwitchOff(); });
                Receive<Reply>(reply => { Ack(); Sender.Tell(reply.Expect); _busy.SwitchOff(); });
                Receive<TryReply>(tryReply => { Ack(); Sender.Tell(tryReply.Expect, null); _busy.SwitchOff(); });
                Receive<Forward>(forward => { Ack(); forward.To.Forward(forward.Msg); _busy.SwitchOff(); });
                Receive<CountDown>(countDown => { Ack(); countDown.Latch.Signal(); _busy.SwitchOff(); });
                Receive<Increment>(increment => { Ack(); increment.Counter.IncrementAndGet(); _busy.SwitchOff(); });
                Receive<CountDownNStop>(countDown => { Ack(); countDown.Latch.Signal(); Context.Stop(Self); _busy.SwitchOff(); });
                Receive<Restart>(_ => { Ack(); _busy.SwitchOff(); throw new Exception("restart requested"); }, _ => true); // had to add predicate for compiler magic
                Receive<Interrupt>(_ => { Ack(); Sender.Tell(new Status.Failure(new ActorInterruptedException(cause: new Exception(Ping)))); _busy.SwitchOff(); throw new Exception(Ping); }, _ => true);
                Receive<InterruptNicely>(interrupt => { Ack(); Sender.Tell(interrupt.Expect); _busy.SwitchOff(); });
                Receive<ThrowException>(throwEx => { Ack(); _busy.SwitchOff(); throw throwEx.E; }, _ => true);
                Receive<DoubleStop>(_ => { Ack(); Context.Stop(Self); Context.Stop(Self); _busy.SwitchOff(); });
                Receive<GetStats>(_ => {
                    Ack();
                    Sender.Tell(_interceptor.GetStats(Self));
                    _busy.SwitchOff();
                });
            }
        }

        public class InterceptorStats
        {
            public readonly AtomicCounterLong Suspensions = new(0L);
            public readonly AtomicCounterLong Resumes = new(0L);
            public readonly AtomicCounterLong Registers = new(0L);
            public readonly AtomicCounterLong Unregisters = new(0L);
            public readonly AtomicCounterLong MsgsReceived = new(0L);
            public readonly AtomicCounterLong MsgsProcessed = new(0L);
            public readonly AtomicCounterLong Restarts = new(0L);

            public override string ToString()
            {
                return $"InterceptorStats(susp={Suspensions}, res={Resumes}, reg={Registers}, unreg={Unregisters}," +
                       $"recv={MsgsReceived}, proc={MsgsProcessed}, Restart={Restarts})";
            }
        }

        public class MessageDispatcherInterceptor : Dispatcher
        {
            public readonly ConcurrentDictionary<IActorRef, InterceptorStats> Stats = new();
            public readonly AtomicCounterLong Stops = new(0L);

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

        /// <summary>
        /// Upper bound handed to every <see cref="Meet"/> message so a failed assertion can never
        /// leave an actor parked on a ThreadPool worker for the remainder of the test host's life.
        /// </summary>
        protected TimeSpan MeetMaxWait => Dilated(TimeSpan.FromSeconds(10));

        /// <remarks>
        /// <para>
        /// The dispatcher's idle shutdown is driven off the scheduler -- one
        /// <see cref="MessageDispatcher.ShutdownTimeout"/> round per re-arm -- and the run that
        /// unregisters the last actor needs a worker from the same pool the test is running on
        /// (<c>my-test-dispatcher</c> uses <c>executor = default-executor</c>, i.e. the shared
        /// .NET ThreadPool). This polls with <c>Task.Delay</c> so the calling thread goes back to
        /// the pool between checks; the old implementation ran a <see cref="SpinWait"/> loop that
        /// pinned the calling thread AND burned a core, which on a 2-vCPU agent competes directly
        /// with the workers it is waiting for -- and pushes the pool's starvation heuristic into
        /// its slow (CPU-saturated) thread-injection rate.
        /// </para>
        /// </remarks>
        protected async Task AssertDispatcherAsync(MessageDispatcherInterceptor dispatcher, long stops)
        {
            try
            {
                await AwaitAssertAsync(
                    () => dispatcher.Stops.Current.ShouldBe(stops, $"dispatcher [{dispatcher.Id}] stop count"),
                    dispatcher.ShutdownTimeout * 5,
                    TimeSpan.FromMilliseconds(50));
            }
            catch (Exception ex)
            {
                Sys.EventStream.Publish(new Error(ex, dispatcher.ToString(), dispatcher.GetType(), $"actual: stops={dispatcher.Stops.Current}, required: stops={stops}"));
                throw;
            }
        }

        /// <remarks>
        /// Polls <see cref="CountdownEvent.IsSet"/> rather than calling
        /// <see cref="CountdownEvent.Wait(TimeSpan)"/>: the latter blocks the test's own ThreadPool
        /// worker, and because it blocks inside <see cref="ManualResetEventSlim"/> it does not trip
        /// the pool's blocking compensation either. With the pool floor at 2 on a 2-vCPU agent, a
        /// test thread parked in <c>Wait</c> plus an actor parked in a handler is the whole floor,
        /// and the mailbox run the test is waiting for cannot start until the starvation heuristic
        /// injects another thread.
        /// </remarks>
        protected async Task AssertCountdownAsync(CountdownEvent latch, TimeSpan wait, string hint)
        {
            var countedDown = await AwaitConditionNoThrowAsync(() => latch.IsSet, wait, TimeSpan.FromMilliseconds(25));
            Assert.True(countedDown, $"Failed to count down within {wait.TotalMilliseconds} milliseconds." + hint);
        }

        /// <inheritdoc cref="AssertCountdownAsync"/>
        protected async Task AssertNoCountdownAsync(CountdownEvent latch, TimeSpan wait, string hint)
        {
            var countedDown = await AwaitConditionNoThrowAsync(() => latch.IsSet, wait, TimeSpan.FromMilliseconds(25));
            Assert.False(countedDown, $"Expected count down to fail after {wait.TotalMilliseconds} milliseconds." + hint);
        }

        protected InterceptorStats StatsFor(IActorRef actorRef, MessageDispatcher dispatcher = null)
        {
            return dispatcher?.AsInstanceOf<MessageDispatcherInterceptor>().GetStats(actorRef);
        }

        protected Task AssertRefDefaultZeroAsync(IActorRef actorRef, MessageDispatcher dispatcher = null, long suspensions = 0, long resumes = 0, long registers = 0,
            long unregisters = 0, long msgsReceived = 0, long msgsProcessed = 0, long restarts = 0)
        {
            return AssertRefAsync(actorRef, suspensions, resumes,
                registers, unregisters, msgsReceived, msgsProcessed, restarts, dispatcher);
        }

        protected Task AssertRefAsync(IActorRef actorRef, MessageDispatcher dispatcher = null)
        {
            return AssertRefAsync(actorRef, StatsFor(actorRef, dispatcher).Suspensions.Current,
                StatsFor(actorRef, dispatcher).Resumes.Current,
                StatsFor(actorRef, dispatcher).Registers.Current,
                StatsFor(actorRef, dispatcher).Unregisters.Current,
                StatsFor(actorRef, dispatcher).MsgsReceived.Current,
                StatsFor(actorRef, dispatcher).MsgsProcessed.Current,
                StatsFor(actorRef, dispatcher).Restarts.Current,
                dispatcher);
        }

        /// <remarks>
        /// One polling assertion over the whole stats tuple. The old code ran seven successive
        /// spin-waits against a single shared, non-dilated 1 s budget, so whichever counter settled
        /// last only got whatever milliseconds the earlier six had left over -- and the spinning
        /// itself stole CPU from the dispatcher that had to move those counters.
        /// </remarks>
        protected async Task AssertRefAsync(IActorRef actorRef, long suspensions,
            long resumes, long registers, long unregisters, long msgsReceived,
            long msgsProcessed, long restarts, MessageDispatcher dispatcher = null)
        {
            var stats = StatsFor(actorRef, dispatcher);
            try
            {
                await AwaitAssertAsync(() =>
                {
                    stats.Suspensions.Current.ShouldBe(suspensions, "suspensions");
                    stats.Resumes.Current.ShouldBe(resumes, "resumes");
                    stats.Registers.Current.ShouldBe(registers, "registers");
                    stats.Unregisters.Current.ShouldBe(unregisters, "unregisters");
                    stats.MsgsReceived.Current.ShouldBe(msgsReceived, "msgsReceived");
                    stats.MsgsProcessed.Current.ShouldBe(msgsProcessed, "msgsProcessed");
                    stats.Restarts.Current.ShouldBe(restarts, "restarts");
                }, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(25));
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

        protected abstract MessageDispatcherInterceptor InterceptedDispatcher();
        protected abstract string DispatcherType { get; }

        protected IActorRef NewTestActor(string dispatcher)
        {
            return Sys.ActorOf(Props.Create<DispatcherActor>().WithDispatcher(dispatcher));
        }

        private Task AwaitStartedAsync(IActorRef actorRef)
        {
            return AwaitConditionAsync(() =>
            {
                if (actorRef is RepointableActorRef)
                    return actorRef.AsInstanceOf<RepointableActorRef>().IsStarted;
                return true;
            }, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10));
        }

        [Fact]
        public async Task A_dispatcher_must_dynamically_handle_its_own_lifecycle()
        {
            var dispatcher = InterceptedDispatcher();
            await AssertDispatcherAsync(dispatcher, 0);
            var a = NewTestActor(dispatcher.Id);
            await AssertDispatcherAsync(dispatcher, 0);

            // Wait for the actor to actually terminate before asserting on the dispatcher's stop
            // count. Sys.Stop is fire-and-forget: the Unregister that arms the dispatcher's idle
            // shutdown only runs when the Terminate message reaches the mailbox, which needs a
            // worker thread. Without this, "the dispatcher never shut down" and "the actor never
            // stopped" produce the same failure, and only the latter needs a ThreadPool worker.
            var aTerminated = a.WatchAsync();
            Sys.Stop(a);
            (await aTerminated.WaitAsync(RemainingOrDefault)).ShouldBeTrue("actor a should have terminated");

            await AssertDispatcherAsync(dispatcher, 1);
            await AssertRefAsync(a, suspensions: 0,
                resumes: 0,
                registers: 1,
                unregisters: 1,
                msgsProcessed: 0,
                msgsReceived: 0,
                restarts: 0,
                dispatcher: dispatcher);

            /* we don't run tasks directly on the dispatcher... */
            var a2 = NewTestActor(dispatcher.Id);
            var a2Terminated = a2.WatchAsync();
            Sys.Stop(a2);
            (await a2Terminated.WaitAsync(RemainingOrDefault)).ShouldBeTrue("actor a2 should have terminated");
            await AssertDispatcherAsync(dispatcher, 2);
        }

        [Fact]
        public async Task A_dispatcher_must_process_messages_one_at_a_time()
        {
            var dispatcher = InterceptedDispatcher();
            var start = new CountdownEvent(1);
            var oneAtTime = new CountdownEvent(1);
            var a = NewTestActor(dispatcher.Id);
            await AwaitStartedAsync(a);

            a.Tell(new CountDown(start));
            await AssertCountdownAsync(start, Dilated(TimeSpan.FromSeconds(3.0)), "Should process first message within 3 seconds");
            await AssertRefDefaultZeroAsync(a, registers: 1, msgsReceived: 1, msgsProcessed: 1, dispatcher: dispatcher);

            // Deterministically hold the actor busy with a latch the test controls,
            // instead of racing a wall-clock Thread.Sleep(1000) against a fixed deadline.
            // 'busyStarted' is signaled once the actor is inside the blocking handler;
            // 'release' keeps it busy until the test explicitly lets it go.
            var busyStarted = new CountdownEvent(1);
            var release = new CountdownEvent(1);
            try
            {
                a.Tell(new Meet(busyStarted, release, MeetMaxWait));
                await AssertCountdownAsync(busyStarted, Dilated(TimeSpan.FromSeconds(3.0)), "Should start processing the blocking message within 3 seconds");

                // While the actor is deterministically held busy, the second message must NOT be
                // processed. This positively proves the "one message at a time" property rather than
                // inferring it from a restart that never happens.
                a.Tell(new CountDown(oneAtTime));
                await AssertNoCountdownAsync(oneAtTime, Dilated(TimeSpan.FromMilliseconds(500)), "Should not process the next message while the actor is busy");
            }
            finally
            {
                // Release the gate even when an assertion above threw. A failed test must not leave
                // the actor parked in the Meet handler on a ThreadPool worker: that worker never
                // comes back, and every later spec in the assembly pays for it.
                if (!release.IsSet)
                    release.Signal();
            }

            // The queued message must now be processed once the actor is free.
            await AssertCountdownAsync(oneAtTime, Dilated(TimeSpan.FromSeconds(3.0)), "Should process message when allowed");
            await AssertRefDefaultZeroAsync(a, registers: 1, msgsReceived: 3, msgsProcessed: 3, dispatcher: dispatcher);

            Sys.Stop(a);
            await AssertRefDefaultZeroAsync(a, registers: 1, msgsReceived: 3, msgsProcessed: 3, unregisters: 1, dispatcher: dispatcher);
        }

        [Fact]
        public async Task A_dispatcher_must_handle_queuing_from_multiple_threads()
        {
            var dispatcher = InterceptedDispatcher();
            var counter = new CountdownEvent(200);
            var a = NewTestActor(dispatcher.Id);

            foreach (var i in Enumerable.Range(1, 10))
            {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                Task.Run(() =>
                {
                    foreach (var c in Enumerable.Range(1, 20))
                    {
                        a.Tell(new CountDown(counter));
                    }
                });
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            }

            try
            {
                await AssertCountdownAsync(counter, Dilated(TimeSpan.FromSeconds(3.0)),
                    "Should process 200 messages");
                await AssertRefDefaultZeroAsync(a, dispatcher, registers: 1, msgsReceived: 200, msgsProcessed: 200);
            }
            finally
            {
                var stats = await a.Ask<InterceptorStats>(GetStats.Instance);
                _testOutputHelper.WriteLine("Observed stats: {0}", stats);

                Sys.Stop(a);
            }
           
            
        }

        [Fact]
        public async Task A_dispatcher_should_not_process_messages_for_a_suspended_actor()
        {
            var dispatcher = InterceptedDispatcher();
            var a = NewTestActor(dispatcher.Id).AsInstanceOf<IInternalActorRef>();
            await AwaitStartedAsync(a);
            var done = new CountdownEvent(1);
            a.Suspend();
            a.Tell(new CountDown(done));
            await AssertNoCountdownAsync(done, Dilated(TimeSpan.FromSeconds(1.0)), "Should not process messages while suspended");
            await AssertRefDefaultZeroAsync(a, dispatcher, registers: 1, msgsReceived: 1, suspensions: 1);

            a.Resume(causedByFailure: null);
            await AssertCountdownAsync(done, Dilated(TimeSpan.FromSeconds(3.0)), "Should resume processing of messages when resumed");
            await AssertRefDefaultZeroAsync(a, dispatcher, registers: 1, msgsReceived: 1, msgsProcessed: 1, suspensions: 1, resumes: 1);

            Sys.Stop(a);
            await AssertRefDefaultZeroAsync(a, dispatcher, registers: 1, unregisters: 1, msgsReceived: 1, msgsProcessed: 1, suspensions: 1, resumes: 1);
        }

        [Fact]
        public async Task A_dispatcher_must_handle_waves_of_actors()
        {
            var dispatcher = InterceptedDispatcher();
            var props = Props.Create(() => new DispatcherActor()).WithDispatcher(dispatcher.Id);

            async Task Flood(int num)
            {
                var cachedMessage = new CountDownNStop(new CountdownEvent(num));
                var stopLatch = new CountdownEvent(num);
                var keepAliveLatch = new CountdownEvent(1);
                var waitTime = Dilated(TimeSpan.FromSeconds(20));
                Action<IActorDsl> bossActor = c =>
                {
                    c.Receive<string>(str => str.Equals("run"), (_, context) =>
                    {
                        for (var i = 1; i <= num; i++)
                        {
                            context.Watch(context.ActorOf(props)).Tell(cachedMessage);
                        }
                    });

                    c.Receive<Terminated>((_, _) =>
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
                    await AssertCountdownAsync(cachedMessage.Latch, waitTime, "Counting down from " + num);
                    await AssertCountdownAsync(stopLatch, waitTime, "Expected all children to stop.");
                }
                finally
                {
                    if (!keepAliveLatch.IsSet)
                        keepAliveLatch.Signal();
                    Sys.Stop(boss);
                }
            }

            for (var i = 1; i <= 3; i++)
            {
                await Flood(50000);
                await AssertDispatcherAsync(dispatcher, i);
            }
        }

        /* @Aaronontheweb: Left out the thread interrupt specs, because I don't think they behave the same way in .NET / Windows */

        [Fact]
        public async Task A_dispatcher_must_continue_to_process_messages_when_exception_is_thrown()
        {
            await EventFilter.Exception<IndexOutOfRangeException>().And.Exception<InvalidComObjectException>().ExpectAsync(2,
                async () =>
                {
                    var dispatcher = InterceptedDispatcher();
                    var a = NewTestActor(dispatcher.Id);
                    var f1 = a.Ask(new Reply("foo"));
                    var f2 = a.Ask(new Reply("bar"));
                    var f3 = a.Ask(new ThrowException(new IndexOutOfRangeException("IndexOutOfRangeException")));
                    var f4 = a.Ask(new Reply("foo2"));
                    var f5 = a.Ask(new ThrowException(new InvalidComObjectException("InvalidComObjectException")));
                    var f6 = a.Ask(new Reply("bar2"));

                    // Await the asks instead of Task.Wait-ing them: every blocked Wait costs the
                    // dispatcher one of the ThreadPool workers it needs to answer the next ask.
                    // RemainingOrDefault is the dilated single-expect default; the old
                    // GetTimeoutOrDefault(null) was a raw, non-dilated 3s.
                    (await f1.WaitAsync(RemainingOrDefault)).ShouldBe("foo");
                    (await f2.WaitAsync(RemainingOrDefault)).ShouldBe("bar");
                    (await f4.WaitAsync(RemainingOrDefault)).ShouldBe("foo2");
                    (await f6.WaitAsync(RemainingOrDefault)).ShouldBe("bar2");

                    // the two throwing messages are never answered
                    Assert.False(f3.IsCompleted);
                    Assert.False(f5.IsCompleted);
                });
        }

        [Fact]
        public async Task A_dispatcher_must_not_double_deregister()
        {
            var dispatcher = InterceptedDispatcher();
            for (var i = 1; i <= 1000; i++)
            {
                Sys.ActorOf(Props.Empty);
            }
            var a = NewTestActor(dispatcher.Id);
            a.Tell(DoubleStop.Instance);
            await AwaitConditionAsync(() => StatsFor(a, dispatcher).Registers.Current == 1);
            await AwaitConditionAsync(() => StatsFor(a, dispatcher).Unregisters.Current == 1);
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

        public DispatcherModelSpec(ITestOutputHelper output) : base(DispatcherHocon, output) { }

        protected override MessageDispatcherInterceptor InterceptedDispatcher()
        {
            // use new id for each test, since the MessageDispatcherInterceptor holds state
            return
                Sys.Dispatchers.Lookup("my-test-dispatcher")
                    .AsInstanceOf<MessageDispatcherInterceptor>();
        }

        protected override string DispatcherType => "Dispatcher";

        [Fact]
        public async Task A_dispatcher_must_process_messages_in_parallel()
        {
            var dispatcher = InterceptedDispatcher();
            var aStart = new CountdownEvent(1);
            var aStop = new CountdownEvent(1);
            var bParallel = new CountdownEvent(1);

            var a = NewTestActor(dispatcher.Id);
            var b = NewTestActor(dispatcher.Id);

            try
            {
                a.Tell(new Meet(aStart, aStop, MeetMaxWait));
                await AssertCountdownAsync(aStart, Dilated(TimeSpan.FromSeconds(3)), "Should process first message within 3 seconds");

                b.Tell(new CountDown(bParallel));
                await AssertCountdownAsync(bParallel, Dilated(TimeSpan.FromSeconds(3)), "Should process other actors in parallel");
            }
            finally
            {
                // Release 'a' even when an assertion above failed. Signalling only on the happy path
                // is how this test used to take the rest of the assembly down with it: a held 'a'
                // inside its handler, the ActorSystem could not terminate, and the ThreadPool worker
                // under 'a' was gone for the remainder of the test host's life.
                if (!aStop.IsSet)
                    aStop.Signal();
            }

            Sys.Stop(a);
            Sys.Stop(b);

            await Task.WhenAll(a.WatchAsync(), b.WatchAsync()).WaitAsync(RemainingOrDefault);

            await AssertRefDefaultZeroAsync(a, dispatcher, registers:1, unregisters:1, msgsReceived:1, msgsProcessed:1);
            await AssertRefDefaultZeroAsync(b, dispatcher, registers: 1, unregisters: 1, msgsReceived: 1, msgsProcessed: 1);
        }
    }
}

