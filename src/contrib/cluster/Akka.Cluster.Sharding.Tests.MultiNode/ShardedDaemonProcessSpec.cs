//-----------------------------------------------------------------------
// <copyright file="ShardedDaemonProcessSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Event;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.TestKit;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests.MultiNode
{
    public class ShardedDaemonProcessSpecConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }

        public ShardedDaemonProcessSpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = DebugConfig(false)
                .WithFallback(ConfigurationFactory.ParseString(@"
                    akka.loglevel = INFO
                    # `first` collects Started events from every node, so it sits in front of the
                    # trailing barrier for as long as cross-node shard allocation takes. Give the
                    # barrier more room than the 30s default so a slow allocation cannot turn into
                    # a barrier timeout on the nodes waiting for `first`.
                    akka.testconductor.barrier-timeout = 60s
                    # NB: these braces used to be doubled, which HOCON parsed as nested anonymous
                    # objects and quietly dropped keep-alive-interval on the floor - the spec ran on
                    # the 10s default, so a missed initial start waited 10s for the next ping.
                    akka.cluster.sharded-daemon-process {
                      sharding {
                        # First is likely to be ignored as shard coordinator not ready
                        retry-interval = 0.2s
                      }
                      # quick ping to make test swift
                      keep-alive-interval = 1s
                    }
                "))
                .WithFallback(ClusterSharding.DefaultConfig())
                .WithFallback(ClusterSingleton.DefaultConfig())
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }
    }

    public class ShardedDaemonProcessMultiNode : ShardedDaemonProcessSpec
    {
        public ShardedDaemonProcessMultiNode() : this(new ShardedDaemonProcessSpecConfig()) { }
        protected ShardedDaemonProcessMultiNode(ShardedDaemonProcessSpecConfig config) : base(config, typeof(ShardedDaemonProcessMultiNode)) { }
    }

    public abstract class ShardedDaemonProcessSpec : MultiNodeClusterSpec
    {
        private const int TotalProcesses = 4;

        /// <summary>
        /// Deterministic name for the single cluster-wide collector, so the other nodes can address
        /// it. TestKit creates probe actors under the system guardian, hence the /system path below.
        /// </summary>
        private const string CollectorName = "process-event-collector";

        private readonly ShardedDaemonProcessSpecConfig _config;

        protected ShardedDaemonProcessSpec(ShardedDaemonProcessSpecConfig config, Type type)
            : base(config, type)
        {
            _config = config;
        }

        [MultiNodeFact]
        public async Task ShardedDaemonProcess_Specs()
        {
            await ShardedDaemonProcess_Should_Init_Actor_Set();
        }

        private async Task ShardedDaemonProcess_Should_Init_Actor_Set()
        {
            await AwaitClusterUpAsync(CancellationToken.None, _config.First, _config.Second, _config.Third);

            // One collector for the whole cluster, living on `first`. Every node passes this same
            // ref into its entity Props, so a ProcessActor reports in no matter which node ends up
            // hosting it. Handing each node its own local probe would only ever prove that a node
            // sees its own entities, which says nothing about the set as a whole.
            var collectorProbe = IsNode(_config.First) ? CreateTestProbe(CollectorName) : null;
            await EnterBarrierAsync("collector-started");

            var collector = await Sys
                .ActorSelection(await NodeAsync(_config.First) / "system" / CollectorName)
                .ResolveOne(TimeSpan.FromSeconds(20));

            ShardedDaemonProcess.Get(Sys).Init("the-fearless", TotalProcesses, id => ProcessActor.Props(id, collector));
            await EnterBarrierAsync("sharded-daemon-process-initialized");

            if (collectorProbe is not null)
            {
                await AssertAllProcessesStarted(collectorProbe);
            }

            await EnterBarrierAsync("sharded-daemon-process-started");
        }

        private async Task AssertAllProcessesStarted(TestProbe collectorProbe)
        {
            var startedIds = new HashSet<int>();
            var hostingNodes = new HashSet<string>();

            // A shard can be reallocated while the cluster settles, which stops an entity and starts
            // it again elsewhere, so the same id may report twice. Fish until every distinct id has
            // checked in rather than assuming exactly TotalProcesses messages arrive; this returns
            // as soon as the set is complete, so the bound below is a ceiling and not a delay.
            await collectorProbe.FishForMessageAsync<ProcessActorEvent>(
                isMessage: evt =>
                {
                    if (evt.Event != ProcessActorEvent.Started)
                        return false;

                    startedIds.Add(evt.Id);
                    hostingNodes.Add(evt.HostAddress);
                    return startedIds.Count == TotalProcesses;
                },
                max: TimeSpan.FromSeconds(30),
                hint: $"a Started event from each of the {TotalProcesses} sharded daemon processes");

            startedIds.Should().BeEquivalentTo(
                Enumerable.Range(0, TotalProcesses),
                "every sharded daemon process must start somewhere in the cluster");

            // Placement is up to the allocation strategy, so this is reported rather than asserted -
            // a single-node placement is legal, just not what this spec is here to exercise.
            Log.Info(
                "[{0}] sharded daemon processes started across [{1}] node(s): [{2}]",
                startedIds.Count, hostingNodes.Count, string.Join(", ", hostingNodes.OrderBy(x => x)));
        }
    }

    internal class ProcessActor : UntypedActor
    {
        #region Protocol

        [Serializable]
        public sealed class Stop
        {
            public static readonly Stop Instance = new();
            private Stop() { }
        }

        #endregion

        public static Props Props(int id, IActorRef probe) =>
            Actor.Props.Create(() => new ProcessActor(id, probe));

        public ProcessActor(int id, IActorRef probe)
        {
            Probe = probe;
            Id = id;
        }

        public IActorRef Probe { get; }
        public int Id { get; }

        private string SelfAddress => Cluster.Get(Context.System).SelfAddress.ToString();

        protected override void PreStart()
        {
            base.PreStart();
            Probe.Tell(new ProcessActorEvent(Id, ProcessActorEvent.Started, SelfAddress));
        }

        protected override void OnReceive(object message)
        {
            if (message is Stop)
            {
                Probe.Tell(new ProcessActorEvent(Id, ProcessActorEvent.Stopped, SelfAddress));
                Context.Stop(Self);
            }
        }
    }

    /// <summary>
    /// Reported by a <see cref="ProcessActor"/> to the cluster-wide collector. This crosses the wire
    /// whenever the entity is hosted somewhere other than <c>first</c>, so every member is a plain
    /// serializable type.
    /// </summary>
    [Serializable]
    internal sealed class ProcessActorEvent
    {
        public const string Started = "Started";
        public const string Stopped = "Stopped";

        public ProcessActorEvent(int id, string @event, string hostAddress)
        {
            Id = id;
            Event = @event;
            HostAddress = hostAddress;
        }

        public int Id { get; }

        public string Event { get; }

        /// <summary>
        /// Address of the node that hosted the entity, so the spec can report how the processes were
        /// spread across the cluster.
        /// </summary>
        public string HostAddress { get; }
    }
}
