//-----------------------------------------------------------------------
// <copyright file="ClusterShardingGracefulShutdownSpec.cs" company="Akka.NET Project">
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
using Xunit;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingGracefulShutdownSpecConfig : MultiNodeConfig
    {
        public ClusterShardingGracefulShutdownSpecConfig()
        {
            var first = Role("first");
            var second = Role("second");

            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = INFO
                akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider""
                akka.remote.log-remote-lifecycle-events = off
                akka.persistence.journal.plugin = ""Akka.Persistence.Journal.in-mem""
                akka.persistence.journal.in-mem {
                  timeout = 5s
                  store {
                    native = off
                    dir = ""target/journal-ClusterShardingGracefulShutdownSpec""
                  }
                }
                akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.local""
                akka.persistence.snapshot-store.local.dir = ""target/snapshots-ClusterShardingGracefulShutdownSpec""
            ");
        }
    }

    public class ClusterShardingGracefulShutdownNode1 : ClusterShardingGracefulShutdownSpec { }
    public class ClusterShardingGracefulShutdownNode2 : ClusterShardingGracefulShutdownSpec { }

    public abstract class ClusterShardingGracefulShutdownSpec : MultiNodeClusterSpec
    {
        #region setup

        [Serializable]
        internal sealed class StopEntity
        {
            public static readonly StopEntity Instance = new StopEntity();

            private StopEntity()
            {
            }
        }

        internal class Entity : ReceiveActor
        {
            public Entity()
            {
                Receive<int>(id => Sender.Tell(id));
                Receive<StopEntity>(_ => Context.Stop(Self));
            }
        }

        internal class IllustrateGracefulShutdown : UntypedActor
        {
            private readonly Cluster _cluster;
            private readonly IActorRef _region;
            public IllustrateGracefulShutdown()
            {
                _cluster = Cluster.Get(Context.System);
                _region = ClusterSharding.Get(Context.System).ShardRegion("Entity");
            }

            protected override void OnReceive(object message)
            {
                Terminated terminated;
                if (message.Equals("leave"))
                {
                    Context.Watch(_region);
                    _region.Tell(GracefulShutdown.Instance);
                }
                else if ((terminated = message as Terminated) != null && terminated.ActorRef.Equals(_region))
                {
                    //_cluster.RegisterOnMemberRemoved(() => Context.System.Shutdown());
                    _cluster.Leave(_cluster.SelfAddress);
                }
            }
        }

        internal IdExtractor extractEntityId = message => message is int ? Tuple.Create(message.ToString(), message) : null;

        internal ShardResolver extractShardId = message => message is int ? message.ToString() : null;

        private readonly Lazy<IActorRef> _region;

        private readonly DirectoryInfo[] _storageLocations;
        private readonly RoleName _first;
        private readonly RoleName _second;

        protected ClusterShardingGracefulShutdownSpec() : base(new ClusterShardingGracefulShutdownSpecConfig())
        {
            _storageLocations = new[]
            {
                "akka.persistence.journal.leveldb.dir",
                "akka.persistence.journal.leveldb-shared.store.dir",
                "akka.persistence.snapshot-store.local.dir"
            }.Select(s => new DirectoryInfo(Sys.Settings.Config.GetString(s))).ToArray();

            _region = new Lazy<IActorRef>(() => ClusterSharding.Get(Sys).ShardRegion("Entity"));

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
            var allocationStrategy = new LeastShardAllocationStrategy(2, 1);
            ClusterSharding.Get(Sys).Start(
                typeName: "Entity",
                entityProps: Props.Create<Entity>(),
                settings: ClusterShardingSettings.Create(Sys),
                idExtractor: extractEntityId,
                shardResolver: extractShardId,
                allocationStrategy: allocationStrategy,
                handOffStopMessage: StopEntity.Instance);
        }

        [MultiNodeFact(Skip = "TODO")]
        public void ClusterSharding_should_setup_shared_journal()
        {
            // start the Persistence extension
            Persistence.Persistence.Instance.Apply(Sys);
            RunOn(() =>
            {
                Sys.ActorOf(Props.Create<MemoryJournal>(), "store");
            }, _first);
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
        public void ClusterSharding_should_start_some_shards_in_both_regions()
        {
            ClusterSharding_should_setup_shared_journal();

            Join(_first, _first);
            Join(_second, _first);

            AwaitAssert(() =>
            {
                var probe = CreateTestProbe();
                var regionAddresses = Enumerable.Range(1, 100)
                    .Select(n =>
                    {
                        _region.Value.Tell(n, probe.Ref);
                        probe.ExpectMsg(n, TimeSpan.FromSeconds(1));
                        return probe.LastSender.Path.Address;
                    })
                    .ToArray();

                Assert.Equal(2, regionAddresses.Length);
            });
            EnterBarrier("after-2");
        }

        [MultiNodeFact(Skip = "TODO")]
        public void ClusterSharding_should_gracefully_shutdown_a_region()
        {
            ClusterSharding_should_start_some_shards_in_both_regions();

            RunOn(() =>
            {
                _region.Value.Tell(GracefulShutdown.Instance);
            }, _second);

            RunOn(() =>
            {
                var probe = CreateTestProbe();
                for (int i = 1; i <= 200; i++)
                {
                    _region.Value.Tell(i, probe.Ref);
                    probe.ExpectMsg(i, TimeSpan.FromSeconds(1));
                    Assert.Equal(_region.Value.Path / i.ToString() / i.ToString(), probe.LastSender.Path);
                }
            }, _first);
            EnterBarrier("handoff-completed");

            RunOn(() =>
            {
                var region = _region.Value;
                Watch(region);
                ExpectTerminated(region);
            }, _second);
            EnterBarrier("after-3");
        }
    }
}