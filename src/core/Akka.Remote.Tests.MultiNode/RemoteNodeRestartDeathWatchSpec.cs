//-----------------------------------------------------------------------
// <copyright file="RemoteNodeRestartDeathWatchSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Akka.TestKit;
using Akka.TestKit.Xunit;
using Akka.Util;
using Akka.Util.Internal;
using FluentAssertions;

namespace Akka.Remote.Tests.MultiNode
{
    public class RemoteNodeRestartDeathWatchSpec : MultiNodeSpec
    {
        private readonly RemoteNodeRestartDeathWatchSpecConfig _specConfig;

        public RemoteNodeRestartDeathWatchSpec()
            : this(new RemoteNodeRestartDeathWatchSpecConfig())
        {
        }

        protected RemoteNodeRestartDeathWatchSpec(RemoteNodeRestartDeathWatchSpecConfig specConfig)
            : base(specConfig, typeof(RemoteNodeRestartDeathWatchSpec))
        {
            _specConfig = specConfig;
        }

        protected override int InitialParticipantsValueFactory
        {
            get { return Roles.Count; }
        }

        protected async Task<IActorRef> IdentifyAsync(RoleName role, string actorName)
        {
            Sys.ActorSelection(Node(role)/"user"/actorName).Tell(new Identify(actorName));
            return (await ExpectMsgAsync<ActorIdentity>()).Subject;
        }


        [MultiNodeFact]
        public async Task Must_receive_terminated_when_remote_actor_system_is_restarted()
        {

            await RunOnAsync(async () =>
            {
                var secondAddress = Node(_specConfig.Second).Address;
                await EnterBarrierAsync("actors-started");

                var subject = await IdentifyAsync(_specConfig.Second, "subject");
                Watch(subject);
                subject.Tell("hello");
                await ExpectMsgAsync("hello");
                await EnterBarrierAsync("watch-established");

                // simulate a hard shutdown, nothing sent from the shutdown node
                await TestConductor.BlackholeAsync(_specConfig.Second, _specConfig.First, ThrottleTransportAdapter.Direction.Send);
                await TestConductor.ShutdownAsync(_specConfig.Second);
                await ExpectTerminatedAsync(subject, TimeSpan.FromSeconds(15));

                var restartedSubject = Sys.ActorSelection(new RootActorPath(secondAddress) / "user" / "subject");

                // PHASE 1 -- reachability, probed with something that costs the target nothing.
                // Identify is safe to repeat; "shutdown" is not, so it must never be the probe used
                // to detect that the restarted system is up.
                //
                // This phase also WARMS second's ordinary outbound lane, which is the real reason
                // the split works. second's association to first was created by an INBOUND
                // handshake, so AssociationState.OutboundHandshakeCompleted is false and the FIRST
                // ordinary send from second would otherwise be held in
                // OutboundHandshakeStage._pendingMessage until first answers a HandshakeRsp. The
                // ActorIdentity reply pays that round trip while second is still alive, so the later
                // shutdown-ack does not have to.
                //
                // 30 s is today's value, not a widening. Measured need here is about 1.5 s; the
                // larger bound covers the same restart-and-rebind pattern that has taken up to
                // 12.9 s on Windows in comparable specs.
                await WithinAsync(TimeSpan.FromSeconds(30), async () =>
                {
                    await AwaitAssertAsync(async () =>
                    {
                        var probe = CreateTestProbe();
                        restartedSubject.Tell(new Identify("restarted"), probe.Ref);
                        var identity = await probe.ExpectMsgAsync<ActorIdentity>(TimeSpan.FromSeconds(1));
                        identity.Subject.Should().NotBeNull(
                            "the fresh system on [{0}] must answer for /user/subject", secondAddress);
                    }, interval: TimeSpan.FromMilliseconds(500));
                });

                // PHASE 2 -- stop it. Retrying is safe now: Subject re-arms its terminate timer on
                // every "shutdown", so a lost ack no longer removes the target for the next attempt.
                // 10 s: on the now-warm lane the ack takes under 1 ms; the only real cost is one
                // stream restart, which is under 2 s worst case.
                await WithinAsync(TimeSpan.FromSeconds(10), async () =>
                {
                    await AwaitAssertAsync(async () =>
                    {
                        var probe = CreateTestProbe();
                        restartedSubject.Tell("shutdown", probe.Ref);
                        await probe.ExpectMsgAsync<string>(msg => msg == "shutdown-ack", TimeSpan.FromSeconds(1));
                    }, interval: TimeSpan.FromMilliseconds(500));
                });
            }, _specConfig.First);

            await RunOnAsync(async () =>
            {
                var addr = Sys.AsInstanceOf<ExtendedActorSystem>().Provider.DefaultAddress;
                Sys.ActorOf(Props.Create(() => new Subject()), "subject");
                await EnterBarrierAsync("actors-started");

                await EnterBarrierAsync("watch-established");
                await Sys.WhenTerminated.WaitAsync(TimeSpan.FromSeconds(30));

                // Pin the fresh system to the SAME wire address for BOTH transports. Under
                // AKKA_MNTR_TRANSPORT=artery the inherited config carries `canonical.port = 0`
                // (MultiNodeSpec.SelfPort defaults to 0 -> random port), so without the explicit
                // artery override the restarted system binds a DIFFERENT port and `first` can
                // never re-associate with `secondAddress`. Pekko's version of this spec pins both
                // ports the same way (RemoteNodeRestartDeathWatchSpec.scala's freshSystem config).
                var sb = new StringBuilder().AppendLine("akka.remote.dot-netty.tcp {").AppendLine("hostname = " + addr.Host)
                        .AppendLine("port = " + addr.Port)
                        .AppendLine("}")
                        .AppendLine("akka.remote.artery.canonical.hostname = " + addr.Host)
                        .AppendLine("akka.remote.artery.canonical.port = " + addr.Port);
                var freshSystem = ActorSystem.Create(Sys.Name,
                    ConfigurationFactory.ParseString(sb.ToString()).WithFallback(Sys.Settings.Config));
                freshSystem.ActorOf(Props.Create(() => new Subject()), "subject");

                await freshSystem.WhenTerminated.WaitAsync(TimeSpan.FromSeconds(30));
            }, _specConfig.Second);
        }

