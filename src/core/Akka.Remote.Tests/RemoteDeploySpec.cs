//-----------------------------------------------------------------------
// <copyright file="RemoteDeploySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

﻿using System.Linq;
using Akka.Actor;
using Akka.Routing;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Remote.Tests
{
    public class RemoteDeploySpec : AkkaSpec
    {
        private ActorSystem _remoteSystem;
        private Address _remoteAddress;

        public RemoteDeploySpec()
            : base(@"
            akka {
                loglevel = INFO 
                log-dead-letters-during-shutdown = false
              //  actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                remote.helios.tcp = {
                    hostname = localhost
                    port = 0
                }

                actor.deployment {
                  /router1 {
                    router = round-robin-pool
                    nr-of-instances = 3
                  }
                  /router2 {
                    router = round-robin-pool
                    nr-of-instances = 3
                  }
                  /router3 {
                    router = round-robin-pool
                    nr-of-instances = 0
                  }
                }
            }
")
        {
            _remoteSystem = ActorSystem.Create("RemoteSystem", Sys.Settings.Config);
            _remoteAddress = _remoteSystem.AsInstanceOf<ExtendedActorSystem>().Provider.DefaultAddress;
            var remoteAddressUid = AddressUidExtension.Uid(_remoteSystem);


        }


        [Fact]
        public void Router_in_general_must_use_configured_nr_of_instances_when_FromConfig()
        {
            var router = Sys.ActorOf(Props.Create<BlackHoleActor>().WithRouter(FromConfig.Instance), "router1");

            router.Tell(new GetRoutees(), TestActor);
            ExpectMsg<Routees>().Members.Count().ShouldBe(3);
            Watch(router);
            Sys.Stop(router);
            ExpectTerminated(router);
        }


        [Fact]
        public void Router_in_general_must_not_use_configured_nr_of_instances_when_not_FromConfig()
        {
            var router = Sys.ActorOf(Props.Create<BlackHoleActor>(), "router1");

            router.Tell(new GetRoutees(), TestActor);
            ExpectNoMsg();
        }
    }
}
