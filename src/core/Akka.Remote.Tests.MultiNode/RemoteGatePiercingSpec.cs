using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Akka.TestKit.Internal;

namespace Akka.Remote.Tests.MultiNode
{
    public class RemoteGatePiercingMultiNetSpec : MultiNodeConfig
    {
        public RemoteGatePiercingMultiNetSpec()
        {
            First = Role("first");
            Second = Role("second");

            CommonConfig = DebugConfig(false);

            NodeConfig(new List<RoleName> {First, Second},
                new List<Config>
                {
                    ConfigurationFactory.ParseString("akka.remote.retry-gate-closed-for  = 1 d # Keep it long"),
                    ConfigurationFactory.ParseString("akka.remote.retry-gate-closed-for  = 1 s # Keep it short")
                });
            TestTransport = true;
        }

        public RoleName First { get; private set; }
        public RoleName Second { get; private set; }

        public class Subject : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                if (message != null && (string) message == "shutdown")
                {
                    Context.System.Shutdown();
                }
            }
        }

        public class RemoteGatePiercingMultiNetMultiNetNode1 : RemoteGatePiercingSpec
        {
        }

        public class RemoteGatePiercingMultiNetMultiNetNode2 : RemoteGatePiercingSpec
        {
        }

        public class RemoteGatePiercingSpec : MultiNodeSpec
        {
            private readonly RemoteDeliveryMultiNetSpec _config;
            private readonly Func<RoleName, string, IActorRef> _identify;

            public RemoteGatePiercingSpec() : this(new RemoteDeliveryMultiNetSpec())
            {
            }

            public RemoteGatePiercingSpec(RemoteDeliveryMultiNetSpec config) : base(config)
            {
                _config = config;
                _identify = (role, actorName) =>
                {
                    Sys.ActorSelection(Node(role)/"user"/actorName).Tell(new Identify(actorName));
                    return ExpectMsg<ActorIdentity>().Subject;
                };
            }

            [MultiNodeFact]
            public void Allow_Restarted_Node_to_pass_through_gate()
            {
                Sys.ActorOf<RemoteGatePiercingMultiNetSpec.Subject>("subject");
                EnterBarrier("actors-started");

                RunOn(() =>
                {
                    var p2 = _identify(_config.Second, "subject");
                    EnterBarrier("actors-communicate");

                    EventFilter.Warning(message: "address is now gated").Expect(1, () =>
                    {
                        AwaitResult(RARP.For(Sys).Provider.Transport.
                            ManagementCommand(new ForceDisassociateExplicitly(Node(_config.Second).Address,
                                DisassociateInfo.Unknown)), TimeSpan.FromSeconds(3));
                    });

                    EnterBarrier("gated");

                    EnterBarrier("gate-pierced");
                }, _config.First);

                RunOn(() =>
                {
                    EnterBarrier("actors-communicate");
                    EnterBarrier("gated");
                    Within(TimeSpan.FromSeconds(30), () => AwaitAssert(() => _identify(_config.First, "subject")));
                    EnterBarrier("gate-pierced");
                }, _config.Second);
            }

            private T AwaitResult<T>(Task<T> task, TimeSpan atMost)
            {
                task.Wait(atMost);

                return task.Result;
            }

            protected override int InitialParticipantsValueFactory
            {
                get { return Roles.Count; }
            }
        }
    }
}
