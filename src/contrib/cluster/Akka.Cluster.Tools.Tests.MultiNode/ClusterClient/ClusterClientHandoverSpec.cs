//-----------------------------------------------------------------------
// <copyright file="ClusterClientHandoverSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Cluster.Tools.Client;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.TestKit.TestActors;
using FluentAssertions;

namespace Akka.Cluster.Tools.Tests.MultiNode.Client
{
    public sealed class ClusterClientHandoverSpecConfig : MultiNodeConfig
    {
        public RoleName Client { get; }
        public RoleName First { get; }
        public RoleName Second { get; }

        public ClusterClientHandoverSpecConfig()
        {
            Client = Role("client");
            First = Role("first");
            Second = Role("second");

            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = INFO
                akka.actor.provider = cluster
                akka.remote.log-remote-lifecycle-events = off
                akka.cluster.client {
                  heartbeat-interval = 1d
                  acceptable-heartbeat-pause = 1d
                  reconnect-timeout = 3s
                  refresh-contacts-interval = 1d
                }
                akka.test.filter-leeway = 10s
            ")
                .WithFallback(ClusterClientReceptionist.DefaultConfig())
                .WithFallback(DistributedPubSub.DefaultConfig())
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }
    }

    public class ClusterClientHandoverSpec : MultiNodeClusterSpec
    {
        private readonly ClusterClientHandoverSpecConfig _config;

        public ClusterClientHandoverSpec() : this(new ClusterClientHandoverSpecConfig()) { }

        protected ClusterClientHandoverSpec(ClusterClientHandoverSpecConfig config)
            : base(config, typeof(ClusterClientHandoverSpec))
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

        private IActorRef _clusterClient = null;

        [MultiNodeFact]
        public void ClusterClientHandoverSpecs()
        {
            ClusterClient_must_startup_cluster_with_single_node();
            ClusterClient_must_establish_connection_to_first_node();
            ClusterClient_must_bring_second_node_into_cluster();
            ClusterClient_must_remove_first_node_from_cluster();
            ClusterClient_must_re_establish_on_receptionist_shutdown();
        }
      
        private void ClusterClient_must_startup_cluster_with_single_node()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                Join(_config.First, _config.First);
                RunOn(() =>
                {
                    var service = Sys.ActorOf(EchoActor.Props(this), "testService");
                    ClusterClientReceptionist.Get(Sys).RegisterService(service);
                    AwaitMembersUp(1);
                }, _config.First);
                EnterBarrier("cluster-started");
            });
        }

        private void ClusterClient_must_establish_connection_to_first_node()
        {
            RunOn(() =>
                {
                    _clusterClient =
                        Sys.ActorOf(
                            ClusterClient.Props(ClusterClientSettings.Create(Sys).WithInitialContacts(InitialContacts)),
                            "client1");
                    _clusterClient.Tell(new ClusterClient.Send("/user/testService", "hello", localAffinity:true));
                    ExpectMsg<string>().Should().Be("hello");
                }, _config.Client);
            EnterBarrier("established");
        }

        private void ClusterClient_must_bring_second_node_into_cluster()
        {
            Join(_config.Second, _config.First);
            RunOn(() =>
            {
                var service = Sys.ActorOf(EchoActor.Props(this), "testService");
                ClusterClientReceptionist.Get(Sys).RegisterService(service);
                AwaitMembersUp(2);
            }, _config.Second);
            EnterBarrier("second-up");
        }

        private void ClusterClient_must_remove_first_node_from_cluster()
        {
            RunOn(() =>
            {
                Cluster.Get(Sys).Leave(Node(_config.First).Address);
            }, _config.First);

            RunOn(() =>
                {
                    AwaitMembersUp(1);
                }, 
                _config.Second);
            EnterBarrier("handover-done");
        }

        private void ClusterClient_must_re_establish_on_receptionist_shutdown()
        {
            RunOn(() =>
            {
                Within(TimeSpan.FromSeconds(5), () =>
                {
                    AwaitAssert(() =>
                    {
                        // bugfix verification for https://github.com/akkadotnet/akka.net/issues/3840
                        // need to make sure that no dead contacts are hanging around
                        _clusterClient.Tell(GetContactPoints.Instance);
                        var contacts = ExpectMsg<ContactPoints>(TimeSpan.FromSeconds(1)).ContactPointsList;
                        contacts.Select(x => x.Address).Should().Contain(Node(_config.Second).Address);
                    });
                });
               

                _clusterClient.Tell(new ClusterClient.Send("/user/testService", "hello", localAffinity: true));
                ExpectMsg<string>().Should().Be("hello");

               
            }, _config.Client);
            EnterBarrier("handover-successful");
        }
    }
}