        private sealed class Subject : ActorBase
        {
            private ICancelable? _terminate;

            protected override bool Receive(object message)
            {
                if ("shutdown".Equals(message))
                {
                    Sender.Tell("shutdown-ack");

                    // Do NOT terminate inline. ActorSystem.Terminate() stops the /user guardian, and
                    // Artery's stream materializer supervisor is a /user actor, so every outbound
                    // stream -- with shutdown-ack still inside it -- can abort before the ack ever
                    // reaches the socket. Sliding the terminate behind a timer gives the ack a live
                    // system, and the surrounding warmed-up lane, to leave from.
                    //
                    // Sliding, not one-shot: `first` retries "shutdown" until it sees the ack, so the
                    // target must survive a retry. 5 s, not 3 s: the worst retry cycle is a 1 s
                    // expect + a 500 ms interval + a 2 s stream restart = 3.5 s, and a shorter slide
                    // can terminate the system mid-retry and reintroduce the same failure.
                    _terminate?.Cancel();
                    var system = Context.System;
                    _terminate = system.Scheduler.Advanced.ScheduleOnceCancelable(
                        TimeSpan.FromSeconds(5), () => system.Terminate());
                }
                else
                {
                    Sender.Tell(message);
                }
                return true;
            }

            protected override void PostStop()
            {
                // The sliding timer above re-arms on every "shutdown" and is otherwise left
                // running. Cancel it here so a stopped Subject can't fire it later and terminate
                // whatever ActorSystem happens to own it at that point.
                _terminate?.Cancel();
                base.PostStop();
            }
        }
    }

    #region Config

    public class RemoteNodeRestartDeathWatchSpecConfig : MultiNodeConfig
    {
        public RemoteNodeRestartDeathWatchSpecConfig()
        {
            First = Role("first");
            Second = Role("second");

            CommonConfig = DebugConfig(false).WithFallback(ConfigurationFactory.ParseString(
                @"akka.loglevel = INFO
                  akka.remote.log-remote-lifecycle-events = off
                   akka.remote.transport-failure-detector.heartbeat-interval = 1 s
            akka.remote.transport-failure-detector.acceptable-heartbeat-pause = 3 s
            akka.remote.retry-gate-closed-for = 1s"
            ));
            TestTransport = true;
        }

        public RoleName First { get; }
        public RoleName Second { get; }
    }

    #endregion
}
