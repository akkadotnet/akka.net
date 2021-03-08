//-----------------------------------------------------------------------
// <copyright file="ClusterShardingFailureSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Event;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Akka.Util;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingFailureSpecConfig : MultiNodeClusterShardingConfig
    {
        public RoleName Controller { get; }
        public RoleName First { get; }
        public RoleName Second { get; }

        public ClusterShardingFailureSpecConfig(StateStoreMode mode)
            : base(mode: mode, loglevel: "DEBUG", additionalConfig: @"
            akka.cluster.roles = [""backend""]
            akka.cluster.sharding {
                coordinator-failure-backoff = 3s
                shard-failure-backoff = 3s
            }
            # don't leak ddata state across runs
            akka.cluster.sharding.distributed-data.durable.keys = []
            akka.persistence.journal.leveldb-shared.store.native = off
            ")
        {
            Controller = Role("controller");
            First = Role("first");
            Second = Role("second");

            TestTransport = true;
        }
    }

    public class PersistentClusterShardingFailureSpecConfig : ClusterShardingFailureSpecConfig
    {
        public PersistentClusterShardingFailureSpecConfig()
            : base(StateStoreMode.Persistence)
        {
        }
    }

    public class DDataClusterShardingFailureSpecConfig : ClusterShardingFailureSpecConfig
    {
        public DDataClusterShardingFailureSpecConfig()
            : base(StateStoreMode.DData)
        {
        }
    }

    public class PersistentClusterShardingFailureSpec : ClusterShardingFailureSpec
    {
        public PersistentClusterShardingFailureSpec()
            : base(new PersistentClusterShardingFailureSpecConfig(), typeof(PersistentClusterShardingFailureSpec))
        {
        }
    }

    public class DDataClusterShardingFailureSpec : ClusterShardingFailureSpec
    {
        public DDataClusterShardingFailureSpec()
            : base(new DDataClusterShardingFailureSpecConfig(), typeof(DDataClusterShardingFailureSpec))
        {
        }
    }

    public abstract class ClusterShardingFailureSpec : MultiNodeClusterShardingSpec<ClusterShardingFailureSpecConfig>
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
            private ILoggingAdapter log = Context.GetLogger();
            private int _n = 0;

            public Entity()
            {
                log.Debug("Starting");
                Receive<Get>(get =>
                {
                    log.Debug("Got get request from {0}", Sender);
                    Sender.Tell(new Value(get.Id, _n));
                });
                Receive<Add>(add =>
                {
                    _n += add.I;
                    log.Debug("Got add request from {0}", Sender);
                });
            }

            protected override void PostStop()
            {
                log.Debug("Stopping");
                base.PostStop();
            }
        }

        private ExtractEntityId extractEntityId = message =>
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

        private ExtractShardId extractShardId = message =>
        {
            switch (message)
            {
                case Get msg:
                    return msg.Id[0].ToString();
                case Add msg:
                    return msg.Id[0].ToString();
                case ShardRegion.StartEntity se:
                    return se.EntityId;
            }
            return null;
        };

        private readonly Lazy<IActorRef> _region;

        protected ClusterShardingFailureSpec(ClusterShardingFailureSpecConfig config, Type type)
            : base(config, type)
        {
            _region = new Lazy<IActorRef>(() => ClusterSharding.Get(Sys).ShardRegion("Entity"));
        }

        private void Join(RoleName from, RoleName to)
        {
            Join(from, to, () =>
                StartSharding(
                    Sys,
                    typeName: "Entity",
                    entityProps: Props.Create(() => new Entity()),
                    extractEntityId: extractEntityId,
                    extractShardId: extractShardId)
                );
        }

        #endregion

        [MultiNodeFact]
        public void ClusterSharding_with_flaky_journal_network_specs()
        {
            ClusterSharding_with_flaky_journal_network_must_join_cluster();
            ClusterSharding_with_flaky_journal_network_must_recover_after_journal_network_failure();
        }

        private void ClusterSharding_with_flaky_journal_network_must_join_cluster()
        {
            Within(TimeSpan.FromSeconds(20), () =>
            {
                StartPersistenceIfNeeded(startOn: config.Controller, config.First, config.Second);

                Join(config.First, config.First);
                Join(config.Second, config.First);

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
                }, config.First);
                EnterBarrier("after-2");
            });
        }

        private void ClusterSharding_with_flaky_journal_network_must_recover_after_journal_network_failure()
        {
            Within(TimeSpan.FromSeconds(20), () =>
            {
                RunOn(() =>
                {
                    if (PersistenceIsNeeded)
                    {
                        TestConductor.Blackhole(config.Controller, config.First, ThrottleTransportAdapter.Direction.Both).Wait();
                        TestConductor.Blackhole(config.Controller, config.Second, ThrottleTransportAdapter.Direction.Both).Wait();
                    }
                    else
                    {
                        TestConductor.Blackhole(config.First, config.Second, ThrottleTransportAdapter.Direction.Both).Wait();
                    }
                }, config.Controller);
                EnterBarrier("journal-backholded");

                RunOn(() =>
                {
                    // try with a new shard, will not reply until journal/network is available again
                    var region = _region.Value;
                    region.Tell(new Add("40", 4));
                    var probe = CreateTestProbe();
                    region.Tell(new Get("40"), probe.Ref);
                    probe.ExpectNoMsg(TimeSpan.FromSeconds(1));
                }, config.First);
                EnterBarrier("first-delayed");

                RunOn(() =>
                {
                    if (PersistenceIsNeeded)
                    {
                        TestConductor.PassThrough(config.Controller, config.First, ThrottleTransportAdapter.Direction.Both).Wait();
                        TestConductor.PassThrough(config.Controller, config.Second, ThrottleTransportAdapter.Direction.Both).Wait();
                    }
                    else
                    {
                        TestConductor.PassThrough(config.First, config.Second, ThrottleTransportAdapter.Direction.Both).Wait();
                    }
                }, config.Controller);
                EnterBarrier("journal-ok");

                RunOn(() =>
                {
                    var region = _region.Value;
                    region.Tell(new Get("21"));
                    ExpectMsg<Value>(v => v.Id == "21" && v.N == 3);
                    var entity21 = LastSender;
                    var shard2 = Sys.ActorSelection(entity21.Path.Parent);


                    //Test the ShardCoordinator allocating shards after a journal/network failure
                    region.Tell(new Add("30", 3));

                    //Test the Shard starting entities and persisting after a journal/network failure
                    region.Tell(new Add("11", 1));

                    //Test the Shard passivate works after a journal failure
                    shard2.Tell(new Passivate(PoisonPill.Instance), entity21);

                    AwaitAssert(() =>
                    {
                        // Note that the order between this Get message to 21 and the above Passivate to 21 is undefined.
                        // If this Get arrives first the reply will be Value("21", 3) and then it is retried by the
                        // awaitAssert.
                        // Also note that there is no timeout parameter on below expectMsg because messages should not
                        // be lost here. They should be buffered and delivered also after Passivate completed.
                        region.Tell(new Get("21"));
                        // counter reset to 0 when started again
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
                }, config.First);
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
                }, config.Second);
                EnterBarrier("after-3");
            });
        }
    }
}
