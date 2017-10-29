﻿//-----------------------------------------------------------------------
// <copyright file="ClusterShardingLeavingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Cluster.Tests.MultiNode;
using Akka.Configuration;
using Akka.Persistence.Journal;
using Akka.Remote.TestKit;
using Xunit;
using System.Collections.Immutable;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingLeavingSpecConfig : MultiNodeConfig
    {
        public RoleName First { get; private set; }

        public RoleName Second { get; private set; }

        public RoleName Third { get; private set; }

        public RoleName Fourth { get; private set; }

        public ClusterShardingLeavingSpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");

            CommonConfig = DebugConfig(false)
                .WithFallback(ConfigurationFactory.ParseString(@"
                    akka.actor {
                        serializers {
                            hyperion = ""Akka.Serialization.HyperionSerializer, Akka.Serialization.Hyperion""
                        }
                        serialization-bindings {
                            ""System.Object"" = hyperion
                        }
                    }
                    akka.cluster.auto-down-unreachable-after = 0s

                    akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.inmem""
                    akka.persistence.journal.plugin = ""akka.persistence.journal.memory-journal-shared""

                    akka.persistence.journal.MemoryJournal {
                        class = ""Akka.Persistence.Journal.MemoryJournal, Akka.Persistence""
                        plugin-dispatcher = ""akka.actor.default-dispatcher""
                    }

                    akka.persistence.journal.memory-journal-shared {
                        class = ""Akka.Cluster.Sharding.Tests.MemoryJournalShared, Akka.Cluster.Sharding.Tests.MultiNode""
                        plugin-dispatcher = ""akka.actor.default-dispatcher""
                        timeout = 5s
                    }
                "))
                .WithFallback(Sharding.ClusterSharding.DefaultConfig())
                .WithFallback(Tools.Singleton.ClusterSingletonManager.DefaultConfig())
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }
    }

    public class ClusterShardinLeavingSpec : MultiNodeClusterSpec
    {
        #region setup

        [Serializable]
        internal sealed class Ping
        {
            public readonly string Id;

            public Ping(string id)
            {
                Id = id;
            }
        }

        [Serializable]
        internal sealed class GetLocations
        {
            public static readonly GetLocations Instance = new GetLocations();

            private GetLocations()
            {
            }
        }

        [Serializable]
        internal sealed class Locations
        {
            public readonly IImmutableDictionary<string, IActorRef> LocationMap;
            public Locations(IImmutableDictionary<string, IActorRef> locationMap)
            {
                LocationMap = locationMap;
            }
        }

        internal class Entity : ReceiveActor
        {
            public Entity()
            {
                Receive<Ping>(_ => Sender.Tell(Self));
            }
        }

        internal class ShardLocations : ReceiveActor
        {
            private Locations _locations = null;

            public ShardLocations()
            {
                Receive<GetLocations>(_ => Sender.Tell(_locations));
                Receive<Locations>(l => _locations = l);
            }
        }

        internal ExtractEntityId extractEntityId = message => message is Ping p ? Tuple.Create(p.Id, message) : null;
        internal ExtractShardId extractShardId = message => message is Ping p ? p.Id[0].ToString() : null;

        private readonly Lazy<IActorRef> _region;

        private readonly ClusterShardingLeavingSpecConfig _config;

        public ClusterShardinLeavingSpec()
            : this(new ClusterShardingLeavingSpecConfig())
        {
        }

        protected ClusterShardinLeavingSpec(ClusterShardingLeavingSpecConfig config)
            : base(config, typeof(ClusterShardinLeavingSpec))
        {
            _config = config;

            _region = new Lazy<IActorRef>(() => ClusterSharding.Get(Sys).ShardRegion("Entity"));
        }

        protected override int InitialParticipantsValueFactory { get { return Roles.Count; } }

        #endregion

        private void Join(RoleName from, RoleName to)
        {
            RunOn(() =>
            {
                Cluster.Join(Node(to).Address);
                StartSharding();
                Within(TimeSpan.FromSeconds(15), () =>
                {
                    AwaitAssert(() =>
                    {
                        Cluster.State.Members.Should().Contain(i => i.UniqueAddress == Cluster.SelfUniqueAddress && i.Status == MemberStatus.Up);
                    });
                });
            }, from);
            EnterBarrier(from.Name + "-joined");
        }

        private void StartSharding()
        {
            ClusterSharding.Get(Sys).Start(
                typeName: "Entity",
                entityProps: Props.Create<Entity>(),
                settings: ClusterShardingSettings.Create(Sys),
                extractEntityId: extractEntityId,
                extractShardId: extractShardId);
        }

        [MultiNodeFact]
        public void ClusterSharding_with_leaving_member_specs()
        {
            ClusterSharding_with_leaving_member_should_setup_shared_journal();
            ClusterSharding_with_leaving_member_should_join_cluster();
            ClusterSharding_with_leaving_member_should_initialize_shards();
            ClusterSharding_with_leaving_member_should__recover_after_leaving_coordinator_node();
        }

        public void ClusterSharding_with_leaving_member_should_setup_shared_journal()
        {
            // start the Persistence extension
            Persistence.Persistence.Instance.Apply(Sys);
            RunOn(() =>
            {
                Persistence.Persistence.Instance.Apply(Sys).JournalFor("akka.persistence.journal.MemoryJournal");
            }, _config.First);
            EnterBarrier("persistence-started");

            Sys.ActorSelection(Node(_config.First) / "system" / "akka.persistence.journal.MemoryJournal").Tell(new Identify(null));
            var sharedStore = ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(10)).Subject;
            sharedStore.Should().NotBeNull();

            MemoryJournalShared.SetStore(sharedStore, Sys);

            EnterBarrier("after-1");

            //check persistence running
            var probe = CreateTestProbe();
            var journal = Persistence.Persistence.Instance.Get(Sys).JournalFor(null);
            journal.Tell(new Persistence.ReplayMessages(0, 0, long.MaxValue, Guid.NewGuid().ToString(), probe.Ref));
            probe.ExpectMsg<Persistence.RecoverySuccess>(TimeSpan.FromSeconds(10));

            EnterBarrier("after-1-test");
        }

        public void ClusterSharding_with_leaving_member_should_join_cluster()
        {
            Within(TimeSpan.FromSeconds(20), () =>
            {
                Join(_config.First, _config.First);
                Join(_config.Second, _config.First);
                Join(_config.Third, _config.First);
                Join(_config.Fourth, _config.First);

                EnterBarrier("after-2");
            });
        }

        public void ClusterSharding_with_leaving_member_should_initialize_shards()
        {
            RunOn(() =>
            {
                var shardLocations = Sys.ActorOf(Props.Create<ShardLocations>(), "shardLocations");
                var locations = Enumerable.Range(1, 10)
                    .Select(n =>
                    {
                        var id = n.ToString();
                        _region.Value.Tell(new Ping(id));
                        return new KeyValuePair<string, IActorRef>(id, ExpectMsg<IActorRef>());
                    })
                    .ToImmutableDictionary(kv => kv.Key, kv => kv.Value);

                shardLocations.Tell(new Locations(locations));
            }, _config.First);
            EnterBarrier("after-3");
        }

        public void ClusterSharding_with_leaving_member_should__recover_after_leaving_coordinator_node()
        {

            Sys.ActorSelection(Node(_config.First) / "user" / "shardLocations").Tell(GetLocations.Instance);
            var originalLocations = ExpectMsg<Locations>();
            var firstAddress = Node(_config.First).Address;

            RunOn(() =>
            {
                Cluster.Leave(Node(_config.First).Address);
            }, _config.Third);

            RunOn(() =>
            {
                var region = _region.Value;
                Watch(region);
                ExpectTerminated(region, TimeSpan.FromSeconds(15));
            }, _config.First);
            EnterBarrier("stopped");

            RunOn(() =>
            {
                Within(TimeSpan.FromSeconds(15), () =>
                {
                    AwaitAssert(() =>
                    {
                        var region = _region.Value;
                        var probe = CreateTestProbe();
                        foreach (var kv in originalLocations.LocationMap)
                        {
                            var id = kv.Key;
                            var r = kv.Value;
                            region.Tell(new Ping(id), probe.Ref);
                            if (r.Path.Address.Equals(firstAddress))
                                probe.ExpectMsg<IActorRef>(TimeSpan.FromSeconds(1)).Should().NotBe(r);
                            else
                                probe.ExpectMsg(r, TimeSpan.FromSeconds(1)); // should not move
                        }
                    });
                });
            }, _config.Second, _config.Third, _config.Fourth);
            EnterBarrier("after-4");
        }
    }
}