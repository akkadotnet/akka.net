//-----------------------------------------------------------------------
// <copyright file="ClusterShardingLeavingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Tests.MultiNode;
using Akka.Configuration;
using Akka.Persistence.Journal;
using Akka.Remote.TestKit;
using Xunit;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingLeavingSpecConfig : MultiNodeConfig
    {
        public ClusterShardingLeavingSpecConfig()
        {
            var first = Role("first");
            var second = Role("second");
            var third = Role("third");
            var fourth = Role("fourth");

            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = INFO
                akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider""
                akka.remote.log-remote-lifecycle-events = off
                akka.cluster.auto-down-unreachable-after = 0s
                akka.cluster.down-removal-margin = 5s
                akka.persistence.journal.plugin = ""Akka.Persistence.Journal.in-mem""
                akka.persistence.journal.in-mem {
                  timeout = 5s
                  store {
                    native = off
                    dir = ""target/journal-ClusterShardingLeavingSpec""
                  }
                }
                akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.local""
                akka.persistence.snapshot-store.local.dir = ""target/snapshots-ClusterShardingLeavingSpec""
            ");
        }
    }

    public class ClusterShardingLeavingNode1 : ClusterShardinLeavingSpec { }
    public class ClusterShardingLeavingNode2 : ClusterShardinLeavingSpec { }
    public class ClusterShardingLeavingNode3 : ClusterShardinLeavingSpec { }
    public class ClusterShardingLeavingNode4 : ClusterShardinLeavingSpec { }

    public abstract class ClusterShardinLeavingSpec : MultiNodeClusterSpec
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
            public readonly IDictionary<string, IActorRef> LocationMap;
            public Locations(IDictionary<string, IActorRef> locationMap)
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

        internal IdExtractor extractEntityId = message => message is Ping ? Tuple.Create((message as Ping).Id, message) : null;
        internal ShardResolver extractShardId = message => message is Ping ? (message as Ping).Id[0].ToString() : null;

        private readonly DirectoryInfo[] _storageLocations;
        private readonly Lazy<IActorRef> _region;

        private readonly RoleName _first;
        private readonly RoleName _second;
        private readonly RoleName _third;
        private readonly RoleName _fourth;

        protected ClusterShardinLeavingSpec() : base(new ClusterShardingLeavingSpecConfig())
        {
            _storageLocations = new[]
            {
                "akka.persistence.journal.leveldb.dir",
                "akka.persistence.journal.leveldb-shared.store.dir",
                "akka.persistence.snapshot-store.local.dir"
            }.Select(s => new DirectoryInfo(Sys.Settings.Config.GetString(s))).ToArray();

            _first = new RoleName("first");
            _second = new RoleName("second");
            _third = new RoleName("third");
            _fourth = new RoleName("fourth");

            _region = new Lazy<IActorRef>(() => ClusterSharding.Get(Sys).ShardRegion("Entity"));
        }

        protected override int InitialParticipantsValueFactory { get { return Roles.Count; } }

        protected override void AtStartup()
        {
            base.AtStartup();
            RunOn(() =>
            {
                foreach (var location in _storageLocations) if (location.Exists) location.Delete();
            }, _first);
        }

        protected override void AfterTermination()
        {
            base.AfterTermination();
            RunOn(() =>
            {
                foreach (var location in _storageLocations) if (location.Exists) location.Delete();
            }, _first);
        }

        #endregion

        private void Join(RoleName from, RoleName to)
        {
            RunOn(() =>
            {
                Cluster.Join(Node(to).Address);
                StartSharding();
                Within(TimeSpan.FromSeconds(5), () =>
                {
                    AwaitAssert(() => Assert.True(Cluster.ReadView.State.Members.Any(m => m.UniqueAddress == Cluster.SelfUniqueAddress && m.Status == MemberStatus.Up)));
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
                idExtractor: extractEntityId,
                shardResolver: extractShardId);
        }

        [MultiNodeFact(Skip = "TODO")]
        public void ClusterSharding_with_leaving_member_should_setup_shared_journal()
        {
            // start the Persistence extension
            Persistence.Persistence.Instance.Apply(Sys);
            RunOn(() =>
            {
                Sys.ActorOf(Props.Create<MemoryJournal>(), "store");
            }, _first);
            EnterBarrier("persistence-started");

            Sys.ActorSelection(Node(_first) / "user" / "store").Tell(new Identify(null));
            var sharedStore = ExpectMsg<ActorIdentity>().Subject;

            //SharedLeveldbJournal.setStore(sharedStore, system)
            EnterBarrier("after-1");
        }

        [MultiNodeFact(Skip = "TODO")]
        public void ClusterSharding_with_leaving_member_should_join_cluster()
        {
            ClusterSharding_with_leaving_member_should_setup_shared_journal();

            Within(TimeSpan.FromSeconds(20), () =>
            {
                Join(_first, _first);
                Join(_second, _first);
                Join(_third, _first);
                Join(_fourth, _first);

                EnterBarrier("after-2");
            });
        }

        [MultiNodeFact(Skip = "TODO")]
        public void ClusterSharding_with_leaving_member_should_initialize_shards()
        {
            ClusterSharding_with_leaving_member_should_join_cluster();

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
                    .ToDictionary(kv => kv.Key, kv => kv.Value);

                shardLocations.Tell(new Locations(locations));
            }, _first);
            EnterBarrier("after-3");
        }

        [MultiNodeFact(Skip = "TODO")]
        public void ClusterSharding_with_leaving_member_should_recover_after_leaving_coordinator_node()
        {
            ClusterSharding_with_leaving_member_should_initialize_shards();

            Within(TimeSpan.FromSeconds(30), () =>
            {
                RunOn(() =>
                {
                    Cluster.Leave(Node(_first).Address);
                }, _third);

                RunOn(() =>
                {
                    var region = _region.Value;
                    Watch(region);
                    ExpectTerminated(region, TimeSpan.FromSeconds(15));
                }, _first);
                EnterBarrier("stopped");

                RunOn(() =>
                {
                    Sys.ActorSelection(Node(_first) / "user" / "sharedLocations").Tell(GetLocations.Instance);
                    var locations = ExpectMsg<Locations>();
                    var firstAddress = Node(_first).Address;
                    AwaitAssert(() =>
                    {
                        var region = _region.Value;
                        var probe = CreateTestProbe();
                        foreach (var kv in locations.LocationMap)
                        {
                            var id = kv.Key;
                            var r = kv.Value;
                            region.Tell(new Ping(id), probe.Ref);
                            if (r.Path.Address.Equals(firstAddress))
                            {
                                Assert.NotEqual(r, probe.ExpectMsg<IActorRef>(TimeSpan.FromSeconds(1)));
                            }
                            else
                            {
                                probe.ExpectMsg(r, TimeSpan.FromSeconds(1)); // should not move
                            }
                        }
                    });
                }, _second, _third, _fourth);
                EnterBarrier("after-4");
            });
        }
    }
}