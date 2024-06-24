//-----------------------------------------------------------------------
// <copyright file="ClusterClientHandoverSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Cluster.Tools.Client;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Akka.Discovery;
using Akka.Discovery.Config;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.TestKit.TestActors;
using FluentAssertions;

namespace Akka.Cluster.Tools.Tests.MultiNode.Client
{
    public sealed class ClusterClientDiscoverySpecConfig : MultiNodeConfig
    {
        public RoleName Client { get; }
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }

        public ClusterClientDiscoverySpecConfig()
        {
            Client = Role("client");
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = ConfigurationFactory.ParseString("""
akka.loglevel = INFO

akka.remote.dot-netty.tcp.hostname = localhost 
akka.actor.provider = cluster
akka.remote.log-remote-lifecycle-events = off
akka.cluster.client {
  heartbeat-interval = 1d
  acceptable-heartbeat-pause = 1d
  reconnect-timeout = 3s
  refresh-contacts-interval = 1d
}
akka.test.filter-leeway = 10s
""")
                .WithFallback(ClusterClientReceptionist.DefaultConfig())
                .WithFallback(DistributedPubSub.DefaultConfig())
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());
            
            NodeConfig(new[]{ Client }, 
                new []{
                    ConfigurationFactory.ParseString("""
akka {
  cluster.client {
    heartbeat-interval = 1s
    acceptable-heartbeat-pause = 2s
    use-initial-contacts-discovery = true
    reconnect-timeout = 4s
    verbose-logging = true
    discovery
    {
      service-name = test-cluster
      discovery-timeout = 10s
    }
  }
  

  discovery
  {
    method = config
    config.services.test-cluster.endpoints = []
  }
}
""")});
            TestTransport = true;
        }
    }

    public class ClusterClientDiscoverySpec : MultiNodeClusterSpec
    {
        private readonly ClusterClientDiscoverySpecConfig _config;
        private ConfigServiceDiscovery _discoveryService;

        public ClusterClientDiscoverySpec() : this(new ClusterClientDiscoverySpecConfig()) { }

        protected ClusterClientDiscoverySpec(ClusterClientDiscoverySpecConfig config)
            : base(config, typeof(ClusterClientDiscoverySpec))
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

        private IActorRef _clusterClient = null;

        [MultiNodeFact]
        public void ClusterClientDiscoverySpecs()
        {
            ClusterClient_must_startup_cluster_with_single_node();
            ClusterClient_must_establish_connection_to_first_node();
            ClusterClient_must_down_existing_cluster();
            ClusterClient_second_node_must_form_a_new_cluster();
            ClusterClient_must_re_establish_on_cluster_restart();
            ClusterClient_must_simulate_a_cluster_forced_shutdown();
            ClusterClient_third_node_formed_a_cluster();
            ClusterClient_must_re_establish_on_cluster_restart_after_hard_shutdown();
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
                
                RunOn(() =>
                {
                    _discoveryService =
                        (ConfigServiceDiscovery)Discovery.Discovery.Get(Sys).LoadServiceDiscovery("config");
                    var address = GetAddress(_config.First);
                    _discoveryService.TryAddEndpoint("test-cluster", new ServiceDiscovery.ResolvedTarget(address.Host, address.Port));
                    
                    var resolved = _discoveryService.Lookup(new Lookup("test-cluster"), TimeSpan.FromSeconds(1)).Result;
                    resolved.Addresses.Count.Should().Be(1);
                }, _config.Client);
                EnterBarrier("discovery-entry-added");
            });
        }

        private void ClusterClient_must_establish_connection_to_first_node()
        {
            RunOn(() =>
                {
                    _clusterClient = Sys.ActorOf(ClusterClient.Props(ClusterClientSettings.Create(Sys)), "client1");
                    
                    Within(TimeSpan.FromSeconds(5), () =>
                    {
                        AwaitAssert(() =>
                        {
                            _clusterClient.Tell(GetContactPoints.Instance, TestActor);
                            var contacts = ExpectMsg<ContactPoints>(TimeSpan.FromSeconds(1)).ContactPointsList;
                            contacts.Count.Should().Be(1);
                            contacts.First().Address.Should().Be(Node(_config.First).Address);
                        }, RemainingOrDefault);
                    });
                    
                    _clusterClient.Tell(new ClusterClient.Send("/user/testService", "hello", localAffinity:true));
                    ExpectMsg<string>().Should().Be("hello");
                }, _config.Client);
            EnterBarrier("established");
        }

        private void ClusterClient_must_down_existing_cluster()
        {
            RunOn(() =>
            {
                Cluster.Get(Sys).Leave(Node(_config.First).Address);
            }, _config.First);

            EnterBarrier("cluster-downed");
            
            RunOn(() =>
            {
                var address = GetAddress(_config.First);
                _discoveryService.TryRemoveEndpoint("test-cluster", new ServiceDiscovery.ResolvedTarget(address.Host, address.Port));
                
                var resolved = _discoveryService.Lookup(new Lookup("test-cluster"), TimeSpan.FromSeconds(1)).Result;
                resolved.Addresses.Count.Should().Be(0);
            }, _config.Client);
            EnterBarrier("discovery-entry-removed");
            
        }
        
        private void ClusterClient_second_node_must_form_a_new_cluster()
        {
            Join(_config.Second, _config.Second);
            RunOn(() =>
            {
                var service = Sys.ActorOf(EchoActor.Props(this), "testService");
                ClusterClientReceptionist.Get(Sys).RegisterService(service);
                AwaitMembersUp(1);
            }, _config.Second);
            
            EnterBarrier("cluster-restarted");
            
            RunOn(() =>
            {
                var address = GetAddress(_config.Second);
                _discoveryService.TryAddEndpoint("test-cluster", new ServiceDiscovery.ResolvedTarget(address.Host, address.Port));
                
                var resolved = _discoveryService.Lookup(new Lookup("test-cluster"), TimeSpan.FromSeconds(1)).Result;
                resolved.Addresses.Count.Should().Be(1);
            }, _config.Client);
            EnterBarrier("discovery-entry-updated");
        }

        private void ClusterClient_must_re_establish_on_cluster_restart()
        {
            RunOn(() =>
            {
                Within(TimeSpan.FromSeconds(5), () =>
                {
                    AwaitAssert(() =>
                    {
                        _clusterClient.Tell(GetContactPoints.Instance, TestActor);
                        var contacts = ExpectMsg<ContactPoints>(TimeSpan.FromSeconds(1)).ContactPointsList;
                        contacts.Count.Should().Be(1);
                        contacts.First().Address.Should().Be(Node(_config.Second).Address);
                    }, RemainingOrDefault);
                });

                _clusterClient.Tell(new ClusterClient.Send("/user/testService", "hello", localAffinity: true));
                ExpectMsg<string>().Should().Be("hello");
               
            }, _config.Client);
            EnterBarrier("re-establish-successful");
        }

        private void ClusterClient_must_simulate_a_cluster_forced_shutdown()
        {
            RunOn(() =>
            {
                // simulate a hard shutdown
                TestConductor.Exit(_config.Second, 0).Wait();
            }, _config.Client);
            EnterBarrier("hard-shutdown-and-discovery-entry-updated");
        }

        private void ClusterClient_third_node_formed_a_cluster()
        {
            Join(_config.Third, _config.Third);
            RunOn(() =>
            {
                var service = Sys.ActorOf(EchoActor.Props(this), "testService");
                ClusterClientReceptionist.Get(Sys).RegisterService(service);
                AwaitMembersUp(1);
            }, _config.Third);
            
            EnterBarrier("cluster-restarted");
            
            RunOn(() =>
            {
                var address = GetAddress(_config.Third);
                _discoveryService.TryAddEndpoint("test-cluster", new ServiceDiscovery.ResolvedTarget(address.Host, address.Port));
                
                var resolved = _discoveryService.Lookup(new Lookup("test-cluster"), TimeSpan.FromSeconds(1)).Result;
                resolved.Addresses.Count.Should().Be(2);
            }, _config.Client);
            EnterBarrier("discovery-entry-updated");
        }
        
        private void ClusterClient_must_re_establish_on_cluster_restart_after_hard_shutdown()
        {
            RunOn(() =>
            {
                Within(TimeSpan.FromSeconds(20), () =>
                {
                    AwaitAssert(() =>
                    {
                        _clusterClient.Tell(GetContactPoints.Instance, TestActor);
                        var contacts = ExpectMsg<ContactPoints>(TimeSpan.FromSeconds(1)).ContactPointsList;
                        contacts.Count.Should().Be(1);
                        contacts.First().Address.Should().Be(Node(_config.Third).Address);
                    }, TimeSpan.FromSeconds(20));
                });

                _clusterClient.Tell(new ClusterClient.Send("/user/testService", "hello", localAffinity: true));
                ExpectMsg<string>().Should().Be("hello");
               
            }, _config.Client);
            EnterBarrier("re-establish-successful");
        }

    }
}
