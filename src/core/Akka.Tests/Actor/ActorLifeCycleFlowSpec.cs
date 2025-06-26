//-----------------------------------------------------------------------
// <copyright file="ActorLifeCycleFlowSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Actor;

#nullable enable
public class ActorLifeCycleFlowSpec : AkkaSpec
{
    internal static class Msg
    {
        public enum CallTime
        {
            PreBase,
            PostBase,
            Single
        }

        public enum Methods
        {
            None,
            AroundPreRestart,
            AroundPreStart,
            PreStart,
            AroundPostRestart,
            PreRestart,
            PostRestart,
            AroundPostStop,
            PostStop,
        }
        
        public record Crash(string Message);
        public record Crashed(int Id);
        public record StartTimer(Methods InMethod, CallTime CallTime);
        public record TimerStarted;
        public record GetTimers;
        public record Timers(ImmutableArray<StartTimer> ActiveTimers);
        public record Stop;

        public record Invocation(Methods Method, int Id, CallTime CallTime, ImmutableArray<StartTimer> ActiveTimers)
        {
            public virtual bool Equals(Invocation? other)
            {
                if (other is null)
                    return false;
                if (Method != other.Method || Id != other.Id || CallTime != other.CallTime)
                    return false;
                if (ActiveTimers.Length != other.ActiveTimers.Length)
                    return false;
                return !ActiveTimers.Any(timerKey => !other.ActiveTimers.Contains(timerKey));
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = Method.GetHashCode();
                    hashCode = (hashCode * 397) ^ Id;
                    hashCode = (hashCode * 397) ^ (int)CallTime;
                    hashCode = (hashCode * 397) ^ ActiveTimers.GetHashCode();
                    return hashCode;
                }
            }

