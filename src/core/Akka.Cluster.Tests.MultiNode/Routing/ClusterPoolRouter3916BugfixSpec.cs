//-----------------------------------------------------------------------
// <copyright file="ClusterPoolRouter3916BugfixSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Cluster.Routing;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Routing;

namespace Akka.Cluster.Tests.MultiNode.Routing
{
    public class ClusterPoolRouter3916BugfixSpecConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }

        public ClusterPoolRouter3916BugfixSpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = DebugConfig(false)
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());

            NodeConfig(new List<RoleName> { First }, new List<Config> { ConfigurationFactory.ParseString(@"akka.cluster.roles =[""a""]") });
            NodeConfig(new List<RoleName> { Second, Third }, new List<Config> { ConfigurationFactory.ParseString(@"akka.cluster.roles =[""b""]") });
        }
    }

    public class ClusterPoolRouter3916BugfixSpec : MultiNodeClusterSpec
    {
        private readonly ClusterPoolRouter3916BugfixSpecConfig _config;

        public ClusterPoolRouter3916BugfixSpec() : this(new ClusterPoolRouter3916BugfixSpecConfig())
        {
        }

        protected ClusterPoolRouter3916BugfixSpec(ClusterPoolRouter3916BugfixSpecConfig config)
            : base(config, typeof(ClusterPoolRouter3916BugfixSpec))
        {
            _config = config;
        }

        internal class EchoActor : ReceiveActor
        {
            public EchoActor()
            {
                ReceiveAny(_ => Sender.Tell(_));
            }
        }

        [MultiNodeFact]
        public void PoolRouteeSpecs()
        {
            Must_have_routees_at_startup();
        }

        private void Must_have_routees_at_startup()
        {
            AwaitClusterUp(_config.First, _config.Second, _config.Third);

            Within(TimeSpan.FromSeconds(20), () =>
            {
                RunOn(() =>
                {
                    var pool = new RoundRobinPool(10);
                    var routerPoolSettings = new ClusterRouterPoolSettings(1000, 1, true, "b");
                    var config = new ClusterRouterPool(pool, routerPoolSettings);

                    var router = Sys.ActorOf(Props.Create(() => new EchoActor()).WithRouter(config), "myRouter");

                    AwaitAssert(() =>
                    {
                        router.Tell("i");
                        ExpectMsg("i", TimeSpan.FromMilliseconds(100));
                    }, interval:TimeSpan.FromMilliseconds(250));

                }, _config.First);

                EnterBarrier("after-1");
            });
            
        }
    }
}
