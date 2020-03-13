//-----------------------------------------------------------------------
// <copyright file="RemoteRestartedQuarantinedSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Text;
using Akka.Actor;
using Akka.Configuration;
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
        private readonly Func<RoleName, string, (int, IActorRef)> _identifyWithUid;

        public RemoteRestartedQuarantinedSpec()
            : this(new RemoteRestartedQuarantinedMultiNetSpec())
        {
        }

        protected RemoteRestartedQuarantinedSpec(RemoteRestartedQuarantinedMultiNetSpec config)
            : base(config, typeof(RemoteRestartedQuarantinedSpec))
        {
            _config = config;

            _identifyWithUid = (role, actorName) =>
            {
                Sys.ActorSelection(Node(role) / "user" / actorName).Tell("identify");
                return ExpectMsg<(int, IActorRef)>();
            };
        }

        protected override int InitialParticipantsValueFactory { get; } = 2;

        [MultiNodeFact]
        public void A_restarted_quarantined_system_should_not_crash_the_other_system()
        {
            Sys.ActorOf<RemoteRestartedQuarantinedMultiNetSpec.Subject>("subject");
            EnterBarrier("subject-started");

            RunOn(() =>
            {
                var secondAddress = Node(_config.Second).Address;
                var uid = _identifyWithUid(_config.Second, "subject").Item1;

                RARP.For(Sys).Provider.Transport.Quarantine(Node(_config.Second).Address, uid);

                EnterBarrier("quarantined");
                EnterBarrier("still-quarantined");

                TestConductor.Shutdown(_config.Second).Wait();

                Within(TimeSpan.FromSeconds(30), () =>
                {
                    AwaitAssert(() =>
                    {
                        Sys.ActorSelection(new RootActorPath(secondAddress)/"user"/"subject")
                            .Tell(new Identify("subject"));
                        ExpectMsg<ActorIdentity>(i => i.Subject != null, TimeSpan.FromSeconds(10));
                    });
                });

                Sys.ActorSelection(new RootActorPath(secondAddress) / "user" / "subject").Tell("shutdown");
            }, _config.First);

            RunOn(() =>
            {
                var addr = ((ExtendedActorSystem) Sys).Provider.DefaultAddress;
                var firstAddress = Node(_config.First).Address;
                Sys.EventStream.Subscribe(TestActor, typeof (ThisActorSystemQuarantinedEvent));

                var actorRef = _identifyWithUid(_config.First, "subject").Item2;

                EnterBarrier("quarantined");

                // Check that quarantine is intact
                Within(TimeSpan.FromSeconds(30), () =>
                {
                    AwaitAssert(() =>
                    {
                        EventFilter.Warning(null, null, "The remote system has quarantined this system")
                            .ExpectOne(10.Seconds(), () => actorRef.Tell("boo!"));
                    });
                });

                ExpectMsg<ThisActorSystemQuarantinedEvent>(TimeSpan.FromSeconds(10));

                EnterBarrier("still-quarantined");

                Sys.WhenTerminated.Wait(TimeSpan.FromSeconds(10));

                var sb = new StringBuilder()
                    .AppendLine("akka.remote.retry-gate-closed-for = 0.5 s")
                    .AppendLine("akka.remote.dot-netty.tcp {")
                    .AppendLine("hostname = " + addr.Host)
                    .AppendLine("port = " + addr.Port)
                    .AppendLine("}");
                var freshSystem = ActorSystem.Create(Sys.Name,
                    ConfigurationFactory.ParseString(sb.ToString()).WithFallback(Sys.Settings.Config));

                var probe = CreateTestProbe(freshSystem);

                freshSystem.ActorSelection(new RootActorPath(firstAddress)/"user"/"subject")
                    .Tell(new Identify("subject"), probe.Ref);

                // TODO sometimes it takes long time until the new connection is established,
                //      It seems like there must first be a transport failure detector timeout, that triggers
                //      "No response from remote. Handshake timed out or transport failure detector triggered"
                probe.ExpectMsg<ActorIdentity>(i => i.Subject != null, TimeSpan.FromSeconds(30));

                freshSystem.ActorOf<RemoteRestartedQuarantinedMultiNetSpec.Subject>("subject");

                freshSystem.WhenTerminated.Wait(TimeSpan.FromSeconds(10));
            }, _config.Second);
        }
    }
}
