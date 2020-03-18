//-----------------------------------------------------------------------
// <copyright file="FSMTimingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Event;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using static Akka.Actor.FSMBase;

namespace Akka.Tests.Actor
{
    public class TimerSpec : AbstractTimerSpec
    {
        protected override Props TargetProps(IActorRef monitor, TimeSpan interval, bool repeat, Func<int> initial) =>
            Props.Create(() => new Target(monitor, interval, repeat, initial ?? (() => 1)));
    }

    public class FsmTimerSpec : AbstractTimerSpec
    {
        protected override Props TargetProps(IActorRef monitor, TimeSpan interval, bool repeat, Func<int> initial) =>
            Props.Create(() => new FsmTarget(monitor, interval, repeat, initial ?? (() => 1)));
    }

    public abstract class AbstractTimerSpec : AkkaSpec
    {
        int interval = 1;
        TimeSpan dilatedInterval;

        protected abstract Props TargetProps(IActorRef monitor, TimeSpan interval, bool repeat, Func<int> initial = null);

        public AbstractTimerSpec()
        {
            dilatedInterval = Dilated(TimeSpan.FromSeconds(interval));
        }

        [Fact]
        public void Must_schedule_non_repeated_ticks()
        {
            var probe = CreateTestProbe();
            var actor = this.Sys.ActorOf(TargetProps(probe.Ref, TimeSpan.FromMilliseconds(10), false));

            probe.ExpectMsg(new Tock(1));
            probe.ExpectNoMsg(100);

            actor.Tell(End.Instance);
            probe.ExpectMsg(new GotPostStop(false));
        }

        [Fact]
        public void Must_schedule_repeated_ticks()
        {
            var probe = CreateTestProbe();
            var actor = this.Sys.ActorOf(TargetProps(probe.Ref, dilatedInterval, true));

            probe.Within(TimeSpan.FromSeconds(interval * 4) - TimeSpan.FromMilliseconds(100), () =>
            {
                probe.ExpectMsg(new Tock(1));
                probe.ExpectMsg(new Tock(1));
                probe.ExpectMsg(new Tock(1));
            });

            actor.Tell(End.Instance);
            probe.ExpectMsg(new GotPostStop(false));
        }

        [Fact]
        public void Must_replace_timer()
        {
            var probe = CreateTestProbe();
            var actor = this.Sys.ActorOf(TargetProps(probe.Ref, dilatedInterval, true));

            probe.ExpectMsg(new Tock(1));

            var latch = this.CreateTestLatch(1);
            // next Tock(1) enqueued in mailboxed, but should be discarded because of new timer
            actor.Tell(new SlowThenBump(latch));
            probe.ExpectNoMsg(TimeSpan.FromSeconds(interval) + TimeSpan.FromMilliseconds(100));
            latch.CountDown();
            probe.ExpectMsg(new Tock(2));

            actor.Tell(End.Instance);
            probe.ExpectMsg(new GotPostStop(false));
        }

        [Fact]
        public void Must_cancel_timer()
        {
            var probe = CreateTestProbe();
            var actor = this.Sys.ActorOf(TargetProps(probe.Ref, dilatedInterval, true));

            probe.ExpectMsg(new Tock(1));

            actor.Tell(Cancel.Instance);
            probe.ExpectNoMsg(dilatedInterval + TimeSpan.FromMilliseconds(100));

            actor.Tell(End.Instance);
            probe.ExpectMsg(new GotPostStop(false));
        }

        [Fact]
        public void Must_cancel_timers_when_restarted()
        {
            var probe = CreateTestProbe();
            var actor = this.Sys.ActorOf(TargetProps(probe.Ref, dilatedInterval, true));

            actor.Tell(new Throw(new Exc()));
            probe.ExpectMsg(new GotPreRestart(false));

            actor.Tell(End.Instance);
            probe.ExpectMsg(new GotPostStop(false));
        }

        [Fact]
        public void Must_discard_timers_from_old_incarnation_after_restart_alt_1()
        {
            var probe = CreateTestProbe();
            var startCounter = new AtomicCounter(0);
            var actor = this.Sys.ActorOf(TargetProps(probe.Ref, dilatedInterval, true, () => startCounter.IncrementAndGet()));

            probe.ExpectMsg(new Tock(1));

            var latch = this.CreateTestLatch(1);
            // next Tock(1) is enqueued in mailbox, but should be discarded by new incarnation
            actor.Tell(new SlowThenThrow(latch, new Exc()));
            probe.ExpectNoMsg(TimeSpan.FromSeconds(interval) + TimeSpan.FromMilliseconds(100));
            latch.CountDown();
            probe.ExpectMsg(new GotPreRestart(false));
            probe.ExpectNoMsg(TimeSpan.FromSeconds(interval / 2));
            probe.ExpectMsg(new Tock(2)); // this is from the startCounter increment

            actor.Tell(End.Instance);
            probe.ExpectMsg(new GotPostStop(false));
        }

