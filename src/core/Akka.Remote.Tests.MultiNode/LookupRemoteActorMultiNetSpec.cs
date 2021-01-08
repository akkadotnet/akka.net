//-----------------------------------------------------------------------
// <copyright file="LookupRemoteActorMultiNetSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Remote.TestKit;
using Xunit;

namespace Akka.Remote.Tests.MultiNode
{
    public class LookupRemoteActorMultiNetSpec : MultiNodeConfig
    {
        public RoleName Master { get; }
        public RoleName Slave { get; }

        public LookupRemoteActorMultiNetSpec()
        {
            Master = Role("master");
            Slave = Role("slave");

            CommonConfig = DebugConfig(false);
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

    public class LookupRemoteActorSpec : MultiNodeSpec
    {
        private LookupRemoteActorMultiNetSpec _config;

        public LookupRemoteActorSpec()
            : this(new LookupRemoteActorMultiNetSpec())
        {

        }
        protected LookupRemoteActorSpec(LookupRemoteActorMultiNetSpec config)
            : base(config, typeof(LookupRemoteActorSpec))
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

            Remoting_must_lookup_remote_actor();
        }

        public void Remoting_must_lookup_remote_actor()
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
