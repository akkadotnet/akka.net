//-----------------------------------------------------------------------
// <copyright file="RemoteGatePiercingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;

namespace Akka.Remote.Tests.MultiNode
{
    public class RemoteGatePiercingMultiNetSpec : MultiNodeConfig
    {
        public RemoteGatePiercingMultiNetSpec()
        {
            First = Role("first");
            Second = Role("second");

            CommonConfig = DebugConfig(false).WithFallback(ConfigurationFactory.ParseString(@"
      akka.loglevel = INFO
      akka.remote.log-remote-lifecycle-events = INFO
      akka.remote.transport-failure-detector.acceptable-heartbeat-pause = 5 
            "));

            NodeConfig(new[] { First }, new[]
            {
                ConfigurationFactory.ParseString("akka.remote.retry-gate-closed-for  = 1 d # Keep it long")
            });
            NodeConfig(new[] { Second }, new[]
            {
                ConfigurationFactory.ParseString("akka.remote.retry-gate-closed-for  = 1 s # Keep it short")
            });

            TestTransport = true;
        }

        public RoleName First { get; }
        public RoleName Second { get; }

        public sealed class Subject : ReceiveActor
        {
            public Subject()
            {
                Receive<string>(_ => Context.System.Terminate(), s => "shutdown".Equals(s));
            }
        }
    }

    public class RemoteGatePiercingSpec : MultiNodeSpec
    {
        private readonly RemoteGatePiercingMultiNetSpec _config;
        private readonly Func<RoleName, string, IActorRef> _identify;

        public RemoteGatePiercingSpec() : this(new RemoteGatePiercingMultiNetSpec())
        {
        }

        protected RemoteGatePiercingSpec(RemoteGatePiercingMultiNetSpec config) : base(config, typeof(RemoteGatePiercingSpec))
        {
            _config = config;

            _identify = (role, actorName) =>
            {
                Sys.ActorSelection(Node(role)/"user"/actorName).Tell(new Identify(actorName));
                return ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(10)).Subject;
            };
        }

        protected override int InitialParticipantsValueFactory { get; } = 2;

        [MultiNodeFact]
        public void RemoteGatePiercing_must_allow_restarted_node_to_pass_through_gate()
        {
            Sys.ActorOf<RemoteGatePiercingMultiNetSpec.Subject>("subject");
            EnterBarrier("actors-started");

            RunOn(() =>
            {
                _identify(_config.Second, "subject");
                EnterBarrier("actors-communicate");

                EventFilter.Warning(null, null, contains: "address is now gated").ExpectOne(() =>
                {
                    var cmd =
                        RARP.For(Sys)
                            .Provider.Transport.ManagementCommand(
                                new ForceDisassociateExplicitly(Node(_config.Second).Address, DisassociateInfo.Unknown));
                    cmd.Wait(TimeSpan.FromSeconds(3));
                });

                EnterBarrier("gated");
                EnterBarrier("gate-pierced");
            }, _config.First);

            RunOn(() =>
            {
                EnterBarrier("actors-communicate");
                EnterBarrier("gated");

                // Pierce the gate
                Within(TimeSpan.FromSeconds(30), () =>
                {
                    AwaitAssert(() => _identify(_config.First, "subject"));
                });

                EnterBarrier("gate-pierced");
            }, _config.Second);
        }
    }
}
