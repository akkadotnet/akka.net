//-----------------------------------------------------------------------
// <copyright file="ClusterShardingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Remote.TestKit;
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
                    akka.cluster.sharded-daemon-process {{
                      sharding {{
                        # First is likely to be ignored as shard coordinator not ready
                        retry-interval = 0.2s
                      }}
                      # quick ping to make test swift
                      keep-alive-interval = 1s
                    }}
                "))
                .WithFallback(ClusterSharding.DefaultConfig())
                .WithFallback(ClusterSingletonManager.DefaultConfig())
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
        private readonly ShardedDaemonProcessSpecConfig _config;

        protected ShardedDaemonProcessSpec(ShardedDaemonProcessSpecConfig config, Type type)
            : base(config, type)
        {
            _config = config;
        }

        [MultiNodeFact]
        public void ShardedDaemonProcess_Specs()
        {
            ShardedDaemonProcess_Should_Init_Actor_Set();
        }

        public void ShardedDaemonProcess_Should_Init_Actor_Set()
        {
            // HACK
            RunOn(() => FormCluster(_config.First, _config.Second, _config.Third), _config.First);

            var probe = CreateTestProbe();
            ShardedDaemonProcess.Get(Sys).Init("the-fearless", 4, id => ProcessActor.Props(id, probe.Ref));
            EnterBarrier("actor-set-initialized");

            RunOn(() =>
            {
                var startedIds = Enumerable.Range(0, 4).Select(_ =>
                {
                    var evt = probe.ExpectMsg<ProcessActorEvent>(TimeSpan.FromSeconds(5));
                    evt.Event.Should().Be("Started");
                    return evt.Id;
                }).ToList();
                startedIds.Count.Should().Be(4);
            }, _config.First);
            EnterBarrier("actor-set-started");
        }

        private void FormCluster(RoleName first, params RoleName[] rest)
        {
            RunOn(() =>
            {
                Cluster.Join(GetAddress(first));
                AwaitAssert(() =>
                {
                    Cluster.State.Members.Select(i => i.UniqueAddress).Should().Contain(Cluster.SelfUniqueAddress);
                    Cluster.State.Members.Select(i => i.Status).Should().OnlyContain(i => i == MemberStatus.Up);
                });
            }, first);
            EnterBarrier(first.Name + "-joined");

            foreach (var node in rest)
            {
                RunOn(() =>
                {
                    Cluster.Join(GetAddress(first));
                    AwaitAssert(() =>
                    {
                        Cluster.State.Members.Select(i => i.UniqueAddress).Should().Contain(Cluster.SelfUniqueAddress);
                        Cluster.State.Members.Select(i => i.Status).Should().OnlyContain(i => i == MemberStatus.Up);
                    });
                }, node);
            }
            EnterBarrier("all-joined");
        }
    }

    internal class ProcessActor : UntypedActor
    {
        #region Protocol

        [Serializable]
        public sealed class Stop
        {
            public static readonly Stop Instance = new Stop();
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

        protected override void PreStart()
        {
            base.PreStart();
            Probe.Tell(new ProcessActorEvent(Id, "Started"));
        }

        protected override void OnReceive(object message)
        {
            if (message is Stop)
            {
                Probe.Tell(new ProcessActorEvent(Id, "Stopped"));
                Context.Stop(Self);
            }
        }
    }

    internal sealed class ProcessActorEvent
    {
        public ProcessActorEvent(int id, object @event)
        {
            Id = id;
            Event = @event;
        }

        public int Id { get; }
        public object Event { get; }
    }
}
