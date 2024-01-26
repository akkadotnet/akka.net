//-----------------------------------------------------------------------
// <copyright file="DurableDataPocoSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.IO;
using Akka.Actor;
using Akka.Cluster;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.DistributedData.Durable;
using Akka.Event;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.TestKit;
using FluentAssertions;
using FluentAssertions.Extensions;

namespace Akka.DistributedData.Tests.MultiNode
{
    public class DurableDataPocoSpecConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }

        public DurableDataPocoSpecConfig(bool writeBehind)
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            var tempDir = Path.Combine(Path.GetTempPath(), "target", "DurableDataPocoSpec", $"Spec-{DateTime.UtcNow.Ticks}");
            CommonConfig = ConfigurationFactory.ParseString($@"
                akka.loglevel = INFO
                akka.log-config-on-start = off
                akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
                akka.log-dead-letters-during-shutdown = off
                akka.cluster.distributed-data.durable.keys = [""durable*""]
                akka.cluster.distributed-data.durable.lmdb {{
                    map-size = 10 MiB
                    write-behind-interval = {(writeBehind ? "200ms" : "off")}
                }}
                akka.cluster.distributed-data.durable.store-actor-class = ""Akka.DistributedData.LightningDB.LmdbDurableStore, Akka.DistributedData.LightningDB""
                # initialization of lmdb can be very slow in CI environment
                akka.test.single-expect-default = 15s")
                .WithFallback(DistributedData.DefaultConfig());

            NodeConfig(new[] { First }, new[] { ConfigurationFactory.ParseString(@"
                akka.cluster.distributed-data.durable.lmdb {
                  dir = ""target/DurableDataPocoSpec/first-ddata""
                }
            ") });

            NodeConfig(new[] { Second }, new[] { ConfigurationFactory.ParseString(@"
                akka.cluster.distributed-data.durable.lmdb {
                  dir = ""target/DurableDataPocoSpec/second-ddata""
                }
            ") });

            NodeConfig(new[] { Third }, new[] { ConfigurationFactory.ParseString(@"
                akka.cluster.distributed-data.durable.lmdb {
                  dir = ""target/DurableDataPocoSpec/third-ddata""
                }
            ") });
        }
    }

    internal sealed class PocoObject : IReplicatedData<PocoObject>
    {
        public PocoObject(string transactionId, int created)
        {
            TransactionId = transactionId;
            Created = created;
        }

        public string TransactionId { get; }
        public int Created { get; }

        public PocoObject Merge(PocoObject other)
            => other != null && other.Created > Created ? other : this;

        public IReplicatedData Merge(IReplicatedData other)
            => other is PocoObject state ? Merge(state) : this;
    }

    public abstract class DurableDataPocoSpecBase : MultiNodeClusterSpec
    {
        private readonly RoleName _first;
        private readonly RoleName _second;
        private readonly RoleName _third;
        private readonly Cluster.Cluster _cluster;
        private readonly IWriteConsistency _writeThree;
        private readonly IReadConsistency _readThree;

        private readonly ORDictionaryKey<string, PocoObject> _keyA = new("durable-A");
        private readonly ORDictionaryKey<string, PocoObject> _keyB = new("durable-B");
        private readonly ORSetKey<string> _keyC = new("durable-C");

        private int _testStepCounter = 0;

        protected DurableDataPocoSpecBase(DurableDataPocoSpecConfig config, Type type) : base(config, type)
        {
            _cluster = Akka.Cluster.Cluster.Get(Sys);
            var timeout = Dilated(14.Seconds()); // initialization of lmdb can be very slow in CI environment
            _writeThree = new WriteTo(3, timeout);
            _readThree = new ReadFrom(3, timeout);

            _first = config.First;
            _second = config.Second;
            _third = config.Third;
        }

        [MultiNodeFact]
        public void DurableDataPocoSpec_Tests()
        {
            Durable_CRDT_should_work_in_a_single_node_cluster();
            Durable_CRDT_should_work_in_a_multi_node_cluster();
            Durable_CRDT_should_be_durable_after_gossip_update();
            Durable_CRDT_should_handle_Update_before_Load();
        }

        private void TellUpdate(IActorRef replicator, ORDictionaryKey<string, PocoObject> key, IWriteConsistency consistency, PocoObject message)
        {
            var value = new PocoObject(message.TransactionId, message.Created);
            replicator.Tell(Dsl.Update(
                key, 
                ORDictionary<string, PocoObject>.Empty, 
                consistency,
                oldValue =>
                {
                    return oldValue.AddOrUpdate(_cluster, value.TransactionId, null,
                        oldTrxState => value.Merge(oldTrxState));
                }
            ));
        }

        public void Durable_CRDT_should_work_in_a_single_node_cluster()
        {
            Join(_first, _first);

            RunOn(() =>
            {
                var r = NewReplicator();
                Within(TimeSpan.FromSeconds(10), () =>
                {
                    AwaitAssert(() =>
                    {
                        r.Tell(Dsl.GetReplicaCount);
                        ExpectMsg(new ReplicaCount(1));
                    });
                });

                r.Tell(Dsl.Get(_keyA, ReadLocal.Instance));
                ExpectMsg(new NotFound(_keyA, null));

                TellUpdate(r, _keyA, WriteLocal.Instance, new PocoObject("Id_1", 1));
                TellUpdate(r, _keyA, WriteLocal.Instance, new PocoObject("Id_1", 2));
                TellUpdate(r, _keyA, WriteLocal.Instance, new PocoObject("Id_1", 3));

                ExpectMsg(new UpdateSuccess(_keyA, null));
                ExpectMsg(new UpdateSuccess(_keyA, null));
                ExpectMsg(new UpdateSuccess(_keyA, null));

                Watch(r);
                Sys.Stop(r);
                ExpectTerminated(r);

                var r2 = default(IActorRef);
                AwaitAssert(() => r2 = NewReplicator()); // try until name is free

                // note that it will stash the commands until loading completed
                r2.Tell(Dsl.Get(_keyA, ReadLocal.Instance));
                var success = ExpectMsg<GetSuccess>().Get(_keyA);
                success.TryGetValue("Id_1", out var value).Should().BeTrue();
                value.TransactionId.Should().Be("Id_1");
                value.Created.Should().Be(3);

                Watch(r2);
                Sys.Stop(r2);
                ExpectTerminated(r2);

            }, _first);

            EnterBarrierAfterTestStep();
        }

        public void Durable_CRDT_should_work_in_a_multi_node_cluster()
        {
            Join(_first, _first);
            Join(_second, _first);
            Join(_third, _first);

            var r = NewReplicator();
            Within(TimeSpan.FromSeconds(10), () =>
            {
                AwaitAssert(() =>
                {
                    r.Tell(Dsl.GetReplicaCount);
                    ExpectMsg(new ReplicaCount(3));
                });
            });

            EnterBarrier("both-initialized");

            TellUpdate(r, _keyA, _writeThree, new PocoObject("Id_1", 4));
            ExpectMsg(new UpdateSuccess(_keyA, null));

            r.Tell(Dsl.Update(_keyC, ORSet<string>.Empty, _writeThree, c => c.Add(_cluster, Myself.Name)));
            ExpectMsg(new UpdateSuccess(_keyC, null));

            EnterBarrier("update-done-" + _testStepCounter);

            r.Tell(Dsl.Get(_keyA, _readThree));
            var success = ExpectMsg<GetSuccess>().Get(_keyA);
            success.TryGetValue("Id_1", out var value).Should().BeTrue();
            value.TransactionId.Should().Be("Id_1");
            value.Created.Should().Be(4);

            r.Tell(Dsl.Get(_keyC, _readThree));
            ExpectMsg<GetSuccess>().Get(_keyC).Elements.ShouldBe(ImmutableHashSet.CreateRange(new[] { _first.Name, _second.Name, _third.Name }));

            EnterBarrier("values-verified-" + _testStepCounter);

            Watch(r);
            Sys.Stop(r);
            ExpectTerminated(r);

            var r2 = default(IActorRef);
            AwaitAssert(() => r2 = NewReplicator()); // try until name is free
            AwaitAssert(() =>
            {
                r2.Tell(Dsl.GetKeyIds);
                ExpectMsg<GetKeysIdsResult>().Keys.ShouldNotBe(ImmutableHashSet<string>.Empty);
            });

            r2.Tell(Dsl.Get(_keyA, ReadLocal.Instance));
            success = ExpectMsg<GetSuccess>().Get(_keyA);
            success.TryGetValue("Id_1", out value).Should().BeTrue();
            value.TransactionId.Should().Be("Id_1");
            value.Created.Should().Be(4);

            r2.Tell(Dsl.Get(_keyC, ReadLocal.Instance));
            ExpectMsg<GetSuccess>().Get(_keyC).Elements.ShouldBe(ImmutableHashSet.CreateRange(new[] { _first.Name, _second.Name, _third.Name }));

            EnterBarrierAfterTestStep();
        }

        public void Durable_CRDT_should_be_durable_after_gossip_update()
        {
            var r = NewReplicator();

            RunOn(() =>
            {
                Log.Debug("sending message with sender: {}", ActorCell.GetCurrentSelfOrNoSender());
                r.Tell(Dsl.Update(_keyC, ORSet<string>.Empty, WriteLocal.Instance, c => c.Add(_cluster, Myself.Name)));
                ExpectMsg(new UpdateSuccess(_keyC, null));
            }, _first);

            RunOn(() =>
            {
                r.Tell(Dsl.Subscribe(_keyC, TestActor));
                ExpectMsg<Changed>().Get(_keyC).Elements.ShouldBe(ImmutableHashSet.Create(_first.Name));

                // must do one more roundtrip to be sure that it keyB is stored, since Changed might have
                // been sent out before storage
                TellUpdate(r, _keyA, WriteLocal.Instance, new PocoObject("Id_1", 3));
                ExpectMsg(new UpdateSuccess(_keyA, null));

                Watch(r);
                Sys.Stop(r);
                ExpectTerminated(r);

                var r2 = default(IActorRef);
                AwaitAssert(() => r2 = NewReplicator()); // try until name is free
                AwaitAssert(() =>
                {
                    r2.Tell(Dsl.GetKeyIds);
                    ExpectMsg<GetKeysIdsResult>().Keys.ShouldNotBe(ImmutableHashSet<string>.Empty);
                });

                r2.Tell(Dsl.Get(_keyC, ReadLocal.Instance));
                ExpectMsg<GetSuccess>().Get(_keyC).Elements.ShouldBe(ImmutableHashSet.Create(_first.Name));

            }, _second);

            EnterBarrierAfterTestStep();
        }

        public void Durable_CRDT_should_handle_Update_before_Load()
        {
            RunOn(() =>
            {
                var sys1 = ActorSystem.Create("AdditionalSys", Sys.Settings.Config);
                var cluster1 = Akka.Cluster.Cluster.Get(sys1);
                var address = cluster1.SelfAddress;
                try
                {
                    cluster1.Join(address);
                    /* new TestKit(sys1) with ImplicitSender */
                    {
                        var r = NewReplicator(sys1);
                        Within(TimeSpan.FromSeconds(10), () =>
                        {
                            AwaitAssert(() =>
                            {
                                r.Tell(Dsl.GetReplicaCount);
                                ExpectMsg(new ReplicaCount(1));
                            });
                        });

                        r.Tell(Dsl.Get(_keyA, ReadLocal.Instance));
                        ExpectMsg(new NotFound(_keyA, null));

                        TellUpdate(r, _keyA, WriteLocal.Instance, new PocoObject("Id_1", 5));
                        TellUpdate(r, _keyA, WriteLocal.Instance, new PocoObject("Id_1", 6));
                        TellUpdate(r, _keyA, WriteLocal.Instance, new PocoObject("Id_1", 7));
                        TellUpdate(r, _keyB, WriteLocal.Instance, new PocoObject("Id_1", 1));

                        ExpectMsg(new UpdateSuccess(_keyA, null));
                        ExpectMsg(new UpdateSuccess(_keyA, null));
                        ExpectMsg(new UpdateSuccess(_keyA, null));
                        ExpectMsg(new UpdateSuccess(_keyB, null));

                        Watch(r);
                        sys1.Stop(r);
                        ExpectTerminated(r);
                    }
                }
                finally
                {
                    sys1.Terminate().Wait(TimeSpan.FromSeconds(10));
                }
                
                var sys2 = ActorSystem.Create(
                    "AdditionalSys", 
                    ConfigurationFactory.ParseString($"akka.remote.dot-netty.tcp.port = {address.Port}")
                    .WithFallback(Sys.Settings.Config));
                try
                {
                    var cluster2 = Akka.Cluster.Cluster.Get(sys2);
                    cluster2.Join(address);
                    /* new TestKit(sys1) with ImplicitSender */
                    {
                        var r2 = NewReplicator(sys2);

                        // it should be possible to update while loading is in progress
                        TellUpdate(r2, _keyB, WriteLocal.Instance, new PocoObject("Id_1", 2));
                        ExpectMsg(new UpdateSuccess(_keyB, null));

                        // wait until all loaded
                        AwaitAssert(() =>
                        {
                            r2.Tell(Dsl.GetKeyIds);
                            ExpectMsg<GetKeysIdsResult>().Keys.ShouldBe(ImmutableHashSet.CreateRange(new [] { _keyA.Id, _keyB.Id }));   
                        });

                        r2.Tell(Dsl.Get(_keyA, ReadLocal.Instance));
                        var success = ExpectMsg<GetSuccess>().Get(_keyA);
                        success.TryGetValue("Id_1", out var value).Should().BeTrue();
                        value.TransactionId.Should().Be("Id_1");
                        value.Created.Should().Be(7);

                        r2.Tell(Dsl.Get(_keyB, ReadLocal.Instance));
                        success = ExpectMsg<GetSuccess>().Get(_keyB);
                        success.TryGetValue("Id_1", out value).Should().BeTrue();
                        value.TransactionId.Should().Be("Id_1");
                        value.Created.Should().Be(2);
                    }
                }
                finally
                {
                    sys2.Terminate().Wait(TimeSpan.FromSeconds(10));
                }

            }, _first);
            Log.Info("Setup complete");
            EnterBarrierAfterTestStep();
            Log.Info("All setup complete");
        }

        private void EnterBarrierAfterTestStep()
        {
            _testStepCounter++;
            EnterBarrier("after-" + _testStepCounter);
        }

        private IActorRef NewReplicator(ActorSystem system = null)
        {
            if (system == null) system = Sys;

            return system.ActorOf(Replicator.Props(
                    ReplicatorSettings.Create(system).WithGossipInterval(TimeSpan.FromSeconds(1))),
                "replicator-" + _testStepCounter);
        }

        private void Join(RoleName from, RoleName to)
        {
            RunOn(() =>
            {
                _cluster.Join(Node(to).Address);
            }, from);
            EnterBarrier(from.Name + "-joined");
        }
    }

    public class DurableDataPocoSpec : DurableDataPocoSpecBase
    {
        public DurableDataPocoSpec() : base(new DurableDataPocoSpecConfig(writeBehind: false), typeof(DurableDataPocoSpec)) { }
    }

    public class DurableDataWriteBehindPocoSpec : DurableDataPocoSpecBase
    {
        public DurableDataWriteBehindPocoSpec() : base(new DurableDataPocoSpecConfig(writeBehind: true), typeof(DurableDataWriteBehindPocoSpec)) { }
    }
}
