//-----------------------------------------------------------------------
// <copyright file="NewRemoteActorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.TestKit;
using Xunit;

namespace Akka.Remote.Tests.MultiNode
{
    public class NewRemoteActorMultiNodeSpecConfig : MultiNodeConfig
    {
        #region Internal actor classes

        public class SomeActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                if (message.Equals("identify"))
                    Sender.Tell(Self);
            }
        }

        public class SomeActorWithParam : UntypedActor
        {
            private string _ignored;

            public SomeActorWithParam(string ignored)
            {
                _ignored = ignored;
            }


            protected override void OnReceive(object message)
            {
                if (message.Equals("identify"))
                    Sender.Tell(Self);
            }
        }

        #endregion

        public RoleName Master { get; }
        public RoleName Slave { get; }

        public NewRemoteActorMultiNodeSpecConfig()
        {
            Master = Role("master");
            Slave = Role("slave");

            CommonConfig =
                DebugConfig(false)
                    .WithFallback(ConfigurationFactory.ParseString("akka.remote.log-remote-lifecycle-events = off"));

            DeployOn(Master, @"
                /service-hello.remote = ""@slave@""
                /service-hello-null.remote = ""@slave@""
                /service-hello3.remote = ""@slave@""
            ");

            DeployOnAll(@"/service-hello2.remote = ""@slave@""");
        }
    }

    public class NewRemoteActorSpec : MultiNodeSpec
    {
        private NewRemoteActorMultiNodeSpecConfig _config;

        public NewRemoteActorSpec()
            : this(new NewRemoteActorMultiNodeSpecConfig())
        {
        }

        protected NewRemoteActorSpec(NewRemoteActorMultiNodeSpecConfig config) : base(config, typeof(NewRemoteActorSpec))
        {
            _config = config;
        }

        protected override int InitialParticipantsValueFactory
        {
            get { return Roles.Count; }
        }

        protected override bool VerifySystemShutdown
        {
            get { return true; }
        }

        [MultiNodeFact]
        public void NewRemoteActorSpecs()
        {
            A_new_remote_actor_must_be_locally_instantiated_on_a_remote_node_and_be_able_to_communicate_through_its_remote_actor_ref();
            A_new_remote_actor_must_be_locally_instantiated_on_a_remote_node_with_null_parameter_and_be_able_to_communicate_through_its_remote_actor_ref();
            A_new_remote_actor_must_be_able_to_shutdown_system_when_using_remote_deployed_actor();
        }
        
        public void A_new_remote_actor_must_be_locally_instantiated_on_a_remote_node_and_be_able_to_communicate_through_its_remote_actor_ref()
        {
            RunOn(() =>
            {
                var actor = Sys.ActorOf(Props.Create(() => new NewRemoteActorMultiNodeSpecConfig.SomeActor()),
                    "service-hello");
                var foo = Assert.IsType<RemoteActorRef>(actor);
                actor.Path.Address.ShouldBe(Node(_config.Slave).Address);

                var slaveAddress = TestConductor.GetAddressFor(_config.Slave).Result;
                actor.Tell("identify");
                ExpectMsg<IActorRef>().Path.Address.ShouldBe(slaveAddress);
            }, _config.Master);

            EnterBarrier("done");
        }

        public void A_new_remote_actor_must_be_locally_instantiated_on_a_remote_node_with_null_parameter_and_be_able_to_communicate_through_its_remote_actor_ref()
        {
            RunOn(() =>
            {
                var actor = Sys.ActorOf(Props.Create(() => new NewRemoteActorMultiNodeSpecConfig.SomeActorWithParam(null)),
                    "service-hello2");
                var foo = Assert.IsType<RemoteActorRef>(actor);
                actor.Path.Address.ShouldBe(Node(_config.Slave).Address);

                var slaveAddress = TestConductor.GetAddressFor(_config.Slave).Result;
                actor.Tell("identify");
                ExpectMsg<IActorRef>().Path.Address.ShouldBe(slaveAddress);
            }, _config.Master);

            EnterBarrier("done");
        }

        public void A_new_remote_actor_must_be_able_to_shutdown_system_when_using_remote_deployed_actor()
        {
            Within(TimeSpan.FromSeconds(20), () =>
            {
                RunOn(() =>
                {
                    var actor = Sys.ActorOf(Props.Create(() => new NewRemoteActorMultiNodeSpecConfig.SomeActor()),
                        "service-hello3");
                    var foo = Assert.IsType<RemoteActorRef>(actor);
                    actor.Path.Address.ShouldBe(Node(_config.Slave).Address);

                    // This watch is in race with the shutdown of the watched system. This race should remain, as the test should
                    // handle both cases:
                    //  - remote system receives watch, replies with DeathWatchNotification
                    //  - remote system never gets watch, but DeathWatch heartbeats time out, and AddressTerminated is generated
                    //    (this needs some time to happen)
                    Watch(actor);
                    EnterBarrier("deployed");

                    // master system is supposed to be shutdown after slave
                    // this should be triggered by slave system shutdown
                    ExpectTerminated(actor);
                }, _config.Master);

                RunOn(() =>
                {
                    EnterBarrier("deployed");
                }, _config.Slave);

                // Important that this is the last test.
                // It should not be any barriers here.
                // verifySystemShutdown = true will ensure that system shutdown is successful
                VerifySystemShutdown.ShouldBeTrue("Shutdown should be verified!");
            });
        }
    }
}
