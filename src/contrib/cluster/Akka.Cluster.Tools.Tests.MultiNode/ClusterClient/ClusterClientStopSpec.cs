//-----------------------------------------------------------------------
// <copyright file="ClusterClientStopSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Cluster.Tools.Client;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Cluster.Tools.PublishSubscribe.Internal;
using Akka.Configuration;
using Akka.Remote.TestKit;
using FluentAssertions;

namespace Akka.Cluster.Tools.Tests.MultiNode.Client
{
    public class ClusterClientStopSpecConfig : MultiNodeConfig
    {
        public RoleName Client { get; }
        public RoleName First { get; }
        public RoleName Second { get; }

        public ClusterClientStopSpecConfig()
        {
            Client = Role("client");
            First = Role("first");
            Second = Role("second");

            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = INFO
                akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
                akka.remote.log-remote-lifecycle-events = off
                akka.cluster.client {
                  heartbeat-interval = 1s
                  acceptable-heartbeat-pause = 1s
                  reconnect-timeout = 3s
                  receptionist.number-of-contacts = 1
                }
                akka.test.filter-leeway = 10s
            ")
            .WithFallback(ClusterClientReceptionist.DefaultConfig())
            .WithFallback(DistributedPubSub.DefaultConfig());
        }

        public class Service : ReceiveActor
        {
            public Service()
            {
                ReceiveAny(msg => Sender.Tell(msg));
            }
        }
    }

    public class ClusterClientStopSpec : MultiNodeClusterSpec
    {
        private readonly ClusterClientStopSpecConfig _config;

        public ClusterClientStopSpec() : this(new ClusterClientStopSpecConfig())
        {
        }

        protected ClusterClientStopSpec(ClusterClientStopSpecConfig config) : base(config, typeof(ClusterClientStopSpec))
        {
            _config = config;
        }

        protected override int InitialParticipantsValueFactory => 3;

        private void Join(RoleName from, RoleName to)
        {
            RunOn(() =>
            {
                Cluster.Join(Node(to).Address);
                ClusterClientReceptionist.Get(Sys);
            }, from);
            EnterBarrier(from.Name + "-joined");
        }

        private void AwaitCount(int expected)
        {
            AwaitAssert(() =>
            {
                DistributedPubSub.Get(Sys).Mediator.Tell(Count.Instance);
                ExpectMsg<int>().Should().Be(expected);
            });
        }

        private ImmutableHashSet<ActorPath> InitialContacts
        {
            get
            {
                return ImmutableHashSet
                    .Create(_config.First, _config.Second)
                    .Select(r => Node(r) / "system" / "receptionist")
                    .ToImmutableHashSet();
            }
        }

        [MultiNodeFact]
        public void ClusterClientStopSpecs()
        {
            ClusterClient_must_startup_cluster();
            ClusterClient_must_stop_if_reestablish_fails_for_too_long_time();
        }

        public void ClusterClient_must_startup_cluster()
        {
            Within(30.Seconds(), () =>
            {
                Join(_config.First, _config.First);
                Join(_config.Second, _config.First);

                RunOn(() =>
                {
                    var service = Sys.ActorOf(Props.Create(() => new ClusterClientStopSpecConfig.Service()), "testService");
                    ClusterClientReceptionist.Get(Sys).RegisterService(service);
                }, _config.First);

                RunOn(() =>
                {
                    AwaitCount(1);
                }, _config.First, _config.Second);

                EnterBarrier("cluster-started");
            });
        }

        public void ClusterClient_must_stop_if_reestablish_fails_for_too_long_time()
        {
            Within(20.Seconds(), () =>
            {
                RunOn(() =>
                {
                    var c = Sys.ActorOf(ClusterClient.Props(ClusterClientSettings.Create(Sys).WithInitialContacts(InitialContacts)), "client1");
                    c.Tell(new ClusterClient.Send("/user/testService", "hello", localAffinity: true));
                    ExpectMsg<string>(3.Seconds()).Should().Be("hello");
                    EnterBarrier("was-in-contact");
                    Watch(c);

                    // TODO: EventFilter should be after ExpectTerminated
                    EventFilter.Warning(start: "Receptionist reconnect not successful within").ExpectOne(() => { });
                    ExpectTerminated(c, 10.Seconds());
                }, _config.Client);

                RunOn(() =>
                {
                    EnterBarrier("was-in-contact");
                    Sys.Terminate().Wait(10.Seconds());
                }, _config.First, _config.Second);
            });
        }
    }
}
