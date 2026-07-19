//-----------------------------------------------------------------------
// <copyright file="RemoteRestartedQuarantinedSpec.cs" company="Akka.NET Project">
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
using FluentAssertions;

namespace Akka.Remote.Tests.MultiNode
{
    public class RemoteRestartedQuarantinedMultiNetSpec : MultiNodeConfig
    {
        public RemoteRestartedQuarantinedMultiNetSpec()
        {
            First = Role("first");
            Second = Role("second");

            CommonConfig = DebugConfig(false).WithFallback(ConfigurationFactory.ParseString(@"
              akka.loglevel = WARNING
              akka.remote.log-remote-lifecycle-events = WARNING

              # Keep it long, we don't want reconnects
              akka.remote.retry-gate-closed-for  = 1 s

              # Important, otherwise it is very racy to get a non-writing endpoint: the only way to do it if the two nodes
              # associate to each other at the same time. Setting this will ensure that the right scenario happens.
              akka.remote.use-passive-connections = off

              # TODO should not be needed, but see TODO at the end of the test
              akka.remote.transport-failure-detector.heartbeat-interval = 1 s
              akka.remote.transport-failure-detector.acceptable-heartbeat-pause = 10 s
            "));

            TestTransport = true;
        }

        public RoleName First { get; }
        public RoleName Second { get; }

        public sealed class Subject : ReceiveActor
        {
            public Subject()
            {
                Receive<string>(_ => Context.System.Terminate(), s => "shutdown".Equals(s));
                Receive<string>(
                    _ => Sender.Tell((AddressUidExtension.Uid(Context.System), Self)),
                    s => "identify".Equals(s));
            }
        }
    }

    public class RemoteRestartedQuarantinedSpec : MultiNodeSpec
    {
        private readonly RemoteRestartedQuarantinedMultiNetSpec _config;
        private readonly Func<RoleName, string, Task<(long, IActorRef)>> _identifyWithUid;

        public RemoteRestartedQuarantinedSpec()
            : this(new RemoteRestartedQuarantinedMultiNetSpec())
        {
        }

        protected RemoteRestartedQuarantinedSpec(RemoteRestartedQuarantinedMultiNetSpec config)
            : base(config, typeof(RemoteRestartedQuarantinedSpec))
        {
            _config = config;

            _identifyWithUid = async (role, actorName) =>
            {
                Sys.ActorSelection(Node(role) / "user" / actorName).Tell("identify");
                return await ExpectMsgAsync<(long, IActorRef)>();
            };
        }

        protected override int InitialParticipantsValueFactory { get; } = 2;

        [MultiNodeFact]
        public async Task A_restarted_quarantined_system_should_not_crash_the_other_system()
        {
            Sys.ActorOf<RemoteRestartedQuarantinedMultiNetSpec.Subject>("subject");
            await EnterBarrierAsync("subject-started");

            await RunOnAsync(async () =>
            {
                var secondAddress = Node(_config.Second).Address;
                var uid = (await _identifyWithUid(_config.Second, "subject")).Item1;

                // Pekko's artery variant inserts this barrier between the identify exchanges and
                // the Quarantine() call: without it, `second`'s own identify of `first`'s subject
                // can still be in flight when the quarantine lands, and (with inbound quarantine
                // enforcement) `first` then DROPS that identify -- `second` receives the
                // ThisActorSystemQuarantinedEvent instead of its expected (uid, ref) tuple.
                await EnterBarrierAsync("before-quarantined");

                RARP.For(Sys).Provider.Transport.Quarantine(Node(_config.Second).Address, uid);

                await EnterBarrierAsync("quarantined");
                await EnterBarrierAsync("still-quarantined");

                await TestConductor.ShutdownAsync(_config.Second);

                await WithinAsync(TimeSpan.FromSeconds(30), async () =>
                {
                    await AwaitAssertAsync(() =>
                    {
                        Sys.ActorSelection(new RootActorPath(secondAddress)/"user"/"subject")
                            .Tell(new Identify("subject"));
                        ExpectMsg<ActorIdentity>(i => i.Subject != null, TimeSpan.FromSeconds(10));
                    });
                });

                Sys.ActorSelection(new RootActorPath(secondAddress) / "user" / "subject").Tell("shutdown");
            }, _config.First);

            await RunOnAsync(async () =>
            {
                var addr = ((ExtendedActorSystem) Sys).Provider.DefaultAddress;
                var firstAddress = Node(_config.First).Address;
                Sys.EventStream.Subscribe(TestActor, typeof (ThisActorSystemQuarantinedEvent));

                var actorRef = (await _identifyWithUid(_config.First, "subject")).Item2;

                // See the matching barrier in `first`'s block (Pekko artery-variant parity).
                await EnterBarrierAsync("before-quarantined");

                await EnterBarrierAsync("quarantined");

                // Check that quarantine is intact. Classic and artery signal this differently:
                // classic's Endpoint only discovers/logs the quarantine reactively, in response to
                // ITS OWN attempt to write ("The remote system has quarantined this system" --
                // Endpoint.cs's InvalidAssociation text), so a Tell is needed to trigger it. Artery
                // has no such log line -- the quarantining side proactively (and, since the
                // InboundQuarantineCheck port, reactively per dropped message too) pushes an
                // ArteryQuarantined control message straight to the quarantined peer, which
                // publishes ThisActorSystemQuarantinedEvent on arrival with no Tell required --
                // matches Pekko's artery variant of this spec (remote-tests/.../artery/
                // RemoteRestartedQuarantinedSpec.scala), which goes straight to its
                // ThisActorSystemQuarantinedEvent expectation with no intervening send.
                if (Sys.Settings.Config.GetBoolean("akka.remote.artery.enabled", false))
                {
                    await ExpectMsgAsync<ThisActorSystemQuarantinedEvent>(TimeSpan.FromSeconds(10));
                }
                else
                {
                    await WithinAsync(TimeSpan.FromSeconds(30), async () =>
                    {
                        await AwaitAssertAsync(() =>
                        {
                            EventFilter.Warning(null, null, "The remote system has quarantined this system")
                                .ExpectOne(TimeSpan.FromSeconds(10), () => actorRef.Tell("boo!"));
                        });
                    });

                    await ExpectMsgAsync<ThisActorSystemQuarantinedEvent>(TimeSpan.FromSeconds(10));
                }

                await EnterBarrierAsync("still-quarantined");

                await Sys.WhenTerminated.WaitAsync(TimeSpan.FromSeconds(10));

                // Pin BOTH transports' host/port so the fresh incarnation comes back at the SAME
                // address regardless of which transport the run uses (classic ignores the artery
                // keys and vice versa) -- mirrors Pekko's artery RemoteRestartedQuarantinedSpec,
                // whose fresh system pins artery.canonical.port. Without the artery pin the
                // MultiNodeSpec-injected artery tier's canonical.port = 0 wins under
                // AKKA_MNTR_TRANSPORT=artery and the restarted system binds a random port `first`
                // can never reach.
                var sb = new StringBuilder()
                    .AppendLine("akka.remote.retry-gate-closed-for = 0.5 s")
                    .AppendLine("akka.remote.dot-netty.tcp {")
                    .AppendLine("hostname = " + addr.Host)
                    .AppendLine("port = " + addr.Port)
                    .AppendLine("}")
                    .AppendLine("akka.remote.artery.canonical.hostname = " + addr.Host)
                    .AppendLine("akka.remote.artery.canonical.port = " + addr.Port);
                var freshSystem = ActorSystem.Create(Sys.Name,
                    ConfigurationFactory.ParseString(sb.ToString()).WithFallback(Sys.Settings.Config));

                var probe = CreateTestProbe(freshSystem);

                freshSystem.ActorSelection(new RootActorPath(firstAddress)/"user"/"subject")
                    .Tell(new Identify("subject"), probe.Ref);

                // TODO sometimes it takes long time until the new connection is established,
                //      It seems like there must first be a transport failure detector timeout, that triggers
                //      "No response from remote. Handshake timed out or transport failure detector triggered"
                await probe.ExpectMsgAsync<ActorIdentity>(i => i.Subject != null, TimeSpan.FromSeconds(30));

                freshSystem.ActorOf<RemoteRestartedQuarantinedMultiNetSpec.Subject>("subject");

                await freshSystem.WhenTerminated.WaitAsync(TimeSpan.FromSeconds(10));
            }, _config.Second);
        }
    }
}
