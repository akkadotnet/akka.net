namespace Akka.Remote.Tests.MultiNode
{
    using System;

    using Akka.Actor;
    using Akka.Remote.TestKit;

    using Xunit;

    public class LookupRemoteActorMultiNetSpec : MultiNodeConfig
    {
        public RoleName Master { get; private set; }
        public RoleName Slave { get; private set; }

        public LookupRemoteActorMultiNetSpec()
        {
            CommonConfig = DebugConfig(false);

            Master = Role("master");
            Slave = Role("slave");
        }

        public class SomeActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                if (message is Identify)
                {
                    Sender.Tell(Self);
                }
            }
        }
    }

    public class LookupRemoteActorMultiNetNode1 : LookupRemoteActorSpec
    {
    }
    public class LookupRemoteActorMultiNetNode2 : LookupRemoteActorSpec
    {
    }

    public class LookupRemoteActorSpec : MultiNodeSpec
    {
        private LookupRemoteActorMultiNetSpec _config;

        public LookupRemoteActorSpec()
            : this(new LookupRemoteActorMultiNetSpec())
        {

        }
        public LookupRemoteActorSpec(LookupRemoteActorMultiNetSpec config)
            : base(config)
        {
            _config = config;
        }

        protected override int InitialParticipantsValueFactory
        {
            get
            {
                return Roles.Count;
            }
        }

        [MultiNodeFact]
        public void LookupRemoteActorSpecs()
        {
            RunOn(
                () => Sys.ActorOf<NewRemoteActorMultiNodeSpecConfig.SomeActor>("service-hello"),
                _config.Master);

            RemotingMustLookupRemoteActor();
        }

        public void RemotingMustLookupRemoteActor()
        {
            RunOn(
                () =>
                {
                    Sys.ActorSelection(Node(_config.Master) / "user" / "service-hello")
                       .Tell(new Identify("id1"));
                    var hello = ExpectMsg<ActorIdentity>()
                        .Subject;

                    Assert.IsType<RemoteActorRef>(hello);

                    var masterAddress = TestConductor.GetAddressFor(_config.Master).Result;

                    Assert.StrictEqual(masterAddress, hello.Path.Address);
                },
                _config.Slave);

            EnterBarrier("done");
        }
    }
}