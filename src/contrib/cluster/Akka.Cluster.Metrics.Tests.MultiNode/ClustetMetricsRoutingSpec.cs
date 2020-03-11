//-----------------------------------------------------------------------
// <copyright file="ClustetMetricsRoutingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Metrics.Configuration;
using Akka.Cluster.Metrics.Serialization;
using Akka.Cluster.Routing;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote.TestKit;
using Akka.Routing;
using FluentAssertions;
using Akka.Configuration;
using Address = Akka.Actor.Address;
using ConfigurationFactory = Akka.Configuration.ConfigurationFactory;

namespace Akka.Cluster.Metrics.Tests.MultiNode
{
    public class AdaptiveLoadBalancingRouterConfig : MultiNodeConfig
    {
        public sealed class AllocateMemory
        {
            private AllocateMemory() { }
            public static readonly AllocateMemory Instance = new AllocateMemory();
        }
        
        [Serializable]
        public sealed class Reply
        {
            public Reply(Address address)
            {
                Address = address;
            }

            public Address Address { get; }
        }
        
        public class Echo : ActorBase
        {
            /// <inheritdoc />
            protected override bool Receive(object message)
            {
                Sender.Tell(new Reply(Cluster.Get(Context.System).SelfAddress));
                return true;
            }
        }

        public class MemoryAllocator : ActorBase
        {
            private int[,] _usedMemory = new int[0, 0];
            private readonly ILoggingAdapter _log = Context.GetLogger();
            
            /// <inheritdoc />
            protected override bool Receive(object message)
            {
                if (!(message is AllocateMemory))
                    return false;

                using (var process = Process.GetCurrentProcess())
                {
                    var available = process.VirtualMemorySize64;
                    var used = GC.GetTotalMemory(true);
                    var max = process.MaxWorkingSet.ToInt64();
                    max = Math.Max(available, max);
                    _log.Info($"Used memory before: [{used}] bytes, of max [{max}]");
                    // allocate 70% of free space
                    var allocateBytes = (long)(0.7 * (max - used));
                    _usedMemory = Allocate(allocateBytes);
                    process.Refresh();
                    _log.Info($"Used memory after: [{GC.GetTotalMemory(true)}] bytes");
                }
                
                Sender.Tell("done");
                return true;
            }

            private int[,] Allocate(long requestedBytes)
            {
                const double reduceFactor = 0.8;
                // Trying to allocate given portion of memory, or at least something close to it
                while (true)
                {
                    _log.Info($"Trying to allocate {requestedBytes} bytes");
                    try
                    {
                        var numberOfArrays = requestedBytes / 1024;
                        return new int[numberOfArrays, 248]; // each 248 element Int array will use ~ 1 kB
                    }
                    catch (OutOfMemoryException)
                    {
                        _log.Info($"Allocation of {requestedBytes} bytes failed, reducing by {reduceFactor}");
                        requestedBytes = (long)(requestedBytes * reduceFactor);
                        if (requestedBytes < 2) // Should not be possible, but just to not have infinite loop
                            return new int[0, 0];
                    }
                }
            }
        }
        
        public readonly RoleName Node1;
        public readonly RoleName Node2;
        public readonly RoleName Node3;
        public IImmutableSet<RoleName> NodeList => ImmutableHashSet.Create(Node1, Node2, Node3);

