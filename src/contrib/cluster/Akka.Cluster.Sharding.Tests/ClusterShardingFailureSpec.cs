//-----------------------------------------------------------------------
// <copyright file="ClusterShardingFailureSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Tests.MultiNode;
using Akka.Configuration;
using Akka.Persistence.Journal;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingFailureSpecConfig : MultiNodeConfig
    {
        public ClusterShardingFailureSpecConfig()
        {
            var controller = Role("controller");
            var first = Role("first");
            var second = Role("second");

            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = INFO
                akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider""
                akka.remote.log-remote-lifecycle-events = off
                akka.cluster.auto-down-unreachable-after = 0s
                akka.cluster.down-removal-margin = 5s
                akka.cluster.roles = [""backend""]
                akka.persistence.journal.plugin = ""akka.persistence.journal.in-mem""
                akka.persistence.journal.in-mem {
                  timeout = 5s
                  store {
                    native = off
                    dir = ""target/journal-ClusterShardingFailureSpec""
                  }
                }
                akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.local""
                akka.persistence.snapshot-store.local.dir = ""target/snapshots-ClusterShardingFailureSpec""
                akka.cluster.sharding.coordinator-failure-backoff = 3s
                akka.cluster.sharding.shard-failure-backoff = 3s
            ");

            TestTransport = true;
        }
    }

    public class ClusterShardingFailureNode1 : ClusterShardingFailureSpec { }
    public class ClusterShardingFailureNode2 : ClusterShardingFailureSpec { }
    public class ClusterShardingFailureNode3 : ClusterShardingFailureSpec { }

    public abstract class ClusterShardingFailureSpec : MultiNodeClusterSpec
    {
        #region setup

        [Serializable]
        internal sealed class Get
        {
            public readonly string Id;
            public Get(string id)
            {
                Id = id;
            }
        }

        [Serializable]
        internal sealed class Add
        {
            public readonly string Id;
            public readonly int I;
            public Add(string id, int i)
            {
                Id = id;
                I = i;
            }
        }

        [Serializable]
        internal sealed class Value
        {
            public readonly string Id;
            public readonly int N;
            public Value(string id, int n)
            {
                Id = id;
                N = n;
            }
        }

        internal class Entity : ReceiveActor
        {
            private int _n = 0;

            public Entity()
            {
                Receive<Get>(get => Sender.Tell(new Value(get.Id, _n)));
                Receive<Add>(add => _n += add.I);
            }
        }

        internal IdExtractor extractEntityId = message =>
        {
            if (message is Get) return Tuple.Create((message as Get).Id, message);
            if (message is Add) return Tuple.Create((message as Add).Id, message);
            return null;
        };

        internal ShardResolver extractShardId = message =>
        {
            if (message is Get) return (message as Get).Id[0].ToString();
            if (message is Add) return (message as Add).Id[0].ToString();
            return null;
        };

        private DirectoryInfo[] _storageLocations;
        private Lazy<IActorRef> _region;
        private RoleName _first;
        private RoleName _second;
        private RoleName _controller;

        protected ClusterShardingFailureSpec() : base(new ClusterShardingFailureSpecConfig())
        {
            _storageLocations = new[]
            {
                "akka.persistence.journal.leveldb.dir",
                "akka.persistence.journal.leveldb-shared.store.dir",
                "akka.persistence.snapshot-store.local.dir"
            }.Select(s => new DirectoryInfo(Sys.Settings.Config.GetString(s))).ToArray();

            _region = new Lazy<IActorRef>(() => ClusterSharding.Get(Sys).ShardRegion("Entity"));

            _controller = new RoleName("controller");
            _first = new RoleName("first");
            _second = new RoleName("second");
        }


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
        public void ClusterSharding_with_flaky_journal_should_setup_shared_journal()
        {
            // start the Persistence extension
            Persistence.Persistence.Instance.Apply(Sys);
            RunOn(() =>
            {
                Sys.ActorOf(Props.Create<MemoryJournal>(), "store");
            }, _controller);
            EnterBarrier("persistence-started");

            RunOn(() =>
            {
                Sys.ActorSelection(Node(_first) / "user" / "store").Tell(new Identify(null));
                var sharedStore = ExpectMsg<ActorIdentity>().Subject;
                //TODO: SharedLeveldbJournal.setStore(sharedStore, system)
            }, _first, _second);
            EnterBarrier("after-1");
        }

        [MultiNodeFact(Skip = "TODO")]
        public void ClusterSharding_with_flaky_journal_should_join_cluster()
        {
            ClusterSharding_with_flaky_journal_should_setup_shared_journal();

            Within(TimeSpan.FromSeconds(20), () =>
            {
                Join(_first, _first);
                Join(_second, _first);

                RunOn(() =>
                {
                    var region = _region.Value;
                    region.Tell(new Add("10", 1));
                    region.Tell(new Add("20", 2));
                    region.Tell(new Add("21", 3));
                    region.Tell(new Get("10"));
                    ExpectMsg<Value>(v => v.Id == "10" && v.N == 1);
                    region.Tell(new Get("20"));
                    ExpectMsg<Value>(v => v.Id == "20" && v.N == 2);
                    region.Tell(new Get("21"));
                    ExpectMsg<Value>(v => v.Id == "21" && v.N == 3);
                }, _first);
                EnterBarrier("after-2");
            });
        }

        [MultiNodeFact(Skip = "TODO")]
        public void ClusterSharding_with_flaky_journal_should_recover_after_journal_failure()
        {
            ClusterSharding_with_flaky_journal_should_join_cluster();

            Within(TimeSpan.FromSeconds(20), () =>
            {
                RunOn(() =>
                {
                    TestConductor.Blackhole(_controller, _first, ThrottleTransportAdapter.Direction.Both).Wait();
                    TestConductor.Blackhole(_controller, _second, ThrottleTransportAdapter.Direction.Both).Wait();
                }, _controller);
                EnterBarrier("journal-backholded");

                RunOn(() =>
                {
                    // try with a new shard, will not reply until journal is available again
                    var region = _region.Value;
                    region.Tell(new Add("40", 4));
                    var probe = CreateTestProbe();
                    region.Tell(new Get("40"), probe.Ref);
                    probe.ExpectNoMsg(TimeSpan.FromSeconds(1));
                }, _first);
                EnterBarrier("first-delayed");

                RunOn(() =>
                {
                    TestConductor.PassThrough(_controller, _first, ThrottleTransportAdapter.Direction.Both).Wait();
                    TestConductor.PassThrough(_controller, _second, ThrottleTransportAdapter.Direction.Both).Wait();
                }, _controller);
                EnterBarrier("journal-ok");

                RunOn(() =>
                {
                    var region = _region.Value;
                    region.Tell(new Get("21"));
                    ExpectMsg<Value>(v => v.Id == "21" && v.N == 3);
                    var entity21 = LastSender;
                    var shard2 = Sys.ActorSelection(entity21.Path.Parent);


                    //Test the PersistentShardCoordinator allocating shards during a journal failure
                    region.Tell(new Add("30", 3));

                    //Test the Shard starting entities and persisting during a journal failure
                    region.Tell(new Add("11", 1));

                    //Test the Shard passivate works during a journal failure
                    shard2.Tell(new Passivate(PoisonPill.Instance), entity21);
                    region.Tell(new Add("21", 1));

                    region.Tell(new Get("21"));
                    ExpectMsg<Value>(v => v.Id == "21" && v.N == 1);

                    region.Tell(new Get("30"));
                    ExpectMsg<Value>(v => v.Id == "30" && v.N == 3);

                    region.Tell(new Get("11"));
                    ExpectMsg<Value>(v => v.Id == "11" && v.N == 1);

                    region.Tell(new Get("40"));
                    ExpectMsg<Value>(v => v.Id == "40" && v.N == 4);
                }, _first);
                EnterBarrier("verified-first");

                RunOn(() =>
                {
                    var region = _region.Value;
                    region.Tell(new Add("10", 1));
                    region.Tell(new Add("20", 2));
                    region.Tell(new Add("30", 3));
                    region.Tell(new Add("11", 4));
                    region.Tell(new Get("10"));
                    ExpectMsg<Value>(v => v.Id == "10" && v.N == 2);
                    region.Tell(new Get("11"));
                    ExpectMsg<Value>(v => v.Id == "11" && v.N == 5);
                    region.Tell(new Get("20"));
                    ExpectMsg<Value>(v => v.Id == "20" && v.N == 4);
                    region.Tell(new Get("30"));
                    ExpectMsg<Value>(v => v.Id == "30" && v.N == 6);
                }, _second);
                EnterBarrier("after-3");
            });
        }
    }
}