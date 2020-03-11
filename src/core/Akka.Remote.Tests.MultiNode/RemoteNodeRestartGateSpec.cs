//-----------------------------------------------------------------------
// <copyright file="RemoteNodeRestartGateSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Text;
using System.Text.RegularExpressions;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Akka.TestKit;
using Akka.Util.Internal;

namespace Akka.Remote.Tests.MultiNode
{

    #region Spec

    public class RemoteNodeRestartGateSpec : MultiNodeSpec
    {
        private readonly RemoteNodeRestartGateSpecConfig _specConfig;

        public RemoteNodeRestartGateSpec()
            : this(new RemoteNodeRestartGateSpecConfig())
        {
        }

        protected RemoteNodeRestartGateSpec(RemoteNodeRestartGateSpecConfig specConfig)
            : base(specConfig, typeof(RemoteNodeRestartGateSpec))
        {
            _specConfig = specConfig;
        }

        protected override int InitialParticipantsValueFactory
        {
            get { return 2; }
        }

        private IActorRef Identify(RoleName role, string actorName)
        {
            Sys.ActorSelection(Node(role) / "user" / actorName).Tell(new Identify(actorName));
            return ExpectMsg<ActorIdentity>().Subject;
        }


        [MultiNodeFact]
        public void RemoteNodeRestart_must_allow_restarted_node_to_pass_through_gate()
        {
            Sys.ActorOf(Props.Create(() => new Subject()), "subject");
            EnterBarrier("subject-started");

            RunOn(() =>
            {
                var secondAddress = Node(_specConfig.Second).Address;

                Identify(_specConfig.Second, "subject");

                EventFilter.Warning(new Regex("address is now gated")).ExpectOne(() =>
                {
                    RARP.For(Sys).Provider.Transport.ManagementCommand(
                            new ForceDisassociateExplicitly(Node(_specConfig.Second).Address, DisassociateInfo.Unknown))
                        .Wait(TimeSpan.FromSeconds(3));
                });


                EnterBarrier("gated");
                TestConductor.Shutdown(_specConfig.Second).Wait();
                Within(TimeSpan.FromSeconds(10), () =>
                {
                    AwaitAssert(
                        () =>
                        {
                            Sys.ActorSelection(new RootActorPath(secondAddress) / "user" / "subject")
                                .Tell(new Identify("subject"));
                            ExpectMsg<ActorIdentity>().Subject.ShouldNotBe(null);
                        });
                });
                Sys.ActorSelection(new RootActorPath(secondAddress) / "user" / "subject").Tell("shutdown");
            }, _specConfig.First);

            RunOn(() =>
            {
                var addr = Sys.AsInstanceOf<ExtendedActorSystem>().Provider.DefaultAddress;
                var firstAddress = Node(_specConfig.First).Address;

                EnterBarrier("gated");

                Sys.WhenTerminated.Wait(TimeSpan.FromSeconds(10));

                var sb = new StringBuilder();
                sb.AppendLine("akka.remote.retry-gate-closed-for = 0.5s")
                    .AppendLine(" akka.remote.dot-netty.tcp {")
                    .AppendLine("hostname = " + addr.Host)
                    .AppendLine("port =" + addr.Port)
                    .AppendLine("}");

                var freshSystem = ActorSystem.Create(Sys.Name, ConfigurationFactory.ParseString(sb.ToString())
                    .WithFallback(Sys.Settings.Config));

                var probe = CreateTestProbe(freshSystem);

                // Pierce the gate
                Within(TimeSpan.FromSeconds(30), () =>
                {
                    AwaitAssert(() =>

                    {
                        freshSystem.ActorSelection(new RootActorPath(firstAddress) / "user" / "subject")
                            .Tell(new Identify("subject"), probe);
                        probe.ExpectMsg<ActorIdentity>();
                    });
                });

                // Now the other system will be able to pass, too
                freshSystem.ActorOf(Props.Create(() => new Subject()), "subject");
                freshSystem.WhenTerminated.Wait(TimeSpan.FromSeconds(30));
            }, _specConfig.Second);
        }

        private class Subject : ActorBase
        {
            protected override bool Receive(object message)
            {
                if ("shutdown".Equals(message))
                {
                    Context.System.Terminate();
                }
                else
                {
                    Sender.Tell(message);
                }

                return true;
            }
        }
    }

    #endregion

    #region Config

    public class RemoteNodeRestartGateSpecConfig : MultiNodeConfig
    {
        public RemoteNodeRestartGateSpecConfig()
        {
            First = Role("first");
            Second = Role("second");

            CommonConfig = DebugConfig(false).WithFallback(ConfigurationFactory.ParseString(
                @"akka.loglevel = INFO
                   akka.remote.log-remote-lifecycle-events = INFO
                   akka.remote.retry-gate-closed-for  = 1d"
            ));
            TestTransport = true;
        }

        public RoleName First { get; }
        public RoleName Second { get; }
    }

    #endregion
}
