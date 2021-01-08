//-----------------------------------------------------------------------
// <copyright file="RemoteNodeDeathWatchSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Akka.TestKit;
using static Akka.Remote.Tests.MultiNode.RemoteNodeDeathWatchMultiNetSpec;

namespace Akka.Remote.Tests.MultiNode
{
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
            public static Ack Instance { get; } = new Ack();

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
        private readonly Lazy<IActorRef> _remoteWatcher;
        private readonly Func<RoleName, string, IActorRef> _identify;

        protected RemoteNodeDeathWatchSpec(Type type) : this(new RemoteNodeDeathWatchMultiNetSpec(), type)
        {
        }

        protected RemoteNodeDeathWatchSpec(RemoteNodeDeathWatchMultiNetSpec config, Type type) : base(config, type)
        {
            _config = config;

            _remoteWatcher = new Lazy<IActorRef>(() =>
            {
                Sys.ActorSelection("/system/remote-watcher").Tell(new Identify(null));
                return ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(10)).Subject;
            });

            _identify = (role, actorName) =>
            {
                Sys.ActorSelection(Node(role) / "user" / actorName).Tell(new Identify(actorName));
                return ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(10)).Subject;
            };

            MuteDeadLetters(null, typeof(Heartbeat));
        }

        protected override int InitialParticipantsValueFactory => Roles.Count;

        protected abstract string Scenario { get; }

        protected abstract Action Sleep { get; }

        private void AssertCleanup(TimeSpan? timeout = null)
        {
            timeout = timeout ?? TimeSpan.FromSeconds(5);

            Within(timeout.Value, () =>
            {
                AwaitAssert(() =>
                {
                    _remoteWatcher.Value.Tell(RemoteWatcher.Stats.Empty);
                    ExpectMsg<RemoteWatcher.Stats>(s => Equals(s, RemoteWatcher.Stats.Empty));
                });
            });
        }

        [MultiNodeFact]
        public void RemoteNodeDeathWatchSpecs()
        {
            Console.WriteLine($"Executing with {Scenario} scenario");

            RemoteNodeDeathWatch_must_receive_Terminated_when_remote_actor_is_stopped();
            RemoteNodeDeathWatch_must_cleanup_after_watch_unwatch();
            RemoteNodeDeathWatch_must_cleanup_after_bi_directional_watch_unwatch();
            RemoteNodeDeathWatch_must_cleanup_after_bi_directional_watch_stop_unwatch();
            RemoteNodeDeathWatch_must_cleanup_after_stop();
            RemoteNodeDeathWatch_must_receive_Terminated_when_watched_node_crash();
            RemoteNodeDeathWatch_must_cleanup_when_watching_node_crash();
        }

        private void RemoteNodeDeathWatch_must_receive_Terminated_when_remote_actor_is_stopped()
        {
            RunOn(() =>
            {
                var watcher = Sys.ActorOf(Props.Create(() => new ProbeActor(TestActor)), "watcher1");
                EnterBarrier("actors-started-1");

                var subject = _identify(_config.Second, "subject1");
                watcher.Tell(new WatchIt(subject));
                ExpectMsg<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
                subject.Tell("hello1");
                EnterBarrier("hello1-message-sent");
                EnterBarrier("watch-established-1");

                Sleep();
                ExpectMsg<WrappedTerminated>().T.ActorRef.ShouldBe(subject);
            }, _config.First);

            RunOn(() =>
            {
                var subject = Sys.ActorOf(Props.Create(() => new ProbeActor(TestActor)), "subject1");
                EnterBarrier("actors-started-1");

                EnterBarrier("hello1-message-sent");
                ExpectMsg("hello1", TimeSpan.FromSeconds(3));
                EnterBarrier("watch-established-1");

                Sleep();
                Sys.Stop(subject);
            }, _config.Second);

            RunOn(() =>
            {
                EnterBarrier("actors-started-1");
                EnterBarrier("hello1-message-sent");
                EnterBarrier("watch-established-1");
            }, _config.Third);

            EnterBarrier("terminated-verified-1");

            // verify that things are cleaned up, and heartbeating is stopped
            AssertCleanup();
            ExpectNoMsg(TimeSpan.FromSeconds(2));
            AssertCleanup();

            EnterBarrier("after-1");
        }

        private void RemoteNodeDeathWatch_must_cleanup_after_watch_unwatch()
        {
            RunOn(() =>
            {
                var watcher = Sys.ActorOf(Props.Create(() => new ProbeActor(TestActor)), "watcher2");
                EnterBarrier("actors-started-2");

                var subject = _identify(_config.Second, "subject2");
                watcher.Tell(new WatchIt(subject));
                ExpectMsg<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
                EnterBarrier("watch-2");

                Sleep();

                watcher.Tell(new UnwatchIt(subject));
                ExpectMsg<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
                EnterBarrier("unwatch-2");
            }, _config.First);

            RunOn(() => Sys.ActorOf(Props.Create(() => new ProbeActor(TestActor)), "subject2"), _config.Second);
            
            RunOn(() =>
            {
                EnterBarrier("actors-started-2");
                EnterBarrier("watch-2");
                EnterBarrier("unwatch-2");
            }, _config.Second, _config.Third);

            // verify that things are cleaned up, and heartbeating is stopped
            AssertCleanup();
            ExpectNoMsg(TimeSpan.FromSeconds(2));
            AssertCleanup();

            EnterBarrier("after-2");
        }

        private void RemoteNodeDeathWatch_must_cleanup_after_bi_directional_watch_unwatch()
        {
            RunOn(() =>
            {
                var watcher = Sys.ActorOf(Props.Create(() => new ProbeActor(TestActor)), "watcher3");
                Sys.ActorOf(Props.Create(() => new ProbeActor(TestActor)), "subject3");
                EnterBarrier("actors-started-3");

                var other = Myself == _config.First ? _config.Second : _config.First;
                var subject = _identify(other, "subject3");
                watcher.Tell(new WatchIt(subject));
                ExpectMsg<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
                EnterBarrier("watch-3");

                Sleep();

                watcher.Tell(new UnwatchIt(subject));
                ExpectMsg<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
                EnterBarrier("unwatch-3");
            }, _config.First, _config.Second);

            RunOn(() =>
            {
                EnterBarrier("actors-started-3");
                EnterBarrier("watch-3");
                EnterBarrier("unwatch-3");
            }, _config.Third);

            // verify that things are cleaned up, and heartbeating is stopped
            AssertCleanup();
            ExpectNoMsg(TimeSpan.FromSeconds(2));
            AssertCleanup();

            EnterBarrier("after-3");
        }

        private void RemoteNodeDeathWatch_must_cleanup_after_bi_directional_watch_stop_unwatch()
        {
            RunOn(() =>
            {
                var watcher1 = Sys.ActorOf(Props.Create(() => new ProbeActor(TestActor)), "w1");
                var watcher2 = Sys.ActorOf(Props.Create(() => new ProbeActor(TestActor)), "w2");
                var s1 = Sys.ActorOf(Props.Create(() => new ProbeActor(TestActor)), "s1");
                var s2 = Sys.ActorOf(Props.Create(() => new ProbeActor(TestActor)), "s2");
                EnterBarrier("actors-started-4");

                var other = Myself == _config.First ? _config.Second : _config.First;
                var subject1 = _identify(other, "s1");
                var subject2 = _identify(other, "s2");
                watcher1.Tell(new WatchIt(subject1));
                ExpectMsg<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
                watcher2.Tell(new WatchIt(subject2));
                ExpectMsg<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
                EnterBarrier("watch-4");

                Sleep();

                watcher1.Tell(new UnwatchIt(subject1));
                ExpectMsg<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
                EnterBarrier("unwatch-s1-4");
                Sys.Stop(s1);
                ExpectNoMsg(TimeSpan.FromSeconds(2));
                EnterBarrier("stop-s1-4");

                Sys.Stop(s2);
                EnterBarrier("stop-s2-4");
                ExpectMsg<WrappedTerminated>().T.ActorRef.ShouldBe(subject2);
            }, _config.First, _config.Second);

            RunOn(() =>
            {
                EnterBarrier("actors-started-4");
                EnterBarrier("watch-4");
                EnterBarrier("unwatch-s1-4");
                EnterBarrier("stop-s1-4");
                EnterBarrier("stop-s2-4");
            }, _config.Third);

            // verify that things are cleaned up, and heartbeating is stopped
            AssertCleanup();
            ExpectNoMsg(TimeSpan.FromSeconds(2));
            AssertCleanup();

            EnterBarrier("after-4");
        }

        private void RemoteNodeDeathWatch_must_cleanup_after_stop()
        {
            RunOn(() =>
            {
                var p1 = CreateTestProbe();
                var p2 = CreateTestProbe();
                var p3 = CreateTestProbe();
                var a1 = Sys.ActorOf(Props.Create(() => new ProbeActor(p1.Ref)), "a1");
                var a2 = Sys.ActorOf(Props.Create(() => new ProbeActor(p2.Ref)), "a2");
                var a3 = Sys.ActorOf(Props.Create(() => new ProbeActor(p3.Ref)), "a3");

                EnterBarrier("actors-started-5");

                var b1 = _identify(_config.Second, "b1");
                var b2 = _identify(_config.Second, "b2");
                var b3 = _identify(_config.Second, "b3");

                a1.Tell(new WatchIt(b1));
                ExpectMsg<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
                a1.Tell(new WatchIt(b2));
                ExpectMsg<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
                a2.Tell(new WatchIt(b2));
                ExpectMsg<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
                a3.Tell(new WatchIt(b3));
                ExpectMsg<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
                Sleep();
                a2.Tell(new UnwatchIt(b2));
                ExpectMsg<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));

                EnterBarrier("watch-established-5");

                Sleep();

                a1.Tell(PoisonPill.Instance);
                a2.Tell(PoisonPill.Instance);
                a3.Tell(PoisonPill.Instance);

                EnterBarrier("stopped-5");
                EnterBarrier("terminated-verified-5");

                // verify that things are cleaned up, and heartbeating is stopped
                AssertCleanup();
                ExpectNoMsg(TimeSpan.FromSeconds(2));
                AssertCleanup();
            }, _config.First);

            RunOn(() =>
            {
                var p1 = CreateTestProbe();
                var p2 = CreateTestProbe();
                var p3 = CreateTestProbe();
                var b1 = Sys.ActorOf(Props.Create(() => new ProbeActor(p1.Ref)), "b1");
                var b2 = Sys.ActorOf(Props.Create(() => new ProbeActor(p2.Ref)), "b2");
                var b3 = Sys.ActorOf(Props.Create(() => new ProbeActor(p3.Ref)), "b3");

                EnterBarrier("actors-started-5");

                var a1 = _identify(_config.First, "a1");
                var a2 = _identify(_config.First, "a2");
                var a3 = _identify(_config.First, "a3");

                b1.Tell(new WatchIt(a1));
                ExpectMsg<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
                b1.Tell(new WatchIt(a2));
                ExpectMsg<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
                b2.Tell(new WatchIt(a2));
                ExpectMsg<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
                b3.Tell(new WatchIt(a3));
                ExpectMsg<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
                Sleep();
                b2.Tell(new UnwatchIt(a2));
                ExpectMsg<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));

                EnterBarrier("watch-established-5");
                EnterBarrier("stopped-5");

                p1.ReceiveN(2, TimeSpan.FromSeconds(20))
                    .Cast<WrappedTerminated>()
                    .Select(w => w.T.ActorRef)
                    .OrderBy(r => r.Path.Name)
                    .ShouldBe(new[] {a1, a2});
                p3.ExpectMsg<WrappedTerminated>(TimeSpan.FromSeconds(5)).T.ActorRef.ShouldBe(a3);
                p2.ExpectNoMsg(TimeSpan.FromSeconds(2));
                EnterBarrier("terminated-verified-5");
                
                // verify that things are cleaned up, and heartbeating is stopped
                AssertCleanup();
                ExpectNoMsg(TimeSpan.FromSeconds(2));
                p1.ExpectNoMsg(100);
                p2.ExpectNoMsg(100);
                p3.ExpectNoMsg(100);
                AssertCleanup();
            }, _config.Second);

            RunOn(() =>
            {
                EnterBarrier("actors-started-5");
                EnterBarrier("watch-established-5");
                EnterBarrier("stopped-5");
                EnterBarrier("terminated-verified-5");
            }, _config.Third);

            EnterBarrier("after-5");
        }

        private void RemoteNodeDeathWatch_must_receive_Terminated_when_watched_node_crash()
        {
            RunOn(() =>
            {
                var watcher = Sys.ActorOf(Props.Create(() => new ProbeActor(TestActor)), "watcher6");
                var watcher2 = Sys.ActorOf(Props.Create(() => new ProbeActor(Sys.DeadLetters)));
                EnterBarrier("actors-started-6");

                var subject = _identify(_config.Second, "subject6");
                watcher.Tell(new WatchIt(subject));
                ExpectMsg<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
                watcher2.Tell(new WatchIt(subject));
                ExpectMsg<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
                subject.Tell("hello6");

                // testing with this watch/unwatch of watcher2 to make sure that the unwatch doesn't
                // remove the first watch
                watcher2.Tell(new UnwatchIt(subject));
                ExpectMsg<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));

                EnterBarrier("watch-established-6");

                Sleep();

                Log.Info("exit second");
                TestConductor.Exit(_config.Second, 0).Wait();
                ExpectMsg<WrappedTerminated>(TimeSpan.FromSeconds(15)).T.ActorRef.ShouldBe(subject);
                
                // verify that things are cleaned up, and heartbeating is stopped
                AssertCleanup();
                ExpectNoMsg(TimeSpan.FromSeconds(2));
                AssertCleanup();
            }, _config.First);

            RunOn(() =>
            {
                Sys.ActorOf(Props.Create(() => new ProbeActor(TestActor)), "subject6");
                EnterBarrier("actors-started-6");

                ExpectMsg("hello6", TimeSpan.FromSeconds(3));
                EnterBarrier("watch-established-6");
            }, _config.Second);

            RunOn(() =>
            {
                EnterBarrier("actors-started-6");
                EnterBarrier("watch-established-6");
            }, _config.Third);

            EnterBarrier("after-6");
        }

        private void RemoteNodeDeathWatch_must_cleanup_when_watching_node_crash()
        {
            RunOn(() =>
            {
                var watcher = Sys.ActorOf(Props.Create(() => new ProbeActor(TestActor)), "watcher7");
                EnterBarrier("actors-started-7");

                var subject = _identify(_config.First, "subject7");
                watcher.Tell(new WatchIt(subject));
                ExpectMsg<RemoteNodeDeathWatchMultiNetSpec.Ack>(TimeSpan.FromSeconds(1));
                subject.Tell("hello7");
                EnterBarrier("watch-established-7");
            }, _config.Third);

            RunOn(() =>
            {
                Sys.ActorOf(Props.Create(() => new ProbeActor(TestActor)), "subject7");
                EnterBarrier("actors-started-7");

                ExpectMsg("hello7", TimeSpan.FromSeconds(3));
                EnterBarrier("watch-established-7");

                Sleep();

                Log.Info("exit third");
                TestConductor.Exit(_config.Third, 0).Wait();

                // verify that things are cleaned up, and heartbeating is stopped
                AssertCleanup(TimeSpan.FromSeconds(20));
                ExpectNoMsg(TimeSpan.FromSeconds(2));
                AssertCleanup();
            }, _config.First);

            EnterBarrier("after-7");
        }
    }


    #region Several different variations of the test

    public class RemoteNodeDeathWatchFastSpec : RemoteNodeDeathWatchSpec
    {
        public RemoteNodeDeathWatchFastSpec() : base(typeof(RemoteNodeDeathWatchFastSpec))
        { }

        protected override string Scenario { get; } = "fast";

        protected override Action Sleep { get; } = () => Thread.Sleep(100);
    }

    public class RemoteNodeDeathWatchSlowSpec : RemoteNodeDeathWatchSpec
    {
        public RemoteNodeDeathWatchSlowSpec() : base(typeof(RemoteNodeDeathWatchSlowSpec))
        { }

        protected override string Scenario { get; } = "slow";

        protected override Action Sleep { get; } = () => Thread.Sleep(3000);
    }

    #endregion

}
