//-----------------------------------------------------------------------
// <copyright file="ClusterShardingFailureSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Akka.Util;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests
{
    public abstract class ClusterShardingFailureSpecConfig : MultiNodeConfig
    {
        public string Mode { get; }
        public RoleName Controller { get; }
        public RoleName First { get; }
        public RoleName Second { get; }

        protected ClusterShardingFailureSpecConfig(string mode)
        {
            Mode = mode;
            Controller = Role("controller");
            First = Role("first");
            Second = Role("second");

            CommonConfig = DebugConfig(false)
                .WithFallback(ConfigurationFactory.ParseString($@"
                    akka.actor {{
                        serializers {{
                            hyperion = ""Akka.Serialization.HyperionSerializer, Akka.Serialization.Hyperion""
                        }}
                        serialization-bindings {{
                            ""System.Object"" = hyperion
                        }}
                    }}
                    akka.loglevel = INFO
                    akka.actor.provider = cluster
                    akka.remote.log-remote-lifecycle-events = off
                    akka.cluster.auto-down-unreachable-after = 0s
                    akka.cluster.roles = [""backend""]
                    akka.cluster.sharding {{
                        coordinator-failure-backoff = 3s
                        shard-failure-backoff = 3s
                        state-store-mode = ""{mode}""
                    }}
                    akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.inmem""
                    akka.persistence.journal.plugin = ""akka.persistence.journal.memory-journal-shared""

                    akka.persistence.journal.MemoryJournal {{
                        class = ""Akka.Persistence.Journal.MemoryJournal, Akka.Persistence""
                        plugin-dispatcher = ""akka.actor.default-dispatcher""
                    }}
                    akka.cluster.sharding.distributed-data.durable.lmdb {{
                        dir = ""target/ClusterShardingFailureSpec/sharding-ddata""
                        map-size = 10000000
                    }}
                    akka.persistence.journal.memory-journal-shared {{
                        class = ""Akka.Cluster.Sharding.Tests.MemoryJournalShared, Akka.Cluster.Sharding.Tests.MultiNode""
                        plugin-dispatcher = ""akka.actor.default-dispatcher""
                        timeout = 5s
                    }}
                    akka.cluster.sharding.distributed-data.durable.lmdb {{
                      dir = ""target/ClusterShardingFailureSpec/sharding-ddata""
                      map-size = 10000000
                    }}"))
                .WithFallback(Sharding.ClusterSharding.DefaultConfig())
                .WithFallback(Tools.Singleton.ClusterSingletonManager.DefaultConfig())
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());

            TestTransport = true;
        }
    }
    public class PersistentClusterShardingFailureSpecConfig : ClusterShardingFailureSpecConfig
    {
        public PersistentClusterShardingFailureSpecConfig() : base("persistence") { }
    }
    public class DDataClusterShardingFailureSpecConfig : ClusterShardingFailureSpecConfig
    {
        public DDataClusterShardingFailureSpecConfig() : base("ddata") { }
    }

    public class PersistentClusterShardingFailureSpec : ClusterShardingFailureSpec
    {
        public PersistentClusterShardingFailureSpec() : this(new PersistentClusterShardingFailureSpecConfig()) { }
        protected PersistentClusterShardingFailureSpec(PersistentClusterShardingFailureSpecConfig config) : base(config, typeof(PersistentClusterShardingFailureSpec)) { }
    }
    public class DDataClusterShardingFailureSpec : ClusterShardingFailureSpec
    {
        public DDataClusterShardingFailureSpec() : this(new DDataClusterShardingFailureSpecConfig()) { }
        protected DDataClusterShardingFailureSpec(DDataClusterShardingFailureSpecConfig config) : base(config, typeof(DDataClusterShardingFailureSpec)) { }
    }
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

        internal ExtractEntityId extractEntityId = message =>
        {
            switch (message)
            {
                case Get msg:
                    return (msg.Id, message);
                case Add msg:
                    return (msg.Id, message);
            }
            return Option<(string, object)>.None;
        };

        internal ExtractShardId extractShardId = message =>
        {
            switch (message)
            {
                case Get msg:
                    return msg.Id[0].ToString();
                case Add msg:
                    return msg.Id[0].ToString();
            }
            return null;
        };

        private Lazy<IActorRef> _region;

        private readonly ClusterShardingFailureSpecConfig _config;

        private readonly List<FileInfo> _storageLocations;

        protected ClusterShardingFailureSpec(ClusterShardingFailureSpecConfig config, Type type)
            : base(config, type)
        {
            _config = config;
            _region = new Lazy<IActorRef>(() => ClusterSharding.Get(Sys).ShardRegion("Entity"));
            _storageLocations = new List<FileInfo>
            {
                new FileInfo(Sys.Settings.Config.GetString("akka.cluster.sharding.distributed-data.durable.lmdb.dir", null))
            };

            IsDDataMode = config.Mode == "ddata";

            DeleteStorageLocations();
            EnterBarrier("startup");
        }

        protected bool IsDDataMode { get; }

        protected override void AfterTermination()
        {
            base.AfterTermination();
            DeleteStorageLocations();
        }

        private void DeleteStorageLocations()
        {
            foreach (var fileInfo in _storageLocations)
            {
                if (fileInfo.Exists) fileInfo.Delete();
            }
        }

        #endregion

        private void Join(RoleName from, RoleName to)
        {
            RunOn(() =>
            {
                Cluster.Join(GetAddress(to));
                StartSharding();

                AwaitAssert(() =>
                {
                    Cluster.State.Members.Select(i => i.UniqueAddress).Should().Contain(Cluster.SelfUniqueAddress);
                    Cluster.State.Members.Select(i => i.Status).Should().OnlyContain(i => i == MemberStatus.Up);
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
        public void ClusterSharding_with_flaky_journal_network_specs()
        {
            if (!IsDDataMode)
            {
                ClusterSharding_with_flaky_journal_network_should_setup_shared_journal();
            }

            ClusterSharding_with_flaky_journal_network_should_join_cluster();
            ClusterSharding_with_flaky_journal_network_should_recover_after_journal_network_failure();
        }

        public void ClusterSharding_with_flaky_journal_network_should_setup_shared_journal()
        {
            // start the Persistence extension
            Persistence.Persistence.Instance.Apply(Sys);
            RunOn(() =>
            {
                Persistence.Persistence.Instance.Apply(Sys).JournalFor("akka.persistence.journal.MemoryJournal");
            }, _config.Controller);
            EnterBarrier("persistence-started");

            RunOn(() =>
            {
                Sys.ActorSelection(Node(_config.Controller) / "system" / "akka.persistence.journal.MemoryJournal").Tell(new Identify(null));
                var sharedStore = ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(10)).Subject;
                sharedStore.Should().NotBeNull();

                MemoryJournalShared.SetStore(sharedStore, Sys);
            }, _config.First, _config.Second);
            EnterBarrier("after-1");

            RunOn(() =>
            {
                //check persistence running
                var probe = CreateTestProbe();
                var journal = Persistence.Persistence.Instance.Get(Sys).JournalFor(null);
                journal.Tell(new Persistence.ReplayMessages(0, 0, long.MaxValue, Guid.NewGuid().ToString(), probe.Ref));
                probe.ExpectMsg<Persistence.RecoverySuccess>(TimeSpan.FromSeconds(10));
            }, _config.First, _config.Second);
            EnterBarrier("after-1-test");
        }

        public void ClusterSharding_with_flaky_journal_network_should_join_cluster()
        {
            Within(TimeSpan.FromSeconds(20), () =>
            {
                Join(_config.First, _config.First);
                Join(_config.Second, _config.First);

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
                }, _config.First);
                EnterBarrier("after-2");
            });
        }

        public void ClusterSharding_with_flaky_journal_network_should_recover_after_journal_network_failure()
        {
            Within(TimeSpan.FromSeconds(20), () =>
            {
                RunOn(() =>
                {
                    if (IsDDataMode)
                    {
                        TestConductor.Blackhole(_config.First, _config.Second, ThrottleTransportAdapter.Direction.Both).Wait();
                    }
                    else
                    {
                        TestConductor.Blackhole(_config.Controller, _config.First, ThrottleTransportAdapter.Direction.Both).Wait();
                        TestConductor.Blackhole(_config.Controller, _config.Second, ThrottleTransportAdapter.Direction.Both).Wait();
                    }
                }, _config.Controller);
                EnterBarrier("journal-backholded");

                RunOn(() =>
                {
                    // try with a new shard, will not reply until journal is available again
                    var region = _region.Value;
                    region.Tell(new Add("40", 4));
                    var probe = CreateTestProbe();
                    region.Tell(new Get("40"), probe.Ref);
                    probe.ExpectNoMsg(TimeSpan.FromSeconds(1));
                }, _config.First);
                EnterBarrier("first-delayed");

                RunOn(() =>
                {
                    if (IsDDataMode)
                    {
                        TestConductor.PassThrough(_config.First, _config.Second, ThrottleTransportAdapter.Direction.Both).Wait();
                    }
                    else
                    {
                        TestConductor.PassThrough(_config.Controller, _config.First, ThrottleTransportAdapter.Direction.Both).Wait();
                        TestConductor.PassThrough(_config.Controller, _config.Second, ThrottleTransportAdapter.Direction.Both).Wait();
                    }
                }, _config.Controller);
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

                    AwaitAssert(() =>
                    {
                        region.Tell(new Get("21"));
                        ExpectMsg<Value>(v => v.Id == "21" && v.N == 0, hint: "Passivating did not reset Value down to 0");
                    });

                    region.Tell(new Add("21", 1));

                    region.Tell(new Get("21"));
                    ExpectMsg<Value>(v => v.Id == "21" && v.N == 1);

                    region.Tell(new Get("30"));
                    ExpectMsg<Value>(v => v.Id == "30" && v.N == 3);

                    region.Tell(new Get("11"));
                    ExpectMsg<Value>(v => v.Id == "11" && v.N == 1);

                    region.Tell(new Get("40"));
                    ExpectMsg<Value>(v => v.Id == "40" && v.N == 4);
                }, _config.First);
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
                }, _config.Second);
                EnterBarrier("after-3");
            });
        }
    }
}
