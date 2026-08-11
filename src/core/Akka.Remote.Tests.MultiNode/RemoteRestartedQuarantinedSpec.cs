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

                // This barrier separates the identify exchanges from the Quarantine() call.
                //
                // Without it, the identify that `second` sends can still be in flight when the
                // quarantine starts. Inbound quarantine enforcement then makes `first` discard
                // that identify, and `second` receives ThisActorSystemQuarantinedEvent in place of
                // the uid and ref that it expects.
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

                // Test that the quarantine is still in effect. The two transports tell the
                // quarantined system about the quarantine in different ways, thus this test needs
                // two procedures.
                //
                // Classic: the endpoint learns about the quarantine only when it tries to write.
                // It then logs the warning "The remote system has quarantined this system". The
                // test must therefore send a message to cause the write, and then look for that
                // warning.
                //
                // Artery: the quarantining system sends an ArteryQuarantined control message to the
                // peer. The peer publishes ThisActorSystemQuarantinedEvent when the message
                // arrives. No send from the test is necessary, and there is no equivalent warning
                // to look for.
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

                // Set the host and port for both transports, so that the new system starts at the
                // same address as the old one. Each transport ignores the keys of the other, thus
                // it is safe to set both.
                //
                // The artery keys are necessary. MultiNodeSpec adds a config tier that sets
                // artery.canonical.port to 0. Without an explicit port here, that tier wins, the
                // new system binds a random port, and `first` cannot reach it.
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