        [Fact]
        public void Must_discard_timers_from_old_incarnation_after_restart_alt_2()
        {
            var probe = CreateTestProbe();
            var actor = this.Sys.ActorOf(TargetProps(probe.Ref, dilatedInterval, true));

            probe.ExpectMsg(new Tock(1));
            // change state so that we see that the restart starts over again
            actor.Tell(Bump.Instance);

            probe.ExpectMsg(new Tock(2));

            var latch = this.CreateTestLatch(1);
            // next Tock(2) is enqueued in mailbox, but should be discarded by new incarnation
            actor.Tell(new SlowThenThrow(latch, new Exc()));
            probe.ExpectNoMsg(TimeSpan.FromSeconds(interval) + TimeSpan.FromMilliseconds(100));
            latch.CountDown();
            probe.ExpectMsg(new GotPreRestart(false));
            probe.ExpectMsg(new Tock(1));

            actor.Tell(End.Instance);
            probe.ExpectMsg(new GotPostStop(false));
        }

        [Fact]
        public void Must_cancel_timers_when_stopped()
        {
            var probe = CreateTestProbe();
            var actor = this.Sys.ActorOf(TargetProps(probe.Ref, dilatedInterval, true));

            actor.Tell(End.Instance);
            probe.ExpectMsg(new GotPostStop(false));
        }

        [Fact]
        public void Must_handle_AutoReceivedMessages_automatically()
        {
            var probe = CreateTestProbe();
            var actor = this.Sys.ActorOf(TargetProps(probe.Ref, TimeSpan.FromMilliseconds(10), false));

            this.Watch(actor);
            actor.Tell(AutoReceive.Instance);

            ExpectTerminated(actor);
        }


        #region Actors

        internal interface ICommand
        {
        }

        internal class Tick : ICommand
        {
            public int N { get; }

            public Tick(int n)
            {
                N = n;
            }
        }

        internal class Bump : ICommand
        {
            public static readonly Bump Instance = new Bump();

            private Bump()
            {
            }
        }

        internal class SlowThenBump : ICommand, INoSerializationVerificationNeeded
        {
            public TestLatch Latch { get; }

            public SlowThenBump(TestLatch latch)
            {
                Latch = latch;
            }
        }

        internal class End : ICommand
        {
            public static readonly End Instance = new End();

            private End()
            {
            }
        }

        internal class Throw : ICommand
        {
            public Exception E { get; }

            public Throw(Exception e)
            {
                E = e;
            }
        }

        internal class Cancel : ICommand
        {
            public static readonly Cancel Instance = new Cancel();

            private Cancel()
            {
            }
        }

        internal class SlowThenThrow : ICommand, INoSerializationVerificationNeeded
        {
            public TestLatch Latch { get; }
            public Exception E { get; }

            public SlowThenThrow(TestLatch latch, Exception e)
            {
                Latch = latch;
                E = e;
            }
        }

        internal class AutoReceive : ICommand
        {
            public static readonly AutoReceive Instance = new AutoReceive();

            private AutoReceive()
            {
            }
        }

        internal interface IEvent
        {
        }

        internal class Tock : IEvent
        {
            public int N { get; }

            public Tock(int n)
            {
                N = n;
            }

            public override int GetHashCode()
            {
                return N.GetHashCode();
            }

            public override bool Equals(object obj)
            {
                if (obj is Tock other)
                {
                    return N.Equals(other.N);
                }
                return false;
            }
        }

        internal class GotPostStop : IEvent
        {
            public bool TimerActive { get; }

            public GotPostStop(bool timerActive)
            {
                TimerActive = timerActive;
            }

            public override int GetHashCode()
            {
                return TimerActive.GetHashCode();
            }

            public override bool Equals(object obj)
            {
                if (obj is GotPostStop other)
                {
                    return TimerActive.Equals(other.TimerActive);
                }
                return false;
            }
        }

        internal class GotPreRestart : IEvent
        {
            public bool TimerActive { get; }

            public GotPreRestart(bool timerActive)
            {
                TimerActive = timerActive;
            }

            public override int GetHashCode()
            {
                return TimerActive.GetHashCode();
            }

            public override bool Equals(object obj)
            {
                if (obj is GotPreRestart other)
                {
                    return TimerActive.Equals(other.TimerActive);
                }
                return false;
            }
        }

        internal class Exc : Exception
        {
            public Exc()
                : base("simulated exc")
            {
            }
        }

        internal class Target : ActorBase, IWithTimers
        {
            private IActorRef monitor;
            private TimeSpan interval;
            int bumpCount;

            public ITimerScheduler Timers { get ; set ; }

            public Target(IActorRef monitor, TimeSpan interval, bool repeat, Func<int> initial)
            {
                this.monitor = monitor;
                this.interval = interval;
                bumpCount = initial();

                if (repeat)
                    Timers.StartPeriodicTimer("T", new Tick(bumpCount), interval);
                else
                    Timers.StartSingleTimer("T", new Tick(bumpCount), interval);
            }

            protected override void PreRestart(Exception reason, object message)
            {
                monitor.Tell(new GotPreRestart(Timers.IsTimerActive("T")));
                // don't call super.preRestart to avoid postStop
            }

