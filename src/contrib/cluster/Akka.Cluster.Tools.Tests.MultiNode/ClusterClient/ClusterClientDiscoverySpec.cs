//-----------------------------------------------------------------------
// <copyright file="ClusterClientDiscoverySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Cluster.Tools.Client;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Akka.Discovery;
using Akka.Discovery.Config;
using Akka.Event;
using Akka.Management.Dsl;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.TestKit.TestActors;
using FluentAssertions;
using FluentAssertions.Extensions;

namespace Akka.Cluster.Tools.Tests.MultiNode.Client;

public sealed class ClusterClientDiscoverySpecConfig : MultiNodeConfig
{
    public static readonly int[] HttpPorts = [30001, 30002, 30003];
        
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
                                                        akka.loglevel = DEBUG
                                                        akka.actor.provider = cluster

                                                        akka.remote.dot-netty.tcp.hostname = localhost
                                                        akka.remote.dot-netty.tcp.port = 0
                                                        akka.remote.log-remote-lifecycle-events = off

                                                        akka.management.http.hostname = "127.0.0.1"

                                                        akka.cluster.client {
                                                          heartbeat-interval = 1d
                                                          acceptable-heartbeat-pause = 1d
                                                          reconnect-timeout = 1s
                                                          refresh-contacts-interval = 1d
                                                        }
                                                        akka.test.filter-leeway = 10s
                                                        """)
            .WithFallback(ClusterClientReceptionist.DefaultConfig())
            .WithFallback(DistributedPubSub.DefaultConfig())
            .WithFallback(MultiNodeClusterSpec.ClusterConfig());
            
        NodeConfig([First], [ConfigurationFactory.ParseString($"akka.management.http.port = {HttpPorts[0]}")]);
        NodeConfig([Second], [ConfigurationFactory.ParseString($"akka.management.http.port = {HttpPorts[1]}")]);
        NodeConfig([Third], [ConfigurationFactory.ParseString($"akka.management.http.port = {HttpPorts[2]}")]);
            
        NodeConfig([Client],
        [
            ConfigurationFactory.ParseString("""
                                             akka {
                                               cluster.client {
                                                 heartbeat-interval = 1s
                                                 acceptable-heartbeat-pause = 2s
                                                 use-initial-contacts-discovery = true
                                                 reconnect-timeout = 3s
                                                 verbose-logging = true
                                                 discovery
                                                 {
                                                   service-name = test-cluster
                                                   probe-timeout = 1s
                                                 }
                                               }

                                               discovery
                                               {
                                                 method = config
                                                 config.services.test-cluster.endpoints = []
                                               }
                                             }
                                             """)
        ]);
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

    private async Task Join(RoleName from, RoleName to)
    {
        RunOn(() =>
        {
            Cluster.Join(Node(to).Address);
            ClusterClientReceptionist.Get(Sys);
        }, from);
        await EnterBarrierAsync(from.Name + "-joined");
    }

    private IActorRef _clusterClient = null;

    [MultiNodeFact]
    public async Task ClusterClientDiscoverySpecs()
    {
        await ClusterClient_must_startup_cluster_with_single_node();
        await ClusterClient_must_establish_connection_to_first_node();
        await ClusterClient_must_down_existing_cluster();
        await ClusterClient_second_node_must_form_a_new_cluster();
        await ClusterClient_must_re_establish_on_cluster_restart();
        await ClusterClient_must_simulate_a_cluster_forced_shutdown();
        await ClusterClient_third_node_formed_a_cluster();
        await ClusterClient_must_re_establish_on_cluster_restart_after_hard_shutdown();
    }
        
    private async Task ClusterClient_must_startup_cluster_with_single_node()
    {
        await Join(_config.First, _config.First);
            
        await RunOnAsync(async () =>
        {
            await AkkaManagement.Get(Sys).Start();
                    
            var service = Sys.ActorOf(EchoActor.Props(this), "testService");
            ClusterClientReceptionist.Get(Sys).RegisterService(service);
            await AwaitMembersUpAsync(1);
        }, _config.First);
            
        await EnterBarrierAsync("cluster-started");
                
        await RunOnAsync(async () =>
        {
            _discoveryService =
                (ConfigServiceDiscovery)Discovery.Discovery.Get(Sys).LoadServiceDiscovery("config");
            var address = GetAddress(_config.First);
            _discoveryService.TryAddEndpoint("test-cluster", 
                new ServiceDiscovery.ResolvedTarget(address.Host, ClusterClientDiscoverySpecConfig.HttpPorts[0]));
                    
            var resolved = await _discoveryService.Lookup(new Lookup("test-cluster"), TimeSpan.FromSeconds(1));
            resolved.Addresses.Count.Should().Be(1);
        }, _config.Client);
            
        await EnterBarrierAsync("discovery-entry-added");
    }

    private async Task ClusterClient_must_establish_connection_to_first_node()
    {
        await RunOnAsync(async () =>
        {
            _clusterClient = Sys.ActorOf(ClusterClient.Props(ClusterClientSettings.Create(Sys)), "client1");
                    
            await AwaitAssertAsync(async () =>
            {
                _clusterClient.Tell(GetContactPoints.Instance, TestActor);
                var contacts = (await ExpectMsgAsync<ContactPoints>(TimeSpan.FromSeconds(1))).ContactPointsList;
                contacts.Count.Should().Be(1);
                contacts.First().Address.Should().Be((await NodeAsync(_config.First)).Address);
            }, 10.Seconds());
                    
            _clusterClient.Tell(new ClusterClient.Send("/user/testService", "hello", localAffinity:true));
            (await FishForMessageAsync(msg => msg is string)).Should().Be("hello");
        }, _config.Client);
            
        await EnterBarrierAsync("established");
    }

    private async Task ClusterClient_must_down_existing_cluster()
    {
        await RunOnAsync(async () =>
        {
            await AkkaManagement.Get(Sys).Stop();
                
            Cluster.Get(Sys).Leave((await NodeAsync(_config.First)).Address);
        }, _config.First);

        await EnterBarrierAsync("cluster-downed");
            
        await RunOnAsync(async () =>
        {
            var address = GetAddress(_config.First);
            _discoveryService.TryRemoveEndpoint("test-cluster", 
                new ServiceDiscovery.ResolvedTarget(address.Host, ClusterClientDiscoverySpecConfig.HttpPorts[0]));
                
            var resolved = await _discoveryService.Lookup(new Lookup("test-cluster"), TimeSpan.FromSeconds(1));
            resolved.Addresses.Count.Should().Be(0);
        }, _config.Client);
            
        await EnterBarrierAsync("discovery-entry-removed");
            
    }
        
    private async Task ClusterClient_second_node_must_form_a_new_cluster()
    {
        await Join(_config.Second, _config.Second);
        await RunOnAsync(async () =>
        {
            await AkkaManagement.Get(Sys).Start();
                
            var service = Sys.ActorOf(EchoActor.Props(this), "testService");
            ClusterClientReceptionist.Get(Sys).RegisterService(service);
            await AwaitMembersUpAsync(1);
        }, _config.Second);
            
        await EnterBarrierAsync("cluster-restarted");
            
        await RunOnAsync(async () =>
        {
            var address = GetAddress(_config.Second);
            _discoveryService.TryAddEndpoint("test-cluster", 
                new ServiceDiscovery.ResolvedTarget(address.Host, ClusterClientDiscoverySpecConfig.HttpPorts[1]));
                
            var resolved = await _discoveryService.Lookup(new Lookup("test-cluster"), TimeSpan.FromSeconds(1));
            resolved.Addresses.Count.Should().Be(1);
        }, _config.Client);
            
        await EnterBarrierAsync("discovery-entry-updated");
    }

    private async Task ClusterClient_must_re_establish_on_cluster_restart()
    {
        await RunOnAsync(async () =>
        {
            await AwaitAssertAsync(async () =>
            {
                _clusterClient.Tell(GetContactPoints.Instance, TestActor);
                var contacts = (await ExpectMsgAsync<ContactPoints>(TimeSpan.FromSeconds(1))).ContactPointsList;
                contacts.Count.Should().Be(1);
                contacts.First().Address.Should().Be((await NodeAsync(_config.Second)).Address);
            }, 10.Seconds());

            _clusterClient.Tell(new ClusterClient.Send("/user/testService", "hello", localAffinity: true));
            (await FishForMessageAsync(msg => msg is string)).Should().Be("hello");
        }, _config.Client);
            
        await EnterBarrierAsync("re-establish-successful");
    }

    private async Task ClusterClient_must_simulate_a_cluster_forced_shutdown()
    {
        await RunOnAsync(async () =>
        {
            await AkkaManagement.Get(Sys).Stop();

            // simulate a hard shutdown
            await TestConductor.ExitAsync(_config.Second, 0);
        }, _config.Client);
            
        await EnterBarrierAsync("hard-shutdown-and-discovery-entry-updated");
    }

    private async Task ClusterClient_third_node_formed_a_cluster()
    {
        await Join(_config.Third, _config.Third);
        await RunOnAsync(async () =>
        {
            await AkkaManagement.Get(Sys).Start();
                
            var service = Sys.ActorOf(EchoActor.Props(this), "testService");
            ClusterClientReceptionist.Get(Sys).RegisterService(service);
            await AwaitMembersUpAsync(1);
        }, _config.Third);
            
        await EnterBarrierAsync("cluster-restarted");
            
        await RunOnAsync(async () =>
        {
            var address = GetAddress(_config.Third);
            _discoveryService.TryAddEndpoint("test-cluster", 
                new ServiceDiscovery.ResolvedTarget(address.Host, ClusterClientDiscoverySpecConfig.HttpPorts[2]));
                
            var resolved = await _discoveryService.Lookup(new Lookup("test-cluster"), TimeSpan.FromSeconds(1));
            resolved.Addresses.Count.Should().Be(2);
        }, _config.Client);
            
        await EnterBarrierAsync("discovery-entry-updated");
    }
        
    private async Task ClusterClient_must_re_establish_on_cluster_restart_after_hard_shutdown()
    {
        await RunOnAsync(async () =>
        {
            await AwaitAssertAsync(async () =>
            {
                _clusterClient.Tell(GetContactPoints.Instance, TestActor);
                var contacts = (await ExpectMsgAsync<ContactPoints>(TimeSpan.FromSeconds(1))).ContactPointsList;
                contacts.Count.Should().Be(1);
                contacts.First().Address.Should().Be((await NodeAsync(_config.Third)).Address);
            }, 20.Seconds());

            _clusterClient.Tell(new ClusterClient.Send("/user/testService", "hello", localAffinity: true));
            (await FishForMessageAsync(msg => msg is string)).Should().Be("hello");
        }, _config.Client);
            
        await EnterBarrierAsync("re-establish-successful");
    }

}