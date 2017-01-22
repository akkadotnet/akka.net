//-----------------------------------------------------------------------
// <copyright file="ReplicatorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.TestKit;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Remote.Transport;
using Akka.TestKit;

namespace Akka.DistributedData.Tests.MultiNode
{
    public class ReplicatorSpecConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }

        public ReplicatorSpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = DebugConfig(true).WithFallback(ConfigurationFactory.ParseString(@"
                akka.actor.provider=""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
                akka.test.timefactor=1.0
                akka.test.calling-thread-dispatcher.type=""Akka.TestKit.CallingThreadDispatcherConfigurator, Akka.TestKit""
                akka.test.calling-thread-dispatcher.throughput=2147483647
                akka.test.test-actor.dispatcher.type=""Akka.TestKit.CallingThreadDispatcherConfigurator, Akka.TestKit""
                akka.test.test-actor.dispatcher.throughput=2147483647
                akka.cluster.distributed-data.gossip-interval=2s
            ")).WithFallback(DistributedData.DefaultConfig());

            TestTransport = true;
        }
    }

    public abstract class ReplicatorSpec : MultiNodeSpec
    {
        private readonly ReplicatorSpecConfig _config;
        private readonly Cluster.Cluster _cluster;

        private readonly IActorRef _replicator;

        private readonly GCounterKey KeyA = new GCounterKey("A");
        private readonly GCounterKey KeyB = new GCounterKey("B");
        private readonly GCounterKey KeyC = new GCounterKey("C");
        private readonly GCounterKey KeyD = new GCounterKey("D");
        private readonly GCounterKey KeyE = new GCounterKey("E");
        private readonly GCounterKey KeyE2 = new GCounterKey("E2");
        private readonly GCounterKey KeyF = new GCounterKey("F");
        private readonly ORSetKey<string> KeyG = new ORSetKey<string>("G");
        private readonly ORDictionaryKey<string, Flag> KeyH = new ORDictionaryKey<string, Flag>("H");
        private readonly GSetKey<string> KeyI = new GSetKey<string>("I");
        private readonly GSetKey<string> KeyJ = new GSetKey<string>("J");
        private readonly GCounterKey KeyX = new GCounterKey("X");
        private readonly GCounterKey KeyY = new GCounterKey("Y");
        private readonly GCounterKey KeyZ = new GCounterKey("Z");

        private readonly TimeSpan _timeOut;
        private readonly WriteTo _writeTwo;
        private readonly WriteMajority _writeMajority;
        private readonly WriteAll _writeAll;
        private readonly ReadFrom _readTwo;
        private readonly ReadMajority _readMajority;
        private readonly ReadAll _readAll;

        private int afterCounter = 0;

        public ReplicatorSpec()
            : this(new ReplicatorSpecConfig())
        { }

        public ReplicatorSpec(ReplicatorSpecConfig config)
            : base(config)
        {
            _config = config;
            _cluster = Cluster.Cluster.Get(Sys);
            var settings = ReplicatorSettings.Create(Sys).WithGossipInterval(TimeSpan.FromSeconds(1.0)).WithMaxDeltaElements(10);
            var props = Replicator.Props(settings);
            _replicator = Sys.ActorOf(props, "replicator");

            _timeOut = Dilated(TimeSpan.FromSeconds(2.0));
            _writeTwo = new WriteTo(2, _timeOut);
            _writeMajority = new WriteMajority(_timeOut);
            _writeAll = new WriteAll(_timeOut);
            _readTwo = new ReadFrom(2, _timeOut);
            _readMajority = new ReadMajority(_timeOut);
            _readAll = new ReadAll(_timeOut);
        }

        //[MultiNodeFact]
        public void ReplicatorSpecTests()
        {
            Cluster_CRDT_should_work_in_single_node_cluster();
            Cluster_CRDT_should_merge_the_update_with_existing_value();
            Cluster_CRDT_should_reply_with_ModifyFailure_if_exception_is_thrown_by_modify_function();
            Cluster_CRDT_should_replicate_values_to_new_node();
            Cluster_CRDT_should_work_in_2_node_cluster();
            Cluster_CRDT_should_be_replicated_after_successful_update();
            Cluster_CRDT_should_converge_after_partition();
            Cluster_CRDT_should_support_majority_quorum_write_and_read_with_3_nodes_with_1_unreachable();
            Cluster_CRDT_should_converge_after_many_concurrent_updates();
            Cluster_CRDT_should_read_repair_happens_before_GetSuccess();
            Cluster_CRDT_should_check_that_remote_update_and_local_update_both_cause_a_change_event_to_emit_with_the_merged_data();
            Cluster_CRDT_should_avoid_duplicate_change_events_for_same_data();
        }

        public void Cluster_CRDT_should_work_in_single_node_cluster()
        {
            Join(_config.First, _config.First);

            RunOn(() =>
            {
                Within(TimeSpan.FromSeconds(5.0), () =>
                {
                    _replicator.Tell(Replicator.GetReplicaCount.Instance);
                    ExpectMsg<Replicator.ReplicaCount>();
                });

                var changedProbe = CreateTestProbe();
                _replicator.Tell(new Replicator.Subscribe(KeyA, changedProbe.Ref));
                _replicator.Tell(new Replicator.Subscribe(KeyX, changedProbe.Ref));

                Within(TimeSpan.FromSeconds(5.0), () =>
                {
                    _replicator.Tell(new Replicator.Get(KeyA, ReadLocal.Instance));
                    ExpectMsg(new Replicator.NotFound(KeyA, null));
                });

                var c3 = GCounter.Empty.Increment(_cluster.SelfUniqueAddress, 3);
                var update = new Replicator.Update(KeyA, GCounter.Empty, WriteLocal.Instance, x => ((GCounter)x).Increment(_cluster.SelfUniqueAddress, 3));
                _replicator.Tell(update);
                ExpectMsg(new Replicator.UpdateSuccess(KeyA, null));
                changedProbe.ExpectMsg(new Replicator.Changed(KeyA, c3));
                _replicator.Tell(new Replicator.Get(KeyA, ReadLocal.Instance));
                ExpectMsg(new Replicator.GetSuccess(KeyA, null, c3));

                var changedProbe2 = CreateTestProbe();
                _replicator.Tell(new Replicator.Subscribe(KeyA, changedProbe2.Ref));
                changedProbe2.ExpectMsg(new Replicator.Changed(KeyA, c3));


                var c4 = c3.Increment(_cluster.SelfUniqueAddress);
                _replicator.Tell(new Replicator.Update(KeyA, _writeTwo, x => ((GCounter)x).Increment(_cluster.SelfUniqueAddress)));
                ExpectMsg(new Replicator.UpdateTimeout(KeyA, null));
                _replicator.Tell(new Replicator.Get(KeyA, ReadLocal.Instance));
                ExpectMsg(new Replicator.GetSuccess(KeyA, null, c4));
                changedProbe.ExpectMsg(new Replicator.Changed(KeyA, c4));

                var c5 = c4.Increment(_cluster.SelfUniqueAddress);
                _replicator.Tell(new Replicator.Update(KeyA, _writeMajority, x => ((GCounter)x).Increment(_cluster.SelfUniqueAddress)));
                ExpectMsg(new Replicator.UpdateSuccess(KeyA, null));
                _replicator.Tell(new Replicator.Get(KeyA, _readMajority));
                ExpectMsg(new Replicator.GetSuccess(KeyA, null, c5));
                changedProbe.ExpectMsg(new Replicator.Changed(KeyA, c5));

                var c6 = c5.Increment(_cluster.SelfUniqueAddress);
                _replicator.Tell(new Replicator.Update(KeyA, _writeAll, x => ((GCounter)x).Increment(_cluster.SelfUniqueAddress)));
                ExpectMsg(new Replicator.UpdateSuccess(KeyA, null));
                _replicator.Tell(new Replicator.Get(KeyA, _readAll));
                ExpectMsg(new Replicator.GetSuccess(KeyA, null, c6));
                changedProbe.ExpectMsg(new Replicator.Changed(KeyA, c6));

                var c9 = GCounter.Empty.Increment(_cluster.SelfUniqueAddress, 9);
                _replicator.Tell(new Replicator.Update(KeyX, GCounter.Empty, WriteLocal.Instance, x => ((GCounter)x).Increment(_cluster.SelfUniqueAddress, 9)));
                ExpectMsg(new Replicator.UpdateSuccess(KeyX, null));
                changedProbe.ExpectMsg(new Replicator.Changed(KeyX, c9));
                _replicator.Tell(new Replicator.Delete(KeyX, WriteLocal.Instance));
                ExpectMsg(new Replicator.DeleteSuccess(KeyX));
                changedProbe.ExpectMsg(new Replicator.DataDeleted(KeyX), TimeSpan.FromMinutes(5.0));
                _replicator.Tell(new Replicator.Get(KeyX, ReadLocal.Instance));
                ExpectMsg(new Replicator.DataDeleted(KeyX));
                _replicator.Tell(new Replicator.Get(KeyX, _readAll));
                ExpectMsg(new Replicator.DataDeleted(KeyX));
                _replicator.Tell(new Replicator.Update(KeyX, WriteLocal.Instance, x => ((GCounter)x).Increment(_cluster.SelfUniqueAddress)));
                ExpectMsg(new Replicator.DataDeleted(KeyX));
                _replicator.Tell(new Replicator.Delete(KeyX, WriteLocal.Instance));
                ExpectMsg(new Replicator.DataDeleted(KeyX));

                _replicator.Tell(Replicator.GetKeyIds.Instance);
                ExpectMsg(new Replicator.GetKeysIdsResult(ImmutableHashSet<string>.Empty.Add("A")));
            }, _config.First);

            EnterBarrierAfterTestStep();
        }

        public void Cluster_CRDT_should_merge_the_update_with_existing_value()
        {
            RunOn(() =>
            {
                var update = new Replicator.Update(KeyJ, new GSet<string>(), WriteLocal.Instance, x => ((GSet<string>)x).Add("a").Add("b"));
                _replicator.Tell(update);
                ExpectMsg(new Replicator.UpdateSuccess(KeyJ, null));
                var update2 = new Replicator.Update(KeyJ, new GSet<string>(), WriteLocal.Instance, x => ((GSet<string>)x).Add("c"));
                _replicator.Tell(update2);
                ExpectMsg(new Replicator.UpdateSuccess(KeyJ, null));
                _replicator.Tell(new Replicator.Get(KeyJ, ReadLocal.Instance));
                ExpectMsg<Replicator.GetSuccess>(x => x.Data.Equals(new GSet<string>(new[] { "a", "b", "c" }.ToImmutableHashSet())));
            }, _config.First);

            EnterBarrierAfterTestStep();
        }

        public void Cluster_CRDT_should_reply_with_ModifyFailure_if_exception_is_thrown_by_modify_function()
        {
            RunOn(() =>
            {
                var exception = new Exception("Test exception");
                Func<IReplicatedData, IReplicatedData> update = x =>
                {
                    throw exception;
                };
                _replicator.Tell(new Replicator.Update(KeyA, GCounter.Empty, WriteLocal.Instance, update));
                ExpectMsg<Replicator.ModifyFailure>(x => x.Cause.Equals(exception));
            }, _config.First);

            EnterBarrierAfterTestStep();
        }

        public void Cluster_CRDT_should_replicate_values_to_new_node()
        {
            Join(_config.Second, _config.First);

            RunOn(() =>
                Within(TimeSpan.FromSeconds(10), () =>
                    AwaitAssert(() =>
                    {
                        _replicator.Tell(Dsl.GetReplicaCount);
                        ExpectMsg(new Replicator.ReplicaCount(2));
                    })),
            _config.First, _config.Second);
        }

        public void Cluster_CRDT_should_work_in_2_node_cluster()
        {
            RunOn(() =>
            {
                // start with 20 on both nodes
                _replicator.Tell(Dsl.Update(KeyB, GCounter.Empty, WriteLocal.Instance, x => x.Increment(_cluster.SelfUniqueAddress, 20)));
                ExpectMsg(new Replicator.UpdateSuccess(KeyB, null));

                // add 1 on both nodes using WriteTwo
                _replicator.Tell(Dsl.Update(KeyB, GCounter.Empty, _writeTwo, x => x.Increment(_cluster.SelfUniqueAddress, 1)));
                ExpectMsg(new Replicator.UpdateSuccess(KeyB, null));

                // the total, after replication should be 42
                AwaitAssert(() =>
                {
                    _replicator.Tell(Dsl.Get(KeyB, _readTwo));
                    var c = ExpectMsg<Replicator.GetSuccess>(g => Equals(g.Key, KeyB)).Get(KeyB);
                    c.Value.ShouldBe(42);
                });
            }, _config.First, _config.Second);

            EnterBarrier("update-42");

            RunOn(() =>
            {
                // add 1 on both nodes using WriteAll
                _replicator.Tell(Dsl.Update(KeyB, GCounter.Empty, _writeAll, x => x.Increment(_cluster.SelfUniqueAddress, 1)));
                ExpectMsg(new Replicator.UpdateSuccess(KeyB, null));

                // the total, after replication should be 44
                AwaitAssert(() =>
                {
                    _replicator.Tell(Dsl.Get(KeyB, _readAll));
                    var c = ExpectMsg<Replicator.GetSuccess>(g => Equals(g.Key, KeyB)).Get(KeyB);
                    c.Value.ShouldBe(44);
                });
            }, _config.First, _config.Second);

            EnterBarrier("update-44");

            RunOn(() =>
            {
                // add 1 on both nodes using WriteMajority
                _replicator.Tell(Dsl.Update(KeyB, GCounter.Empty, _writeMajority, x => x.Increment(_cluster.SelfUniqueAddress, 1)));
                ExpectMsg(new Replicator.UpdateSuccess(KeyB, null));

                // the total, after replication should be 46
                AwaitAssert(() =>
                {
                    _replicator.Tell(Dsl.Get(KeyB, _readMajority));
                    var c = ExpectMsg<Replicator.GetSuccess>(g => Equals(g.Key, KeyB)).Get(KeyB);
                    c.Value.ShouldBe(46);
                });
            }, _config.First, _config.Second);

            EnterBarrierAfterTestStep();
        }

        public void Cluster_CRDT_should_be_replicated_after_successful_update()
        {
            var changedProbe = CreateTestProbe();
            RunOn(() =>
            {
                _replicator.Tell(Dsl.Subscribe(KeyC, changedProbe.Ref));
            }, _config.First, _config.Second);

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Update(KeyC, GCounter.Empty, _writeTwo, x => x.Increment(_cluster.SelfUniqueAddress, 30)));
                ExpectMsg(new Replicator.UpdateSuccess(KeyC, null));
                changedProbe.ExpectMsg<Replicator.Changed>(c => Equals(c.Key, KeyC)).Get(KeyC).Value.ShouldBe(30);

                _replicator.Tell(Dsl.Update(KeyY, GCounter.Empty, _writeTwo, x => x.Increment(_cluster.SelfUniqueAddress, 30)));
                ExpectMsg(new Replicator.UpdateSuccess(KeyY, null));

                _replicator.Tell(Dsl.Update(KeyZ, GCounter.Empty, _writeMajority, x => x.Increment(_cluster.SelfUniqueAddress, 30)));
                ExpectMsg(new Replicator.UpdateSuccess(KeyZ, null));
            }, _config.First);

            EnterBarrier("update-c30");

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Get(KeyC, ReadLocal.Instance));
                var c30 = ExpectMsg<Replicator.GetSuccess>(c => Equals(c.Key, KeyC)).Get(KeyC);
                c30.Value.ShouldBe(30);

                // replicate with gossip after WriteLocal
                _replicator.Tell(Dsl.Update(KeyC, GCounter.Empty, WriteLocal.Instance, x => x.Increment(_cluster.SelfUniqueAddress, 1)));
                ExpectMsg(new Replicator.UpdateSuccess(KeyC, null));
                changedProbe.ExpectMsg<Replicator.Changed>(c => Equals(c.Key, KeyC)).Get(KeyC).Value.ShouldBe(31);

                _replicator.Tell(Dsl.Delete(KeyY, WriteLocal.Instance));
                ExpectMsg(new Replicator.DeleteSuccess(KeyY));

                _replicator.Tell(Dsl.Get(KeyZ, _readMajority));
                changedProbe.ExpectMsg<Replicator.Changed>(c => Equals(c.Key, KeyZ)).Get(KeyZ).Value.ShouldBe(30);
            }, _config.Second);

            EnterBarrier("update-c31");

            RunOn(() =>
            {
                // KeyC and deleted KeyY should be replicated via gossip to the other node
                Within(TimeSpan.FromSeconds(5), () =>
                {
                    AwaitAssert(() =>
                    {
                        _replicator.Tell(Dsl.Get(KeyC, ReadLocal.Instance));
                        var c = ExpectMsg<Replicator.GetSuccess>(g => Equals(g.Key, KeyC)).Get(KeyC);
                        c.Value.ShouldBe(31);

                        _replicator.Tell(Dsl.Get(KeyY, ReadLocal.Instance));
                        ExpectMsg(new Replicator.DataDeleted(KeyY));
                    });
                });
                changedProbe.ExpectMsg<Replicator.Changed>(c => Equals(c.Key, KeyC)).Get(KeyC).Value.ShouldBe(31);
            }, _config.First);

            EnterBarrier("verified-c31");

            // and also for concurrent updates
            RunOn(() =>
            {
                _replicator.Tell(Dsl.Get(KeyC, ReadLocal.Instance));
                var c31 = ExpectMsg<Replicator.GetSuccess>(g => Equals(g.Key, KeyC)).Get(KeyC);
                c31.Value.ShouldBe(31);

                _replicator.Tell(Dsl.Update(KeyC, GCounter.Empty, WriteLocal.Instance, x => x.Increment(_cluster.SelfUniqueAddress, 1)));
                ExpectMsg(new Replicator.UpdateSuccess(KeyC, null));

                Within(TimeSpan.FromSeconds(5), () =>
                {
                    AwaitAssert(() =>
                    {
                        _replicator.Tell(Dsl.Get(KeyC, ReadLocal.Instance));
                        var c = ExpectMsg<Replicator.GetSuccess>(g => Equals(g.Key, KeyC)).Get(KeyC);
                        c.Value.ShouldBe(33);
                    });
                });
            }, _config.First, _config.Second);

            EnterBarrierAfterTestStep();
        }

        public void Cluster_CRDT_should_converge_after_partition()
        {
            RunOn(() =>
            {
                _replicator.Tell(Dsl.Update(KeyD, GCounter.Empty, _writeTwo, x => x.Increment(_cluster.SelfUniqueAddress, 40)));
                ExpectMsg(new Replicator.UpdateSuccess(KeyD, null));

                TestConductor.Blackhole(_config.First, _config.Second, ThrottleTransportAdapter.Direction.Both)
                    .Wait(TimeSpan.FromSeconds(10));
            }, _config.First);

            EnterBarrier("blackhole-first-second");

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Get(KeyD, ReadLocal.Instance));
                var c40 = ExpectMsg<Replicator.GetSuccess>(g => Equals(g.Key, KeyD)).Get(KeyD);
                c40.Value.ShouldBe(40);

                _replicator.Tell(Dsl.Update(KeyD, GCounter.Empty.Increment(_cluster.SelfUniqueAddress, 1), _writeTwo, x => x.Increment(_cluster.SelfUniqueAddress, 1)));
                ExpectMsg(new Replicator.UpdateTimeout(KeyD, null), _timeOut.Add(TimeSpan.FromSeconds(1)));
                _replicator.Tell(Dsl.Update(KeyD, GCounter.Empty, _writeTwo, x => x.Increment(_cluster.SelfUniqueAddress, 1)));
                ExpectMsg(new Replicator.UpdateTimeout(KeyD, null), _timeOut.Add(TimeSpan.FromSeconds(1)));
            }, _config.First, _config.Second);

            RunOn(() =>
            {
                for (int i = 1; i <= 30; i++)
                {
                    var keydn = new GCounterKey("D" + i);
                    _replicator.Tell(Dsl.Update(keydn, GCounter.Empty, WriteLocal.Instance, x => x.Increment(_cluster.SelfUniqueAddress, i)));
                    ExpectMsg(new Replicator.UpdateSuccess(keydn, null));
                }
            }, _config.First);

            EnterBarrier("updates-during-partion");

            RunOn(() =>
            {
                TestConductor.PassThrough(_config.First, _config.Second, ThrottleTransportAdapter.Direction.Both)
                    .Wait(TimeSpan.FromSeconds(5));
            }, _config.First);

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Get(KeyD, _readTwo));
                var c44 = ExpectMsg<Replicator.GetSuccess>(g => Equals(g.Key, KeyD)).Get(KeyD);
                c44.Value.ShouldBe(44);

                Within(TimeSpan.FromSeconds(10), () =>
                {
                    AwaitAssert(() =>
                    {
                        for (int i = 1; i <= 30; i++)
                        {
                            var keydn = new GCounterKey("D" + i);
                            _replicator.Tell(Dsl.Get(keydn, ReadLocal.Instance));
                            ExpectMsg<Replicator.GetSuccess>(g => Equals(g.Key, keydn)).Get(KeyD).Value.ShouldBe(i);
                        }
                    });
                });
            }, _config.First, _config.Second);

            EnterBarrierAfterTestStep();
        }

        public void Cluster_CRDT_should_support_majority_quorum_write_and_read_with_3_nodes_with_1_unreachable()
        {
            Join(_config.First, _config.Third);

            RunOn(() =>
            {
                Within(TimeSpan.FromSeconds(10), () => AwaitAssert(() =>
                {
                    _replicator.Tell(Dsl.GetReplicaCount);
                    ExpectMsg(new Replicator.ReplicaCount(3));
                }));
            }, _config.First, _config.Second, _config.Third);

            EnterBarrier("3-nodes");

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Update(KeyE, GCounter.Empty, _writeMajority, x => x.Increment(_cluster.SelfUniqueAddress, 50)));
                ExpectMsg(new Replicator.UpdateSuccess(KeyE, null));
            }, _config.First, _config.Second, _config.Third);

            EnterBarrier("write-initial-majority");

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Get(KeyE, _readMajority));
                var c150 = ExpectMsg<Replicator.GetSuccess>(g => Equals(g.Key, KeyE)).Get(KeyE);
                c150.Value.ShouldBe(150);
            }, _config.First, _config.Second, _config.Third);

            EnterBarrier("read-initial-majority");

            RunOn(() =>
            {
                TestConductor.Blackhole(_config.First, _config.Third, ThrottleTransportAdapter.Direction.Both).Wait(TimeSpan.FromSeconds(5));
                TestConductor.Blackhole(_config.Second, _config.Third, ThrottleTransportAdapter.Direction.Both).Wait(TimeSpan.FromSeconds(5));
            }, _config.First);

            EnterBarrier("blackhole-third");

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Update(KeyE, GCounter.Empty, WriteLocal.Instance, x => x.Increment(_cluster.SelfUniqueAddress, 1)));
                ExpectMsg(new Replicator.UpdateSuccess(KeyE, null));
            }, _config.Second);

            EnterBarrier("local-update-from-second");

            RunOn(() =>
            {
                // ReadMajority should retrive the previous update from second, before applying the modification
                var probe1 = CreateTestProbe();
                var probe2 = CreateTestProbe();
                _replicator.Tell(Dsl.Get(KeyE, _readMajority), probe2.Ref);
                probe2.ExpectMsg<Replicator.GetSuccess>();
                _replicator.Tell(Dsl.Update(KeyE, GCounter.Empty, _writeMajority, data =>
                {
                    probe1.Ref.Tell(data.Value);
                    return data.Increment(_cluster.SelfUniqueAddress, 1);
                }), probe2.Ref);

                // verify read your own writes, without waiting for the UpdateSuccess reply
                // note that the order of the replies are not defined, and therefore we use separate probes
                var probe3 = CreateTestProbe();
                _replicator.Tell(Dsl.Get(KeyE, _readMajority), probe3.Ref);
                probe1.ExpectMsg(151);
                probe2.ExpectMsg(new Replicator.UpdateSuccess(KeyE, null));
                var c152 = ExpectMsg<Replicator.GetSuccess>(g => Equals(g.Key, KeyE)).Get(KeyE);
                c152.Value.ShouldBe(152);
            }, _config.First);

            EnterBarrier("majority-update-from-first");

            RunOn(() =>
            {
                var probe1 = CreateTestProbe();
                _replicator.Tell(Dsl.Get(KeyE, _readMajority), probe1.Ref);
                probe1.ExpectMsg<Replicator.GetSuccess>();
                _replicator.Tell(Dsl.Update(KeyE, GCounter.Empty, _writeMajority, 153, x => x.Increment(_cluster.SelfUniqueAddress, 1)), probe1.Ref);

                // verify read your own writes, without waiting for the UpdateSuccess reply
                // note that the order of the replies are not defined, and therefore we use separate probes
                var probe2 = CreateTestProbe();
                _replicator.Tell(Dsl.Update(KeyE, GCounter.Empty, _writeMajority, 154, x => x.Increment(_cluster.SelfUniqueAddress, 1)), probe2.Ref);
                var probe3 = CreateTestProbe();
                _replicator.Tell(Dsl.Update(KeyE, GCounter.Empty, _writeMajority, 155, x => x.Increment(_cluster.SelfUniqueAddress, 1)), probe3.Ref);
                var probe5 = CreateTestProbe();
                _replicator.Tell(Dsl.Get(KeyE, _readMajority), probe5.Ref);
                probe1.ExpectMsg(new Replicator.UpdateSuccess(KeyE, 153));
                probe2.ExpectMsg(new Replicator.UpdateSuccess(KeyE, 154));
                probe3.ExpectMsg(new Replicator.UpdateSuccess(KeyE, 155));
                var c155 = ExpectMsg<Replicator.GetSuccess>(g => Equals(g.Key, KeyE)).Get(KeyE);
                c155.Value.ShouldBe(155);
            }, _config.Second);

            EnterBarrier("majority-update-from-second");

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Get(KeyE2, _readAll, 998));
                ExpectMsg(new Replicator.GetFailure(KeyE2, 998), _timeOut.Add(TimeSpan.FromSeconds(1)));
                _replicator.Tell(Dsl.Get(KeyE2, _readAll));
                ExpectMsg(new Replicator.NotFound(KeyE2, null));
            }, _config.First, _config.Second);

            EnterBarrier("read-all-fail-update");

            RunOn(() =>
            {
                TestConductor.PassThrough(_config.First, _config.Third, ThrottleTransportAdapter.Direction.Both).Wait(TimeSpan.FromSeconds(5));
                TestConductor.PassThrough(_config.Second, _config.Third, ThrottleTransportAdapter.Direction.Both).Wait(TimeSpan.FromSeconds(5));
            }, _config.First);

            EnterBarrier("passThrough-third");

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Get(KeyE, _readMajority));
                var c155 = ExpectMsg<Replicator.GetSuccess>(g => Equals(g.Key, KeyE)).Get(KeyE);
                c155.Value.ShouldBe(155);
            }, _config.Third);

            EnterBarrierAfterTestStep();
        }

        public void Cluster_CRDT_should_converge_after_many_concurrent_updates()
        {
            Within(TimeSpan.FromSeconds(10), () =>
            {
                RunOn(() =>
                {
                    var c = GCounter.Empty;
                    for (int i = 0; i < 100; i++)
                    {
                        c = c.Increment(_cluster.SelfUniqueAddress, i);
                        _replicator.Tell(Dsl.Update(KeyF, GCounter.Empty, _writeTwo, x => x.Increment(_cluster.SelfUniqueAddress, 1)));
                    }

                    var results = ReceiveN(100);
                    results.All(x => x is Replicator.UpdateSuccess).ShouldBeTrue();
                }, _config.First, _config.Second, _config.Third);

                EnterBarrier("100-updates-done");

                RunOn(() =>
                {
                    _replicator.Tell(Dsl.Get(KeyF, _readTwo));
                    var c = ExpectMsg<Replicator.GetSuccess>(g => Equals(g.Key, KeyF)).Get(KeyF);
                    c.Value.ShouldBe(3 * 100);
                }, _config.First, _config.Second, _config.Third);

                EnterBarrierAfterTestStep();
            });
        }

        public void Cluster_CRDT_should_read_repair_happens_before_GetSuccess()
        {
            RunOn(() =>
            {
                _replicator.Tell(Dsl.Update(KeyG, ORSet<string>.Empty, _writeTwo, x => x
                    .Add(_cluster.SelfUniqueAddress, "a")
                    .Add(_cluster.SelfUniqueAddress, "b")));
                ExpectMsg<Replicator.UpdateSuccess>();
            }, _config.First);

            EnterBarrier("a-b-added-to-G");

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Get(KeyG, _readAll));
                ExpectMsg<Replicator.GetSuccess>(g => Equals(g.Key, KeyG)).Get(KeyG).Elements.SetEquals(new[] { "a", "b" });
                _replicator.Tell(Dsl.Get(KeyG, ReadLocal.Instance));
                ExpectMsg<Replicator.GetSuccess>(g => Equals(g.Key, KeyG)).Get(KeyG).Elements.SetEquals(new[] { "a", "b" });
            }, _config.Second);

            EnterBarrierAfterTestStep();
        }

        public void Cluster_CRDT_should_check_that_remote_update_and_local_update_both_cause_a_change_event_to_emit_with_the_merged_data()
        {
            var changedProbe = CreateTestProbe();

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Subscribe(KeyH, changedProbe.Ref));
                _replicator.Tell(Dsl.Update(KeyH, ORDictionary<string, Flag>.Empty, _writeTwo, x => x.SetItem(_cluster.SelfUniqueAddress, "a", Flag.False)));
                ExpectMsg<Replicator.GetSuccess>(g => Equals(g.Key, KeyH)).Get(KeyH).Entries.SequenceEqual(ImmutableDictionary.CreateRange(new[]
                {
                    new KeyValuePair<string, Flag>("a", Flag.False),
                })).ShouldBeTrue();
            }, _config.Second);

            EnterBarrier("update-h1");

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Update(KeyH, ORDictionary<string, Flag>.Empty, _writeTwo, x => x.SetItem(_cluster.SelfUniqueAddress, "a", Flag.True)));
            }, _config.First);

            RunOn(() =>
            {
                changedProbe.ExpectMsg<Replicator.GetSuccess>(g => Equals(g.Key, KeyH)).Get(KeyH).Entries.SequenceEqual(ImmutableDictionary.CreateRange(new[]
                {
                    new KeyValuePair<string, Flag>("a", Flag.True)
                })).ShouldBeTrue();

                _replicator.Tell(Dsl.Update(KeyH, ORDictionary<string, Flag>.Empty, _writeTwo, x => x.SetItem(_cluster.SelfUniqueAddress, "b", Flag.True)));
                changedProbe.ExpectMsg<Replicator.GetSuccess>(g => Equals(g.Key, KeyH)).Get(KeyH).Entries.SequenceEqual(ImmutableDictionary.CreateRange(new[]
                {
                    new KeyValuePair<string, Flag>("a", Flag.True),
                    new KeyValuePair<string, Flag>("b", Flag.True)
                })).ShouldBeTrue();
            }, _config.Second);

            EnterBarrierAfterTestStep();
        }

        public void Cluster_CRDT_should_avoid_duplicate_change_events_for_same_data()
        {
            var changedProbe = CreateTestProbe();
            _replicator.Tell(Dsl.Subscribe(KeyI, changedProbe.Ref));

            EnterBarrier("subscribed-I");

            RunOn(() => _replicator.Tell(Dsl.Update(KeyI, GSet<string>.Empty, _writeTwo, a => a.Add("a"))),
                _config.Second);

            Within(TimeSpan.FromSeconds(5), () =>
                changedProbe.ExpectMsg<Replicator.Changed>(c => c.Get(KeyI).Elements.ShouldBe(ImmutableHashSet.Create("a"))));

            EnterBarrier("update-I");

            RunOn(() => _replicator.Tell(Dsl.Update(KeyI, GSet<string>.Empty, _writeTwo, a => a.Add("a"))),
                _config.First);

            changedProbe.ExpectNoMsg(TimeSpan.FromSeconds(1));

            EnterBarrierAfterTestStep();

        }

        protected override int InitialParticipantsValueFactory => Roles.Count;
        private void EnterBarrierAfterTestStep()
        {
            afterCounter++;
            EnterBarrier("after-" + afterCounter);
        }

        private void Join(RoleName from, RoleName to)
        {
            RunOn(() => _cluster.Join(Node(to).Address), from);
            EnterBarrier(from.Name + "-joined");
        }
    }

    public class ReplicatorSpecNode1 : ReplicatorSpec
    { }

    public class ReplicatorSpecNode2 : ReplicatorSpec
    { }

    public class ReplicatorSpecNode3 : ReplicatorSpec
    { }
}