            public override string ToString()
                => $"Invocation {{ {nameof(Method)} = {Method}, {nameof(Id)} = {Id}, {nameof(CallTime)} = {CallTime}, " +
                   $"{nameof(ActiveTimers)} = [{string.Join(", ", ActiveTimers)}] }}";
        }
    }

    private class LifeCycleActor : UntypedActor, IWithTimers
    {
        private readonly int _id;
        private readonly IActorRef _probe;
        private readonly ImmutableArray<Msg.StartTimer> _startTimers;
        private readonly ILoggingAdapter _log;
            
        public LifeCycleActor(IActorRef probe, int id, ImmutableArray<Msg.StartTimer> startTimers)
        {
            _log = Context.GetLogger();
            _probe = probe;
            _id = id;
            _startTimers = startTimers;
        }

        public ITimerScheduler Timers { get; set; } = null!;

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case Msg.Crash c:
                    _log.Info(c.ToString());
                    _probe.Tell(new Msg.Crashed(_id));
                    throw new Exception(c.Message);
                
                case Msg.Stop s:
                    _log.Info(s.ToString());
                    Context.Stop(Self);
                    break;
                
                case Msg.GetTimers:
                    _probe.Tell(new Msg.Timers(ActiveTimers));
                    break;
                
                case Msg.StartTimer t:
                    Timers.StartSingleTimer(t, t, 30.Minutes());
                    _probe.Tell(new Msg.TimerStarted());
                    break;
            }
        }

        private void StartTimer(Msg.Methods method, Msg.CallTime callTime)
        {
            var key = _startTimers.FirstOrDefault(t => t.InMethod == method && t.CallTime == callTime);
            if (key is null || Timers.IsTimerActive(key))
                return;

            Timers.StartSingleTimer(key, "time", 30.Minutes());
            _log.Info($"Timer started: {key}");
            _probe.Tell(new Msg.TimerStarted());
        }

        private ImmutableArray<Msg.StartTimer> ActiveTimers
            => Timers.ActiveTimers.Select(o => (Msg.StartTimer)o).ToImmutableArray();

        #region Overrides

        public override void AroundPreRestart(Exception cause, object message)
        {
            const Msg.Methods method = Msg.Methods.AroundPreRestart;
            _log.Info(method.ToString());
            _probe.Tell(new Msg.Invocation(method, _id, Msg.CallTime.PreBase, ActiveTimers));
            StartTimer(method, Msg.CallTime.PreBase);
            base.AroundPreRestart(cause, message);
            _probe.Tell(new Msg.Invocation(method, _id, Msg.CallTime.PostBase, ActiveTimers));
            StartTimer(method, Msg.CallTime.PostBase);
        }
        
        public override void AroundPreStart()
        {
            const Msg.Methods method = Msg.Methods.AroundPreStart;
            _log.Info(method.ToString());
            _probe.Tell(new Msg.Invocation(method, _id, Msg.CallTime.PreBase, ActiveTimers));
            StartTimer(method, Msg.CallTime.PreBase);
            base.AroundPreStart();
            _probe.Tell(new Msg.Invocation(method, _id, Msg.CallTime.PostBase, ActiveTimers));
            StartTimer(method, Msg.CallTime.PostBase);
        }

        protected override void PreStart()
        {
            const Msg.Methods method = Msg.Methods.PreStart;
            _log.Info(method.ToString());
            StartTimer(method, Msg.CallTime.Single);
            _probe.Tell(new Msg.Invocation(method, _id, Msg.CallTime.Single, ActiveTimers));
        }

        public override void AroundPostRestart(Exception cause, object message)
        {
            const Msg.Methods method = Msg.Methods.AroundPostRestart;
            _log.Info(method.ToString());
            _probe.Tell(new Msg.Invocation(method, _id, Msg.CallTime.PreBase, ActiveTimers));
            StartTimer(method, Msg.CallTime.PreBase);
            base.AroundPostRestart(cause, message);
            _probe.Tell(new Msg.Invocation(method, _id, Msg.CallTime.PostBase, ActiveTimers));
            StartTimer(method, Msg.CallTime.PostBase);
        }

        protected override void PreRestart(Exception reason, object message)
        {
            const Msg.Methods method = Msg.Methods.PreRestart;
            _log.Info(method.ToString());
            _probe.Tell(new Msg.Invocation(method, _id, Msg.CallTime.PreBase, ActiveTimers));
            StartTimer(method, Msg.CallTime.PreBase);
            base.PreRestart(reason, message);
            _probe.Tell(new Msg.Invocation(method, _id, Msg.CallTime.PostBase, ActiveTimers));
            StartTimer(method, Msg.CallTime.PostBase);
        }

        protected override void PostRestart(Exception reason)
        {
            const Msg.Methods method = Msg.Methods.PostRestart;
            _log.Info(method.ToString());
            _probe.Tell(new Msg.Invocation(method, _id, Msg.CallTime.PreBase, ActiveTimers));
            StartTimer(method, Msg.CallTime.PreBase);
            base.PostRestart(reason);
            _probe.Tell(new Msg.Invocation(method, _id, Msg.CallTime.PostBase, ActiveTimers));
            StartTimer(method, Msg.CallTime.PostBase);
        }

        public override void AroundPostStop()
        {
            const Msg.Methods method = Msg.Methods.AroundPostStop;
            _log.Info(method.ToString());
            _probe.Tell(new Msg.Invocation(method, _id, Msg.CallTime.PreBase, ActiveTimers));
            StartTimer(method, Msg.CallTime.PreBase);
            base.AroundPostStop();
            _probe.Tell(new Msg.Invocation(method, _id, Msg.CallTime.PostBase, ActiveTimers));
            StartTimer(method, Msg.CallTime.PostBase);
        }

        protected override void PostStop()
        {
            const Msg.Methods method = Msg.Methods.PostStop;
            _log.Info(method.ToString());
            _probe.Tell(new Msg.Invocation(method, _id, Msg.CallTime.Single, ActiveTimers));
        }

        #endregion
    }

    private readonly ImmutableArray<Msg.StartTimer> _emptyTimers = ImmutableArray<Msg.StartTimer>.Empty;
    
    public ActorLifeCycleFlowSpec(ITestOutputHelper output) : base("akka.loglevel = DEBUG", output) { }
    
    [Fact(DisplayName = "ActorBase Lifecycle flow during actor restart must be correct")]
    public async Task ActorLifecycleAssert()
    {
        var testActor = Sys.ActorOf(Props.Create(() => new LifeCycleActor(TestActor, 1, _emptyTimers)));
        await AssertActorStartFlow(1, _emptyTimers);

        testActor.Tell(new Msg.Crash("I crashed"));
        await ExpectMsgAsync(new Msg.Crashed(1));
        await AssertActorRestartFlow(1, _emptyTimers, _emptyTimers);
        
        testActor.Tell(new Msg.Stop());
        await AssertActorStopFlow(1, _emptyTimers, _emptyTimers);
    }

    [Fact(DisplayName = "ActorBase with manual timer trigger, Lifecycle flow during actor restart, Timers must be reset")]
    public async Task ActorLifecycleWithTimerAssert()
    {
        var testActor = Sys.ActorOf(Props.Create(() => new LifeCycleActor(TestActor, 1, _emptyTimers)));
        await AssertActorStartFlow(1, _emptyTimers);

        var startedKeys = new[] { new Msg.StartTimer(Msg.Methods.None, Msg.CallTime.Single) }.ToImmutableArray();
        testActor.Tell(startedKeys[0]);
        await ExpectMsgAsync<Msg.TimerStarted>();
        testActor.Tell(new Msg.GetTimers());
        var timers = await ExpectMsgAsync<Msg.Timers>();
        timers.ActiveTimers.Length.Should().Be(1);
        
        testActor.Tell(new Msg.Crash("I crashed"));
        await ExpectMsgAsync(new Msg.Crashed(1));
        await AssertActorRestartFlow(1, startedKeys, _emptyTimers);
        
        testActor.Tell(new Msg.GetTimers());
        timers = await ExpectMsgAsync<Msg.Timers>();
        timers.ActiveTimers.Length.Should().Be(0);
        
        testActor.Tell(new Msg.Stop());
        await AssertActorStopFlow(1, _emptyTimers, _emptyTimers);
    }

    [Fact(DisplayName = "ActorBase with timers declared in [AroundPreRestart, PreRestart], Lifecycle flow during actor restart, Timers must be reset")]
    public async Task ActorLifecycleWithAroundPreStartAndPreRestartTimerAssert()
    {
        var timerKeys = new[]
        {
            new Msg.StartTimer(Msg.Methods.AroundPreRestart, Msg.CallTime.PreBase),
            new Msg.StartTimer(Msg.Methods.PreRestart, Msg.CallTime.PreBase)
        }.ToImmutableArray();
        
        var testActor = Sys.ActorOf(Props.Create(() => new LifeCycleActor(TestActor, 1, timerKeys)));
        await AssertActorStartFlow(1, timerKeys);

        testActor.Tell(new Msg.Crash("I crashed"));
        await ExpectMsgAsync(new Msg.Crashed(1));
        await AssertActorRestartFlow(1, _emptyTimers, timerKeys);
        
        testActor.Tell(new Msg.GetTimers());
        var timers = await ExpectMsgAsync<Msg.Timers>();
        timers.ActiveTimers.Length.Should().Be(0);
        
        testActor.Tell(new Msg.Stop());
        await AssertActorStopFlow(1, _emptyTimers, timerKeys);
    }
    
    [Fact(DisplayName = "ActorBase with timers declared in [AroundPostReStart, PostRestart], Lifecycle flow during actor restart, Timers must survive")]
    public async Task ActorLifecycleWithAroundPostStartAndPostRestartTimerAssert()
    {
        var timerKeys = new[]
        {
            new Msg.StartTimer(Msg.Methods.AroundPostRestart, Msg.CallTime.PreBase),
            new Msg.StartTimer(Msg.Methods.AroundPostRestart, Msg.CallTime.PostBase),
            new Msg.StartTimer(Msg.Methods.PostRestart, Msg.CallTime.PreBase),
            new Msg.StartTimer(Msg.Methods.PostRestart, Msg.CallTime.PostBase)
        }.ToImmutableArray();
        var testActor = Sys.ActorOf(Props.Create(() => new LifeCycleActor(TestActor, 1, timerKeys)));
        await AssertActorStartFlow(1, timerKeys);

        testActor.Tell(new Msg.Crash("I crashed"));
        await ExpectMsgAsync(new Msg.Crashed(1));
        await AssertActorRestartFlow(1, _emptyTimers, timerKeys);

        testActor.Tell(new Msg.GetTimers());
        var timers = await ExpectMsgAsync<Msg.Timers>();
        timers.ActiveTimers.Length.Should().Be(4);
        timers.ActiveTimers[0].Should().Be(new Msg.StartTimer(Msg.Methods.AroundPostRestart, Msg.CallTime.PreBase));
        timers.ActiveTimers[1].Should().Be(new Msg.StartTimer(Msg.Methods.PostRestart, Msg.CallTime.PreBase));
        timers.ActiveTimers[2].Should().Be(new Msg.StartTimer(Msg.Methods.PostRestart, Msg.CallTime.PostBase));
        timers.ActiveTimers[3].Should().Be(new Msg.StartTimer(Msg.Methods.AroundPostRestart, Msg.CallTime.PostBase));
        
        testActor.Tell(new Msg.Stop());
        await AssertActorStopFlow(1, timers.ActiveTimers, timerKeys);
    }
    
    private async Task AssertActorStartFlow(int id, ImmutableArray<Msg.StartTimer> timerSequence)
    {
        await ExpectMsgAsync(new Msg.Invocation(Msg.Methods.AroundPreStart, id, Msg.CallTime.PreBase, _emptyTimers));
        var startedTimers = await AssertTimerStarted(Msg.Methods.AroundPreStart, Msg.CallTime.PreBase, _emptyTimers, timerSequence);
        
        await ExpectMsgAsync(new Msg.Invocation(Msg.Methods.PreStart, id, Msg.CallTime.Single, startedTimers));
        startedTimers = await AssertTimerStarted(Msg.Methods.PreStart, Msg.CallTime.Single, startedTimers, timerSequence);
        
        await ExpectMsgAsync(new Msg.Invocation(Msg.Methods.AroundPreStart, id, Msg.CallTime.PostBase, startedTimers));
        await AssertTimerStarted(Msg.Methods.AroundPreStart, Msg.CallTime.PostBase, startedTimers, timerSequence);
    }

    /*
     * Restart flow:
     *   ActorCell.FaultRecreate()
     *   |-> AroundPreRestart()
     *   |   |-> Timers.CancelAll()
     *   |   L-> PreRestart()
     *   |       |-> Context.Unwatch() all children
     *   |       L-> PostStop()
     *   L-> ActorCell.FinishRecreate()
     *       L-> AroundPostRestart()
     *           L-> PostRestart()
     *               L-> PreStart()
     */
    private async Task AssertActorRestartFlow(int id, ImmutableArray<Msg.StartTimer> startedTimers, ImmutableArray<Msg.StartTimer> timerSequence)
    {
        // ActorCell.FaultRecreate()
        await ExpectMsgAsync(new Msg.Invocation(Msg.Methods.AroundPreRestart, id, Msg.CallTime.PreBase, startedTimers));
        await AssertTimerStarted(Msg.Methods.AroundPreRestart, Msg.CallTime.PreBase, startedTimers, timerSequence); // started timers doesn't matter because Timers will be cleared
        
        await ExpectMsgAsync(new Msg.Invocation(Msg.Methods.PreRestart, id, Msg.CallTime.PreBase, _emptyTimers)); // Timers cleared
        startedTimers = await AssertTimerStarted(Msg.Methods.PreRestart, Msg.CallTime.PreBase, _emptyTimers, timerSequence);
        
        await ExpectMsgAsync(new Msg.Invocation(Msg.Methods.PostStop, id, Msg.CallTime.Single, startedTimers));
        startedTimers = await AssertTimerStarted(Msg.Methods.PostStop, Msg.CallTime.Single, startedTimers, timerSequence);
        
        await ExpectMsgAsync(new Msg.Invocation(Msg.Methods.PreRestart, id, Msg.CallTime.PostBase, startedTimers));
        startedTimers = await AssertTimerStarted(Msg.Methods.PreRestart, Msg.CallTime.PostBase, startedTimers, timerSequence);
        
        await ExpectMsgAsync(new Msg.Invocation(Msg.Methods.AroundPreRestart, id, Msg.CallTime.PostBase, startedTimers));
        await AssertTimerStarted(Msg.Methods.AroundPreRestart, Msg.CallTime.PostBase, startedTimers, timerSequence); // started timers doesn't matter because Timers will be cleared
        
        // ActorCell.FinishRecreate()
        await ExpectMsgAsync(new Msg.Invocation(Msg.Methods.AroundPostRestart, id, Msg.CallTime.PreBase, _emptyTimers)); // Timers cleared
        startedTimers = await AssertTimerStarted(Msg.Methods.AroundPostRestart, Msg.CallTime.PreBase, _emptyTimers, timerSequence);
        
        await ExpectMsgAsync(new Msg.Invocation(Msg.Methods.PostRestart, id, Msg.CallTime.PreBase, startedTimers));
        startedTimers = await AssertTimerStarted(Msg.Methods.PostRestart, Msg.CallTime.PreBase, startedTimers, timerSequence);
        
        await ExpectMsgAsync(new Msg.Invocation(Msg.Methods.PreStart, id, Msg.CallTime.Single, startedTimers));
        startedTimers = await AssertTimerStarted(Msg.Methods.PreStart, Msg.CallTime.Single, startedTimers, timerSequence);
        
        await ExpectMsgAsync(new Msg.Invocation(Msg.Methods.PostRestart, id, Msg.CallTime.PostBase, startedTimers));
        startedTimers = await AssertTimerStarted(Msg.Methods.PostRestart, Msg.CallTime.PostBase, startedTimers, timerSequence);
        
        await ExpectMsgAsync(new Msg.Invocation(Msg.Methods.AroundPostRestart, id, Msg.CallTime.PostBase, startedTimers));
        await AssertTimerStarted(Msg.Methods.AroundPostRestart, Msg.CallTime.PostBase, startedTimers, timerSequence);
    }

    /*
     * Stop flow:
     *   ActorCell.FinishTerminate()
     *   L-> AroundPostStop()
     *       |-> Timers.CancelAll()
     *       L-> PostStop() 
     */
    private async Task AssertActorStopFlow(int id, ImmutableArray<Msg.StartTimer> startedTimers, ImmutableArray<Msg.StartTimer> timerSequence)
    {
        // ActorCell.FinishTerminate()
        await ExpectMsgAsync(new Msg.Invocation(Msg.Methods.AroundPostStop, id, Msg.CallTime.PreBase, startedTimers));
        await AssertTimerStarted(Msg.Methods.AroundPostStop, Msg.CallTime.PreBase, startedTimers, timerSequence); // started timers doesn't matter because Timers will be cleared
        
        await ExpectMsgAsync(new Msg.Invocation(Msg.Methods.PostStop, id, Msg.CallTime.Single, _emptyTimers)); // Timers cleared
        startedTimers = await AssertTimerStarted(Msg.Methods.PostStop, Msg.CallTime.Single, _emptyTimers, timerSequence);
        
        await ExpectMsgAsync(new Msg.Invocation(Msg.Methods.AroundPostStop, id, Msg.CallTime.PostBase, _emptyTimers));
        await AssertTimerStarted(Msg.Methods.AroundPostStop, Msg.CallTime.PostBase, startedTimers, timerSequence);
    }

    private async Task<ImmutableArray<Msg.StartTimer>> AssertTimerStarted(
        Msg.Methods method,
        Msg.CallTime callTime,
        ImmutableArray<Msg.StartTimer> startedTimers, 
        ImmutableArray<Msg.StartTimer> timerSequence)
    {
        var key = new Msg.StartTimer(method, callTime);
        if (!timerSequence.Contains(key))
            return startedTimers;

        await ExpectMsgAsync<Msg.TimerStarted>();
        return startedTimers.Add(key);
    }
}
