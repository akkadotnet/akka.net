//-----------------------------------------------------------------------
// <copyright file="ClusterShardingRememberEntitiesSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingRememberEntitiesSpecConfig : MultiNodeClusterShardingConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }

        public ClusterShardingRememberEntitiesSpecConfig(
            StateStoreMode mode,
            bool rememberEntities,
            RememberEntitiesStore rememberEntitiesStore = RememberEntitiesStore.DData)
            : base(mode: mode, rememberEntities: rememberEntities, rememberEntitiesStore: rememberEntitiesStore,
                  loglevel: "DEBUG", additionalConfig: @"
              akka.testconductor.barrier-timeout = 60 s
              akka.test.single-expect-default = 60 s
            ")
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            NodeConfig(new[] { Third }, new[] { ConfigurationFactory.ParseString(@"
                akka.cluster.sharding.distributed-data.durable.lmdb {
                    # use same directory when starting new node on third (not used at same time)
                    dir = ""target/ShardingRememberEntitiesSpec/sharding-third""
                }
            ") });
        }
    }

    public class PersistentClusterShardingRememberEntitiesSpecConfig : ClusterShardingRememberEntitiesSpecConfig
    {
        public PersistentClusterShardingRememberEntitiesSpecConfig(bool rememberEntities)
            : base(StateStoreMode.Persistence, rememberEntities)
        {
        }
    }

    public class DDataClusterShardingRememberEntitiesSpecConfig : ClusterShardingRememberEntitiesSpecConfig
    {
        public DDataClusterShardingRememberEntitiesSpecConfig(bool rememberEntities)
            : base(StateStoreMode.DData, rememberEntities)
        {
        }
    }

    public class DDataClusterShardingEventSourcedRememberEntitiesSpecConfig : ClusterShardingRememberEntitiesSpecConfig
    {
        public DDataClusterShardingEventSourcedRememberEntitiesSpecConfig(bool rememberEntities)
            : base(StateStoreMode.DData, rememberEntities, RememberEntitiesStore.Eventsourced)
        {
        }
    }

    public class PersistentClusterShardingRememberEntitiesEnabledSpec : ClusterShardingRememberEntitiesSpec
    {
        public PersistentClusterShardingRememberEntitiesEnabledSpec()
            : base(new PersistentClusterShardingRememberEntitiesSpecConfig(true), typeof(PersistentClusterShardingRememberEntitiesEnabledSpec))
        {
        }
    }

    public class PersistentClusterShardingRememberEntitiesDefaultSpec : ClusterShardingRememberEntitiesSpec
    {
        public PersistentClusterShardingRememberEntitiesDefaultSpec()
            : base(new PersistentClusterShardingRememberEntitiesSpecConfig(false), typeof(PersistentClusterShardingRememberEntitiesDefaultSpec))
        {
        }
    }

    public class DDataClusterShardingRememberEntitiesEnabledSpec : ClusterShardingRememberEntitiesSpec
    {
        public DDataClusterShardingRememberEntitiesEnabledSpec()
            : base(new DDataClusterShardingRememberEntitiesSpecConfig(true), typeof(DDataClusterShardingRememberEntitiesEnabledSpec))
        {
        }
    }

    public class DDataClusterShardingRememberEntitiesDefaultSpec : ClusterShardingRememberEntitiesSpec
    {
        public DDataClusterShardingRememberEntitiesDefaultSpec()
            : base(new DDataClusterShardingRememberEntitiesSpecConfig(false), typeof(DDataClusterShardingRememberEntitiesDefaultSpec))
        {
        }
    }

    public class DDataClusterShardingEventSourcedRememberEntitiesEnabledSpec : ClusterShardingRememberEntitiesSpec
    {
        public DDataClusterShardingEventSourcedRememberEntitiesEnabledSpec()
            : base(new DDataClusterShardingEventSourcedRememberEntitiesSpecConfig(true), typeof(DDataClusterShardingEventSourcedRememberEntitiesEnabledSpec))
        {
        }
    }

    public abstract class ClusterShardingRememberEntitiesSpec : MultiNodeClusterShardingSpec<ClusterShardingRememberEntitiesSpecConfig>
    {
        #region setup

        private ExtractEntityId extractEntityId = message =>
        {
            switch (message)
            {
                case int id:
                    return (id.ToString(), id);
            }
            return Option<(string, object)>.None;
        };

        private ExtractShardId extractShardId = message =>
        {
            switch (message)
            {
                case int id:
                    return id.ToString();
                case ShardRegion.StartEntity msg:
                    return msg.EntityId;
            }
            return null;
        };

        private const string dataType = "Entity";

        private readonly Lazy<IActorRef> _region;

        protected ClusterShardingRememberEntitiesSpec(ClusterShardingRememberEntitiesSpecConfig config, Type type)
            : base(config, type)
        {
            _region = new Lazy<IActorRef>(() => ClusterSharding.Get(Sys).ShardRegion(dataType));
        }


        private IActorRef StartSharding(ActorSystem sys, IActorRef probe)
        {
            return StartSharding(
                sys,
                typeName: dataType,
                entityProps: Props.Create(() => new EntityActor(probe)),
                settings: ClusterShardingSettings.Create(sys).WithRememberEntities(config.RememberEntities),
                extractEntityId: extractEntityId,
                extractShardId: extractShardId);
        }

        private EntityActor.Started ExpectEntityRestarted(
            ActorSystem sys,
            int @event,
            TestProbe probe,
            TestProbe entityProbe)
        {
            if (!config.RememberEntities)
            {
                probe.Send(ClusterSharding.Get(sys).ShardRegion(dataType), @event);
                probe.ExpectMsg(1);
            }

            return entityProbe.ExpectMsg<EntityActor.Started>(TimeSpan.FromSeconds(30));
        }

        #endregion

        [MultiNodeFact]
        public void Cluster_sharding_with_remember_entities_specs()
        {
            Cluster_sharding_with_remember_entities_must_start_remembered_entities_when_coordinator_fail_over();

            // https://github.com/akkadotnet/akka.net/issues/4262 - need to resolve this and then we can remove if statement
            if (!IsDdataMode)
                Cluster_sharding_with_remember_entities_must_start_remembered_entities_in_new_cluster();
        }

        private void Cluster_sharding_with_remember_entities_must_start_remembered_entities_when_coordinator_fail_over()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                StartPersistenceIfNeeded(startOn: config.First, config.First, config.Second, config.Third);

                var entityProbe = CreateTestProbe();
                var probe = CreateTestProbe();
                Join(config.Second, config.Second);
                RunOn(() =>
                {
                    StartSharding(Sys, entityProbe.Ref);
                    probe.Send(_region.Value, 1);
                    probe.ExpectMsg(1);
                    entityProbe.ExpectMsg<EntityActor.Started>();
                }, config.Second);
                EnterBarrier("second-started");

                Join(config.Third, config.Second);
                RunOn(() =>
                {
                    StartSharding(Sys, entityProbe.Ref);
                }, config.Third);

                RunOn(() =>
                {
                    Within(Remaining, () =>
                    {
                        AwaitAssert(() =>
                        {
                            Cluster.State.Members.Count.Should().Be(2);
                            Cluster.State.Members.Should().OnlyContain(i => i.Status == MemberStatus.Up);
                        });
                    });
                }, config.Second, config.Third);
                EnterBarrier("all-up");

                RunOn(() =>
                {
                    if (IsDdataMode)
                    {
                        // Entity 1 in region of first node was started when there was only one node
                        // and then the remembering state will be replicated to second node by the
                        // gossip. So we must give that a chance to replicate before shutting down second.
                        Thread.Sleep(5000);
                    }
                    TestConductor.Exit(config.Second, 0).Wait();
                }, config.First);

                EnterBarrier("crash-second");

                RunOn(() =>
                {
                    ExpectEntityRestarted(Sys, 1, probe, entityProbe);
                }, config.Third);

                EnterBarrier("after-2");
            });
        }

        private void Cluster_sharding_with_remember_entities_must_start_remembered_entities_in_new_cluster()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                RunOn(() =>
                {
                    Watch(_region.Value);

                    Cluster.Get(Sys).Leave(Cluster.Get(Sys).SelfAddress);
                    ExpectTerminated(_region.Value);
                    AwaitAssert(() =>
                    {
                        Cluster.Get(Sys).IsTerminated.Should().BeTrue();
                    });
                    // no nodes left of the original cluster, start a new cluster

                    var sys2 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
                    var entityProbe2 = CreateTestProbe(sys2);
                    var probe2 = CreateTestProbe(sys2);

                    if (PersistenceIsNeeded)
                        SetStore(sys2, storeOn: config.First);

                    Cluster.Get(sys2).Join(Cluster.Get(sys2).SelfAddress);

                    StartSharding(sys2, entityProbe2.Ref);

                    ExpectEntityRestarted(sys2, 1, probe2, entityProbe2);

                    Shutdown(sys2);
                }, config.Third);
                EnterBarrier("after-3");
            });
        }
    }
}