        public AdaptiveLoadBalancingRouterConfig()
        {
            Node1 = Role("node-1");
            Node2 = Role("node-2");
            Node3 = Role("node-3");
        
            CommonConfig = DebugConfig(on: false)
                .WithFallback(ConfigurationFactory.ParseString(@"
                    # Enable metrics extension in akka-cluster-metrics.
                    akka.extensions=[""Akka.Cluster.Metrics.ClusterMetricsExtensionProvider, Akka.Cluster.Metrics""]

                    akka.cluster.failure-detector.acceptable-heartbeat-pause = 10s

                    # Use rapid metrics collection.
                    akka.cluster.metrics {
                        collector {
                            sample-interval = 1s
                            gossip-interval = 1s
                            moving-average-half-life = 2s
                        }
                    }

                    # Use metrics extension routing.
                    akka.actor.deployment {
                        /router3 = {
                            router = cluster-metrics-adaptive-pool
                            metrics-selector = cpu
                            nr-of-instances = 9
                        }
                        /router4 = {
                            router = cluster-metrics-adaptive-pool
                            metrics-selector = ""Akka.Cluster.Metrics.Tests.MultiNode.TestCustomMetricsSelector, Akka.Cluster.Metrics.Tests.MultiNode""
                            nr-of-instances = 10
                            cluster {
                                enabled = on
                                max-nr-of-instances-per-node = 2
                            }
                        }
                    }
                "))
                .WithFallback(ClusterMetrics.DefaultConfig())
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }
    }

    public class TestCustomMetricsSelector : IMetricsSelector
    {
        public TestCustomMetricsSelector(Config unused) { }
        
        /// <inheritdoc />
        public IImmutableDictionary<Address, int> Weights(IImmutableSet<NodeMetrics> nodeMetrics)
            => ImmutableDictionary<Address, int>.Empty;
    }

    public class AdaptiveLoadBalancingRouterSpec : MultiNodeClusterSpec
    {
        private readonly AdaptiveLoadBalancingRouterConfig _config;
        private readonly ClusterMetricsSettings _metricsSettings;
        
        private AdaptiveLoadBalancingRouterSpec(AdaptiveLoadBalancingRouterConfig config) 
            : base(config, typeof(AdaptiveLoadBalancingRouterSpec))
        {
            _config = config;
            _metricsSettings = ClusterMetricsSettings.Create(Cluster.System.Settings.Config);
        }
        
        public AdaptiveLoadBalancingRouterSpec() 
            : this(new AdaptiveLoadBalancingRouterConfig())
        {
        }

        [MultiNodeFact]
        public async Task Cluster_with_AdaptiveLoadBalancingRouter_spec()
        {
            Should_start_cluster_nodes();
            await Should_use_all_nodes_in_the_cluster_when_not_overloaded();
            
            /*
             * This part of spec has to be ignored since this in MNTR tests all nodes
             * are running on the same machine and share system resources - like memory
             * so allocating memory on one node allocates it on all nodes.
             * 
             * Sometimes it may pass, sometimes not - too racy to keep it running on CI
             */
            //await Should_prefer_node_with_more_free_memory_capacity();
            
            await Should_create_routees_from_configuration();
            await Should_create_routees_from_cluster_enabled_configuration();
        }

        private void Should_start_cluster_nodes()
        {
            AwaitClusterUp(Roles.ToArray());
            EnterBarrier("after-1");
        }

        private async Task Should_use_all_nodes_in_the_cluster_when_not_overloaded()
        {
            await RunOnAsync(async () =>
            {
                var router1 = await StartRouter("router1");
                
                // collect some metrics before we start
                await AwaitMetrics();

                var iterationCount = 100;
                foreach (var i in Enumerable.Range(1, iterationCount))
                {
                    router1.Tell("hit");
                    // wait a while between each message, since metrics is collected periodically
                    await Task.Delay(10);
                }

                var replies = ReceiveReplies(iterationCount);

                replies[Node(_config.Node1).Address].Should().BeGreaterThan(0);
                replies[Node(_config.Node2).Address].Should().BeGreaterThan(0);
                replies[Node(_config.Node3).Address].Should().BeGreaterThan(0);
                replies.Values.Sum().Should().Be(iterationCount);
            }, _config.Node1);
            
            EnterBarrier("after-2");
        }

        private async Task Should_prefer_node_with_more_free_memory_capacity()
        {
            GC.Collect();
            EnterBarrier("gc");
            
            await RunOnAsync(async () =>
            {
                await WithinAsync(20.Seconds(), async () =>
                {
                   Sys.ActorOf(Props.Create<AdaptiveLoadBalancingRouterConfig.MemoryAllocator>(), "memory-allocator")
                       .Tell(AdaptiveLoadBalancingRouterConfig.AllocateMemory.Instance);
                   ExpectMsg("done");
                });
            }, _config.Node2);
            
            EnterBarrier("heap-allocated");

            await RunOnAsync(async () =>
            {
                var router2 = await StartRouter("router2");
                
                // collect some metrics before we start
                await AwaitMetrics();
                
                var iterationCount = 3000;
                foreach (var i in Enumerable.Range(1, iterationCount))
                {
                    router2.Tell("hit");
                }

                var replies = ReceiveReplies(iterationCount);
                replies[Node(_config.Node3).Address].Should().BeGreaterThan(replies[Node(_config.Node2).Address]);
                replies.Values.Sum().Should().Be(iterationCount);
            }, _config.Node1);
            
            EnterBarrier("after-3");
        }

        private async Task Should_create_routees_from_configuration()
        {
            await RunOnAsync(async () =>
            {
                var router3 = Sys.ActorOf(FromConfig.Instance.Props(Props.Create<AdaptiveLoadBalancingRouterConfig.MemoryAllocator>()), "router3");
                // it may take some time until router receives cluster member events
                await AwaitAssertAsync(async () =>
                {
                    var r = await GetCurrentRoutees(router3);
                    r.Count().Should().Be(9);
                });
                var routees = await GetCurrentRoutees(router3);
                routees.Select(r => FullAddress((r as ActorRefRoutee).Actor)).Distinct().Should().BeEquivalentTo(Node(_config.Node1).Address);
            }, _config.Node1);
            
            EnterBarrier("after-4");
        }

        private async Task Should_create_routees_from_cluster_enabled_configuration()
        {
            await RunOnAsync(async () =>
            {
                var router4 = Sys.ActorOf(FromConfig.Instance.Props(Props.Create<AdaptiveLoadBalancingRouterConfig.MemoryAllocator>()), "router4");
                // it may take some time until router receives cluster member events
                await AwaitAssertAsync(async () =>
                {
                    var r = await GetCurrentRoutees(router4);
                    r.Count().Should().Be(6);
                });
                var routees = await GetCurrentRoutees(router4);
                routees
                    .Select(r => FullAddress((r as ActorRefRoutee).Actor)).Distinct()
                    .Should()
                    .BeEquivalentTo(Node(_config.Node1).Address, Node(_config.Node2).Address, Node(_config.Node3).Address);
            }, _config.Node1);
            
            EnterBarrier("after-5");
        }
        
        private Task AwaitMetrics(int factor = 10) => Task.Delay((int)(_metricsSettings.CollectorSampleInterval.TotalMilliseconds * factor));

        private async Task<IActorRef> StartRouter(string name)
        {
            var router = Sys.ActorOf(new ClusterRouterPool(
                new AdaptiveLoadBalancingPool(MemoryMetricsSelector.Instance),
                new ClusterRouterPoolSettings(10, 1, true, null)
            ).Props(Props.Create<AdaptiveLoadBalancingRouterConfig.Echo>()), name);
            
            // it may take some time until router receives cluster member events
            await AwaitAssertAsync(async () =>
            {
                var r = await GetCurrentRoutees(router);
                r.Count.Should().Be(Roles.Count);
            });

            var routees = await GetCurrentRoutees(router);
            routees.Cast<ActorRefRoutee>().Select(r => FullAddress(r.Actor)).Distinct()
                .Should().BeEquivalentTo(Roles.Select(r => Node(r).Address).Distinct());

            return router;
        }
        
        /// <summary>
        /// Fills in self address for local ActorRef
        /// </summary>
        private Address FullAddress(IActorRef actorRef)
        {
            return actorRef.Path.Address.Host == null && actorRef.Path.Address.Port == null
                ? Cluster.SelfAddress
                : actorRef.Path.Address;
        }

        private IImmutableDictionary<Address, int> ReceiveReplies(int expectedReplies)
        {
            var zero = ImmutableDictionary<Address, int>.Empty.AddRange(Roles.ToDictionary(r => Node(r).Address, r => 0));
            return ReceiveWhile(5.Seconds(), msg => (msg as AdaptiveLoadBalancingRouterConfig.Reply)?.Address, expectedReplies)
                .Aggregate(zero, (replyDict, address) => replyDict.SetItem(address, replyDict[address] + 1));
        }

        private async Task<List<Routee>> GetCurrentRoutees(IActorRef router)
        {
            var reply = await router.Ask<Routees>(GetRoutees.Instance);
            return reply.Members.ToList();
        }
    }
}