            protected override void PostStop()
            {
                monitor.Tell(new GotPostStop(Timers.IsTimerActive("T")));
            }

            void Bump()
            {
                bumpCount += 1;
                Timers.StartPeriodicTimer("T", new Tick(bumpCount), interval);
            }

            void AutoReceive()
            {
                Timers.StartSingleTimer("A", PoisonPill.Instance, interval);
            }

            protected override bool Receive(object message)
            {
                switch (message)
                {
                    case Tick m:
                        monitor.Tell(new Tock(m.N));
                        return true;

                    case Bump _:
                        Bump();
                        return true;

                    case SlowThenBump m:
                        m.Latch.Ready(TimeSpan.FromSeconds(10));
                        Bump();
                        return true;

                    case End _:
                        Context.Stop(Self);
                        return true;

                    case Cancel _:
                        Timers.Cancel("T");
                        return true;

                    case Throw m:
                        throw m.E;

                    case SlowThenThrow m:
                        m.Latch.Ready(TimeSpan.FromSeconds(10));
                        throw m.E;

                    case AutoReceive _:
                        AutoReceive();
                        return true;
                }
                return false;
            }
        }

        internal class TheState
        {
            public static readonly TheState Instance = new TheState();

            private TheState()
            {
            }
        }

        internal class FsmTarget : FSM<TheState, int>
        {
            private readonly IActorRef monitor;
            private readonly TimeSpan interval;
            private readonly bool repeat;
            private bool restarting = false;

            public FsmTarget(IActorRef monitor, TimeSpan interval, bool repeat, Func<int> initial)
            {
                this.monitor = monitor;
                this.interval = interval;
                this.repeat = repeat;

                var i = initial();
                InitializeFSM(i);
                SetTimer("T", new Tick(i), interval, repeat);
            }

            protected override void PreRestart(Exception reason, object message)
            {
                restarting = true;
                base.PreRestart(reason, message);
                monitor.Tell(new GotPreRestart(IsTimerActive("T")));
            }

            protected override void PostStop()
            {
                base.PostStop();
                if (!restarting)
                    monitor.Tell(new GotPostStop(IsTimerActive("T")));
            }

            State<TheState, int> Bump(int bumpCount)
            {
                SetTimer("T", new Tick(bumpCount + 1), interval, repeat);
                return Stay().Using(bumpCount + 1);
            }

            State<TheState, int> AutoReceive()
            {
                SetTimer("A", PoisonPill.Instance, interval, repeat);
                return Stay();
            }

            private void InitializeFSM(int initial)
            {
                When(TheState.Instance, e =>
                {
                    switch (e.FsmEvent)
                    {
                        case Tick m:
                            monitor.Tell(new Tock(m.N));
                            return Stay();

                        case Bump _:
                            return Bump(e.StateData);

                        case SlowThenBump m:
                            m.Latch.Ready(TimeSpan.FromSeconds(10));
                            return Bump(e.StateData);

                        case End _:
                            return Stop();

                        case Cancel _:
                            CancelTimer("T");
                            return Stay();

                        case Throw m:
                            throw m.E;

                        case SlowThenThrow m:
                            m.Latch.Ready(TimeSpan.FromSeconds(10));
                            throw m.E;

                        case AutoReceive _:
                            return AutoReceive();
                    }
                    return null;
                });

                StartWith(TheState.Instance, initial);
            }
        }

        #endregion
    }


    public class TimersAndStashSpec : AkkaSpec
    {
        [Fact]
        public void Timers_combined_with_stashing_should_work()
        {

            var probe = CreateTestProbe();
            var actor = this.Sys.ActorOf(Props.Create(() => new ActorWithTimerAndStash(probe.Ref)));
            probe.ExpectMsg("saw-scheduled");
            actor.Tell(StopStashing.Instance);
            probe.ExpectMsg("scheduled");
        }

        #region actors

        internal class StopStashing
        {
            public static readonly StopStashing Instance = new StopStashing();

            private StopStashing()
            {
            }
        }

        internal class ActorWithTimerAndStash : ActorBase, IWithUnboundedStash, IWithTimers
        {
            private IActorRef probe;

            public IStash Stash { get; set; }
            public ITimerScheduler Timers { get; set; }

            public ActorWithTimerAndStash(IActorRef probe)
            {
                this.probe = probe;

                Timers.StartSingleTimer("key", "scheduled", TimeSpan.FromMilliseconds(50));
                Context.Become(Stashing);
            }

            private bool NotStashing(object message)
            {
                probe.Tell(message);
                return true;
            }

            private bool Stashing(object message)
            {
                switch (message)
                {
                    case StopStashing _:
                        Context.Become(NotStashing);
                        Stash.UnstashAll();
                        return true;

                    case "scheduled":
                        probe.Tell("saw-scheduled");
                        Stash.Stash();
                        return true;
                }
                return false;
            }

            protected override bool Receive(object message)
            {
                return false;
            }
        }

        #endregion
    }
}
