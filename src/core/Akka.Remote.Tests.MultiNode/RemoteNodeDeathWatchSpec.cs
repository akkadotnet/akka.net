//-----------------------------------------------------------------------
// <copyright file="RemoteNodeDeathWatchSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Akka.TestKit;
using FluentAssertions;
using static Akka.Remote.Tests.MultiNode.RemoteNodeDeathWatchMultiNetSpec;

namespace Akka.Remote.Tests.MultiNode;

public class RemoteNodeDeathWatchMultiNetSpec : MultiNodeConfig
{
    public RemoteNodeDeathWatchMultiNetSpec()
    {
        First = Role("first");
        Second = Role("second");
        Third = Role("third");

        CommonConfig = DebugConfig(false).WithFallback(ConfigurationFactory.ParseString(@"
                akka.loglevel = INFO
                akka.remote.log-remote-lifecycle-events = off
                ## Use a tighter setting than the default, otherwise it takes 20s for DeathWatch to trigger
                akka.remote.watch-failure-detector.acceptable-heartbeat-pause = 3 s
            "));

        TestTransport = true;
    }

    public RoleName First { get; }
    public RoleName Second { get; }
    public RoleName Third { get; }

    public sealed class WatchIt
    {
        public WatchIt(IActorRef watchee)
        {
            Watchee = watchee;
        }

        public IActorRef Watchee { get; }
    }

    public sealed class UnwatchIt
    {
        public UnwatchIt(IActorRef watchee)
        {
            Watchee = watchee;
        }

        public IActorRef Watchee { get; }
    }

    public sealed class Ack
    {
        public static Ack Instance { get; } = new();

        private Ack()
        {
        }
    }

    /// <summary>
    /// Forwarding <see cref="Terminated"/> to non-watching testActor is not possible,
    /// and therefore the <see cref="Terminated"/> message is wrapped.
    /// </summary>
    public sealed class WrappedTerminated
    {
        public WrappedTerminated(Terminated t)
        {
            T = t;
        }

        public Terminated T { get; }
    }

    public class ProbeActor : ReceiveActor
    {
        private readonly IActorRef _testActor;

        public ProbeActor(IActorRef testActor)
        {
            _testActor = testActor;

            Receive<WatchIt>(w =>
            {
                Context.Watch(w.Watchee);
                Sender.Tell(Ack.Instance);
            });
            Receive<UnwatchIt>(w =>
            {
                Context.Unwatch(w.Watchee);
                Sender.Tell(Ack.Instance);
            });
            Receive<Terminated>(t => _testActor.Forward(new WrappedTerminated(t)));
            ReceiveAny(msg => _testActor.Forward(msg));
        }
    }
}

public abstract class RemoteNodeDeathWatchSpec : MultiNodeSpec
{
    private readonly RemoteNodeDeathWatchMultiNetSpec _config;
    private IActorRef _remoteWatcher;

    protected RemoteNodeDeathWatchSpec(Type type) : this(new RemoteNodeDeathWatchMultiNetSpec(), type)
    {
    }

    protected RemoteNodeDeathWatchSpec(RemoteNodeDeathWatchMultiNetSpec config, Type type) : base(config, type)
    {
        _config = config;

        MuteDeadLetters(null, typeof(Heartbeat));
    }

    protected override int InitialParticipantsValueFactory => Roles.Count;

    protected abstract string Scenario { get; }

    protected abstract Func<Task> SleepAsync { get; }

    private async Task<IActorRef> GetRemoteWatcherAsync()
    {
        if (_remoteWatcher is null)
        {
            Sys.ActorSelection("/system/remote-watcher").Tell(new Identify(null));
            _remoteWatcher = (await ExpectMsgAsync<ActorIdentity>(TimeSpan.FromSeconds(10))).Subject;
        }

        return _remoteWatcher;
    }

    /// <summary>
    /// Upstream (Pekko) asserts <c>actorIdentity.ref.isDefined</c> before unwrapping the identity; the
    /// .NET port had dropped that assertion, so a node that never created the requested actor handed a
    /// null <see cref="ActorIdentity.Subject"/> straight into <c>Context.Watch(null)</c> downstream. That
    /// throws off the test thread, and the failure that surfaces is an unrelated Ack timeout rather than
    /// the real "this actor doesn't exist" cause. Restoring the assertion here makes a missing actor fail
    /// at the identify, naming the actor path.
    /// </summary>
    private async Task<IActorRef> IdentifyAsync(RoleName role, string actorName)
    {
        Sys.ActorSelection(await NodeAsync(role) / "user" / actorName).Tell(new Identify(actorName));
        var identity = await ExpectMsgAsync<ActorIdentity>(TimeSpan.FromSeconds(10));
        identity.Subject.Should().NotBeNull(
            "node [{0}] must answer for /user/{1}", role.Name, actorName);
        return identity.Subject;
    }

    /// <summary>
    /// Bound for a <see cref="WrappedTerminated"/> expected after the peer has already proven (via a
    /// barrier entered only once its own local watch observed the death, see
    /// <see cref="RemoteNodeDeathWatch_must_receive_Terminated_when_remote_actor_is_stoppedAsync"/>) that
    /// the <c>DeathWatchNotification</c> system message was already handed to remoting. Nominal cost from
    /// there is one one-way hop on an association this phase has already exercised in both directions
    /// (identify out, "helloN" back), plus three local mailbox hops on the receiving side
    /// (/system/remote-watcher, then the watcher actor, then the TestActor) - single-digit ms. A lost or
    /// unacked frame is recovered by system-message redelivery: on classic remoting the repeating
    /// AttemptSysMsgRedelivery timer at akka.remote.resend-interval (Remote.conf, 2s), which is the value
    /// read below; Artery resends at akka.remote.artery.advanced.system-message-resend-interval (1s), so
    /// the same budget covers it with more room. Budget two resend cycles plus one hop and dispatcher
    /// scheduling slack on a loaded CI agent: 2s + 2s + 2s = 6s.
    ///
    /// NOT derived from the watch failure detector: the peer stays alive and keeps heartbeating in this
    /// scenario, so RemoteWatcher never marks it unreachable and AddressTerminated never fires. The FD is
    /// the backstop for a dead node, not for a single lost notification on a live one.
    ///
    /// The old bound was the flat 3s akka.test.single-expect-default, which is shorter than one resend
    /// cycle plus a hop - it could not survive a single dropped system message by construction.
    ///
    /// Pass this value undilated: ExpectMsgAsync's timeout parameter is [AutoDilate] and dilates
    /// internally, so pre-dilating here would square the time factor.
    /// </summary>
    private TimeSpan RemoteTerminationTimeout
    {
        get
        {
            var resend = RARP.For(Sys).Provider.RemoteSettings.SysResendTimeout; // classic resend-interval, 2s
            return resend + resend + TimeSpan.FromSeconds(2);                   // = 6s
        }
    }

    private async Task AssertCleanup(TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(5);

        await WithinAsync(timeout.Value, async () =>
        {
            await AwaitAssertAsync(async () =>
            {
                (await GetRemoteWatcherAsync()).Tell(RemoteWatcher.Stats.Empty);
                await ExpectMsgAsync<RemoteWatcher.Stats>(s => Equals(s, RemoteWatcher.Stats.Empty));
            });
        });
    }

    [MultiNodeFact]
    public async Task RemoteNodeDeathWatchSpecs()
    {
        Console.WriteLine($"Executing with {Scenario} scenario");

        await RemoteNodeDeathWatch_must_receive_Terminated_when_remote_actor_is_stoppedAsync();
        await RemoteNodeDeathWatch_must_cleanup_after_watch_unwatchAsync();
        await RemoteNodeDeathWatch_must_cleanup_after_bi_directional_watch_unwatchAsync();
        await RemoteNodeDeathWatch_must_cleanup_after_bi_directional_watch_stop_unwatchAsync();
        await RemoteNodeDeathWatch_must_cleanup_after_stopAsync();
        await RemoteNodeDeathWatch_must_receive_Terminated_when_watched_node_crashAsync();
        await RemoteNodeDeathWatch_must_cleanup_when_watching_node_crashAsync();
    }

    private async Task RemoteNodeDeathWatch_must_receive_Terminated_when_remote_actor_is_stoppedAsync()
    {
        await RunOnAsync(async () =>
        {
            var watcher = Sys.ActorOf(Props.Create(() => new ProbeActor(TestActor)), "watcher1");
            await EnterBarrierAsync("actors-started-1");

            var subject = await IdentifyAsync(_config.Second, "subject1");
            watcher.Tell(new WatchIt(subject));
            await ExpectMsgAsync<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
            subject.Tell("hello1");
            await EnterBarrierAsync("hello1-message-sent");
            await EnterBarrierAsync("watch-established-1");

            await SleepAsync();
            await EnterBarrierAsync("subject-stopped-1");
            (await ExpectMsgAsync<WrappedTerminated>(RemoteTerminationTimeout)).T.ActorRef.ShouldBe(subject);
        }, _config.First);

        await RunOnAsync(async () =>
        {
            var subject = Sys.ActorOf(Props.Create(() => new ProbeActor(TestActor)), "subject1");
            await EnterBarrierAsync("actors-started-1");

            await EnterBarrierAsync("hello1-message-sent");
            await ExpectMsgAsync("hello1", TimeSpan.FromSeconds(3));
            await EnterBarrierAsync("watch-established-1");

            await SleepAsync();
            // A local watch on second's own TestActor + Sys.Stop proves the subject is dead on second
            // before first's window opens. ActorCell.TellWatchersWeDied notifies REMOTE watchers in a
            // strictly earlier loop than LOCAL watchers (second's TestActor here). The remote watcher
            // recorded on the subject is first's /system/remote-watcher, which re-issued watcher1's
            // watch over the wire (RemoteActorRef intercepts Watch; RemoteWatcher forwards the
            // notification to watcher1 locally). So by the time ExpectTerminatedAsync completes, the
            // DeathWatchNotification bound for first has already been handed to the remoting stack.
            // That closes out the independent Task.Delay(3000) timer-skew term that used to be part
            // of first's wait budget.
            await WatchAsync(subject);
            Sys.Stop(subject);
            await ExpectTerminatedAsync(subject);
            await EnterBarrierAsync("subject-stopped-1");
        }, _config.Second);

        await RunOnAsync(async () =>
        {
            await EnterBarrierAsync("actors-started-1");
            await EnterBarrierAsync("hello1-message-sent");
            await EnterBarrierAsync("watch-established-1");
            await EnterBarrierAsync("subject-stopped-1");
        }, _config.Third);

        await EnterBarrierAsync("terminated-verified-1");

        // verify that things are cleaned up, and heartbeating is stopped
        await AssertCleanup();
        await ExpectNoMsgAsync(TimeSpan.FromSeconds(2));
        await AssertCleanup();

        await EnterBarrierAsync("after-1");
    }

    private async Task RemoteNodeDeathWatch_must_cleanup_after_watch_unwatchAsync()
    {
        await RunOnAsync(async () =>
        {
            var watcher = Sys.ActorOf(Props.Create(() => new ProbeActor(TestActor)), "watcher2");
            await EnterBarrierAsync("actors-started-2");

            var subject = await IdentifyAsync(_config.Second, "subject2");
            watcher.Tell(new WatchIt(subject));
            await ExpectMsgAsync<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
            await EnterBarrierAsync("watch-2");

            await SleepAsync();

            watcher.Tell(new UnwatchIt(subject));
            await ExpectMsgAsync<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
            await EnterBarrierAsync("unwatch-2");
        }, _config.First);

        await RunOnAsync(() =>
        {
            Sys.ActorOf(Props.Create(() => new ProbeActor(TestActor)), "subject2");
            return Task.CompletedTask;
        }, _config.Second);

        await RunOnAsync(async () =>
        {
            await EnterBarrierAsync("actors-started-2");
            await EnterBarrierAsync("watch-2");
            await EnterBarrierAsync("unwatch-2");
        }, _config.Second, _config.Third);

        // verify that things are cleaned up, and heartbeating is stopped
        await AssertCleanup();
        await ExpectNoMsgAsync(TimeSpan.FromSeconds(2));
        await AssertCleanup();

        await EnterBarrierAsync("after-2");
    }

    private async Task RemoteNodeDeathWatch_must_cleanup_after_bi_directional_watch_unwatchAsync()
    {
        await RunOnAsync(async () =>
        {
            var watcher = Sys.ActorOf(Props.Create(() => new ProbeActor(TestActor)), "watcher3");
            Sys.ActorOf(Props.Create(() => new ProbeActor(TestActor)), "subject3");
            await EnterBarrierAsync("actors-started-3");

            var other = Myself == _config.First ? _config.Second : _config.First;
            var subject = await IdentifyAsync(other, "subject3");
            watcher.Tell(new WatchIt(subject));
            await ExpectMsgAsync<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
            await EnterBarrierAsync("watch-3");

            await SleepAsync();

            watcher.Tell(new UnwatchIt(subject));
            await ExpectMsgAsync<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
            await EnterBarrierAsync("unwatch-3");
        }, _config.First, _config.Second);

        await RunOnAsync(async () =>
        {
            await EnterBarrierAsync("actors-started-3");
            await EnterBarrierAsync("watch-3");
            await EnterBarrierAsync("unwatch-3");
        }, _config.Third);

        // verify that things are cleaned up, and heartbeating is stopped
        await AssertCleanup();
        await ExpectNoMsgAsync(TimeSpan.FromSeconds(2));
        await AssertCleanup();

        await EnterBarrierAsync("after-3");
    }

    private async Task RemoteNodeDeathWatch_must_cleanup_after_bi_directional_watch_stop_unwatchAsync()
    {
        await RunOnAsync(async () =>
        {
            var watcher1 = Sys.ActorOf(Props.Create(() => new ProbeActor(TestActor)), "w1");
            var watcher2 = Sys.ActorOf(Props.Create(() => new ProbeActor(TestActor)), "w2");
            var s1 = Sys.ActorOf(Props.Create(() => new ProbeActor(TestActor)), "s1");
            var s2 = Sys.ActorOf(Props.Create(() => new ProbeActor(TestActor)), "s2");
            await EnterBarrierAsync("actors-started-4");

            var other = Myself == _config.First ? _config.Second : _config.First;
            var subject1 = await IdentifyAsync(other, "s1");
            var subject2 = await IdentifyAsync(other, "s2");
            watcher1.Tell(new WatchIt(subject1));
            await ExpectMsgAsync<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
            watcher2.Tell(new WatchIt(subject2));
            await ExpectMsgAsync<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
            await EnterBarrierAsync("watch-4");

            await SleepAsync();

            watcher1.Tell(new UnwatchIt(subject1));
            await ExpectMsgAsync<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
            await EnterBarrierAsync("unwatch-s1-4");
            Sys.Stop(s1);
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(2));
            await EnterBarrierAsync("stop-s1-4");

            Sys.Stop(s2);
            await EnterBarrierAsync("stop-s2-4");
            (await ExpectMsgAsync<WrappedTerminated>(RemoteTerminationTimeout)).T.ActorRef.ShouldBe(subject2);
        }, _config.First, _config.Second);

        await RunOnAsync(async () =>
        {
            await EnterBarrierAsync("actors-started-4");
            await EnterBarrierAsync("watch-4");
            await EnterBarrierAsync("unwatch-s1-4");
            await EnterBarrierAsync("stop-s1-4");
            await EnterBarrierAsync("stop-s2-4");
        }, _config.Third);

        // verify that things are cleaned up, and heartbeating is stopped
        await AssertCleanup();
        await ExpectNoMsgAsync(TimeSpan.FromSeconds(2));
        await AssertCleanup();

        await EnterBarrierAsync("after-4");
    }

    private async Task RemoteNodeDeathWatch_must_cleanup_after_stopAsync()
    {
        await RunOnAsync(async () =>
        {
            var p1 = CreateTestProbe();
            var p2 = CreateTestProbe();
            var p3 = CreateTestProbe();
            var a1 = Sys.ActorOf(Props.Create(() => new ProbeActor(p1.Ref)), "a1");
            var a2 = Sys.ActorOf(Props.Create(() => new ProbeActor(p2.Ref)), "a2");
            var a3 = Sys.ActorOf(Props.Create(() => new ProbeActor(p3.Ref)), "a3");

            await EnterBarrierAsync("actors-started-5");

            var b1 = await IdentifyAsync(_config.Second, "b1");
            var b2 = await IdentifyAsync(_config.Second, "b2");
            var b3 = await IdentifyAsync(_config.Second, "b3");

            a1.Tell(new WatchIt(b1));
            await ExpectMsgAsync<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
            a1.Tell(new WatchIt(b2));
            await ExpectMsgAsync<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
            a2.Tell(new WatchIt(b2));
            await ExpectMsgAsync<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
            a3.Tell(new WatchIt(b3));
            await ExpectMsgAsync<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
            await SleepAsync();
            a2.Tell(new UnwatchIt(b2));
            await ExpectMsgAsync<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));

            await EnterBarrierAsync("watch-established-5");

            await SleepAsync();

            a1.Tell(PoisonPill.Instance);
            a2.Tell(PoisonPill.Instance);
            a3.Tell(PoisonPill.Instance);

            await EnterBarrierAsync("stopped-5");
            await EnterBarrierAsync("terminated-verified-5");

            // verify that things are cleaned up, and heartbeating is stopped
            await AssertCleanup();
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(2));
            await AssertCleanup();
        }, _config.First);

        await RunOnAsync(async () =>
        {
            var p1 = CreateTestProbe();
            var p2 = CreateTestProbe();
            var p3 = CreateTestProbe();
            var b1 = Sys.ActorOf(Props.Create(() => new ProbeActor(p1.Ref)), "b1");
            var b2 = Sys.ActorOf(Props.Create(() => new ProbeActor(p2.Ref)), "b2");
            var b3 = Sys.ActorOf(Props.Create(() => new ProbeActor(p3.Ref)), "b3");

            await EnterBarrierAsync("actors-started-5");

            var a1 = await IdentifyAsync(_config.First, "a1");
            var a2 = await IdentifyAsync(_config.First, "a2");
            var a3 = await IdentifyAsync(_config.First, "a3");

            b1.Tell(new WatchIt(a1));
            await ExpectMsgAsync<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
            b1.Tell(new WatchIt(a2));
            await ExpectMsgAsync<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
            b2.Tell(new WatchIt(a2));
            await ExpectMsgAsync<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
            b3.Tell(new WatchIt(a3));
            await ExpectMsgAsync<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
            await SleepAsync();
            b2.Tell(new UnwatchIt(a2));
            await ExpectMsgAsync<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));

            await EnterBarrierAsync("watch-established-5");
            await EnterBarrierAsync("stopped-5");

            (await p1.ReceiveNAsync(2, TimeSpan.FromSeconds(20)).ToListAsync())
                .Cast<WrappedTerminated>()
                .Select(w => w.T.ActorRef)
                .OrderBy(r => r.Path.Name)
                .ShouldBe([a1, a2]);
            (await p3.ExpectMsgAsync<WrappedTerminated>(TimeSpan.FromSeconds(5))).T.ActorRef.ShouldBe(a3);
            await p2.ExpectNoMsgAsync(TimeSpan.FromSeconds(2));
            await EnterBarrierAsync("terminated-verified-5");
                
            // verify that things are cleaned up, and heartbeating is stopped
            await AssertCleanup();
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(2));
            await p1.ExpectNoMsgAsync(100);
            await p2.ExpectNoMsgAsync(100);
            await p3.ExpectNoMsgAsync(100);
            await AssertCleanup();
        }, _config.Second);

        await RunOnAsync(async () =>
        {
            await EnterBarrierAsync("actors-started-5");
            await EnterBarrierAsync("watch-established-5");
            await EnterBarrierAsync("stopped-5");
            await EnterBarrierAsync("terminated-verified-5");
        }, _config.Third);

        await EnterBarrierAsync("after-5");
    }

    private async Task RemoteNodeDeathWatch_must_receive_Terminated_when_watched_node_crashAsync()
    {
        await RunOnAsync(async () =>
        {
            var watcher = Sys.ActorOf(Props.Create(() => new ProbeActor(TestActor)), "watcher6");
            var watcher2 = Sys.ActorOf(Props.Create(() => new ProbeActor(Sys.DeadLetters)));
            await EnterBarrierAsync("actors-started-6");

            var subject = await IdentifyAsync(_config.Second, "subject6");
            watcher.Tell(new WatchIt(subject));
            await ExpectMsgAsync<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
            watcher2.Tell(new WatchIt(subject));
            await ExpectMsgAsync<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
            subject.Tell("hello6");

            // testing with this watch/unwatch of watcher2 to make sure that the unwatch doesn't
            // remove the first watch
            watcher2.Tell(new UnwatchIt(subject));
            await ExpectMsgAsync<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));

            await EnterBarrierAsync("watch-established-6");

            await SleepAsync();

            Log.Info("exit second");
            await TestConductor.ExitAsync(_config.Second, 0);
            (await ExpectMsgAsync<WrappedTerminated>(TimeSpan.FromSeconds(15))).T.ActorRef.ShouldBe(subject);
                
            // verify that things are cleaned up, and heartbeating is stopped
            await AssertCleanup();
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(2));
            await AssertCleanup();
        }, _config.First);

        await RunOnAsync(async () =>
        {
            Sys.ActorOf(Props.Create(() => new ProbeActor(TestActor)), "subject6");
            await EnterBarrierAsync("actors-started-6");

            await ExpectMsgAsync("hello6", TimeSpan.FromSeconds(3));
            await EnterBarrierAsync("watch-established-6");
        }, _config.Second);

        await RunOnAsync(async () =>
        {
            await EnterBarrierAsync("actors-started-6");
            await EnterBarrierAsync("watch-established-6");
        }, _config.Third);

        await EnterBarrierAsync("after-6");
    }

    private async Task RemoteNodeDeathWatch_must_cleanup_when_watching_node_crashAsync()
    {
        await RunOnAsync(async () =>
        {
            var watcher = Sys.ActorOf(Props.Create(() => new ProbeActor(TestActor)), "watcher7");
            await EnterBarrierAsync("actors-started-7");

            var subject = await IdentifyAsync(_config.First, "subject7");
            watcher.Tell(new WatchIt(subject));
            await ExpectMsgAsync<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
            subject.Tell("hello7");
            await EnterBarrierAsync("watch-established-7");
        }, _config.Third);

        await RunOnAsync(async () =>
        {
            Sys.ActorOf(Props.Create(() => new ProbeActor(TestActor)), "subject7");
            await EnterBarrierAsync("actors-started-7");

            await ExpectMsgAsync("hello7", TimeSpan.FromSeconds(3));
            await EnterBarrierAsync("watch-established-7");

            await SleepAsync();

            Log.Info("exit third");
            await TestConductor.ExitAsync(_config.Third, 0);

            // verify that things are cleaned up, and heartbeating is stopped
            await AssertCleanup(TimeSpan.FromSeconds(20));
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(2));
            await AssertCleanup();
        }, _config.First);

        await EnterBarrierAsync("after-7");
    }
}


#region Several different variations of the test

public class RemoteNodeDeathWatchFastSpec : RemoteNodeDeathWatchSpec
{
    public RemoteNodeDeathWatchFastSpec() : base(typeof(RemoteNodeDeathWatchFastSpec))
    { }

    protected override string Scenario { get; } = "fast";

    protected override Func<Task> SleepAsync { get; } = async () => await Task.Delay(100);
}

public class RemoteNodeDeathWatchSlowSpec : RemoteNodeDeathWatchSpec
{
    public RemoteNodeDeathWatchSlowSpec() : base(typeof(RemoteNodeDeathWatchSlowSpec))
    { }

    protected override string Scenario { get; } = "slow";

    protected override Func<Task> SleepAsync { get; } = async () => await Task.Delay(3000);
}

#endregion