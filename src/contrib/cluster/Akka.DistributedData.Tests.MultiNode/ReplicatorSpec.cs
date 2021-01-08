//-----------------------------------------------------------------------
// <copyright file="ReplicatorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.TestKit;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Cluster;
using Akka.Cluster.TestKit;
using Akka.Remote.Transport;
using Akka.TestKit;
using FluentAssertions;

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

            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.actor.provider = cluster
                akka.loglevel = INFO
                akka.log-dead-letters-during-shutdown = off
            ").WithFallback(DistributedData.DefaultConfig()).WithFallback(DebugConfig(false));

            TestTransport = true;
        }
    }

    public class ReplicatorSpec : MultiNodeClusterSpec
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

        private int _afterCounter = 0;

        private readonly RoleName _first;
        private readonly RoleName _second;
        private readonly RoleName _third;

        public ReplicatorSpec()
            : this(new ReplicatorSpecConfig())
        { }

        protected ReplicatorSpec(ReplicatorSpecConfig config)
            : base(config, typeof(ReplicatorSpec))
        {
            _config = config;
            _first = config.First;
            _second = config.Second;
            _third = config.Third;
            _cluster = Akka.Cluster.Cluster.Get(Sys);
            var settings = ReplicatorSettings.Create(Sys)
                .WithGossipInterval(TimeSpan.FromSeconds(1.0))
                .WithMaxDeltaElements(10);
            var props = Replicator.Props(settings);
            _replicator = Sys.ActorOf(props, "replicator");

            _timeOut = Dilated(TimeSpan.FromSeconds(3.0));
            _writeTwo = new WriteTo(2, _timeOut);
            _writeMajority = new WriteMajority(_timeOut);
            _writeAll = new WriteAll(_timeOut);
            _readTwo = new ReadFrom(2, _timeOut);
            _readMajority = new ReadMajority(_timeOut);
            _readAll = new ReadAll(_timeOut);
        }

        [MultiNodeFact()]
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
            Join(_first, _first);

            RunOn(() =>
            {
                Within(TimeSpan.FromSeconds(5.0), () =>
                {
                    _replicator.Tell(Dsl.GetReplicaCount);
                    ExpectMsg(new ReplicaCount(1));
                });

                var changedProbe = CreateTestProbe();
                _replicator.Tell(Dsl.Subscribe(KeyA, changedProbe.Ref));
                _replicator.Tell(Dsl.Subscribe(KeyX, changedProbe.Ref));

                _replicator.Tell(Dsl.Get(KeyA, ReadLocal.Instance));
                ExpectMsg(new NotFound(KeyA, null));

                var c3 = GCounter.Empty.Increment(_cluster, 3);
                var update = Dsl.Update(KeyA, GCounter.Empty, WriteLocal.Instance, x => x.Increment(_cluster, 3));
                _replicator.Tell(update);
                ExpectMsg(new UpdateSuccess(KeyA, null));
                _replicator.Tell(Dsl.Get(KeyA, ReadLocal.Instance));
                ExpectMsg(new GetSuccess(KeyA, null, c3));
                changedProbe.ExpectMsg(new Changed(KeyA, c3));

                var changedProbe2 = CreateTestProbe();
                _replicator.Tell(new Subscribe(KeyA, changedProbe2.Ref));
                changedProbe2.ExpectMsg(new Changed(KeyA, c3));


                var c4 = c3.Increment(_cluster);
                // too strong consistency level
                _replicator.Tell(Dsl.Update(KeyA, _writeTwo, x => x.Increment(_cluster)));
                ExpectMsg(new UpdateTimeout(KeyA, null), _timeOut.Add(TimeSpan.FromSeconds(1)));
                _replicator.Tell(Dsl.Get(KeyA, ReadLocal.Instance));
                ExpectMsg(new GetSuccess(KeyA, null, c4));
                changedProbe.ExpectMsg(new Changed(KeyA, c4));

                var c5 = c4.Increment(_cluster);
                // too strong consistency level
                _replicator.Tell(Dsl.Update(KeyA, _writeMajority, x => x.Increment(_cluster)));
                ExpectMsg(new UpdateSuccess(KeyA, null));
                _replicator.Tell(Dsl.Get(KeyA, _readMajority));
                ExpectMsg(new GetSuccess(KeyA, null, c5));
                changedProbe.ExpectMsg(new Changed(KeyA, c5));

                var c6 = c5.Increment(_cluster);
                _replicator.Tell(Dsl.Update(KeyA, _writeAll, x => x.Increment(_cluster)));
                ExpectMsg(new UpdateSuccess(KeyA, null));
                _replicator.Tell(Dsl.Get(KeyA, _readAll));
                ExpectMsg(new GetSuccess(KeyA, null, c6));
                changedProbe.ExpectMsg(new Changed(KeyA, c6));

                var c9 = GCounter.Empty.Increment(_cluster, 9);
                _replicator.Tell(Dsl.Update(KeyX, GCounter.Empty, WriteLocal.Instance, x => x.Increment(_cluster, 9)));
                ExpectMsg(new UpdateSuccess(KeyX, null));
                changedProbe.ExpectMsg(new Changed(KeyX, c9));
                _replicator.Tell(Dsl.Delete(KeyX, WriteLocal.Instance));
                ExpectMsg(new DeleteSuccess(KeyX));
                changedProbe.ExpectMsg(new DataDeleted(KeyX));
                _replicator.Tell(Dsl.Get(KeyX, ReadLocal.Instance));
                ExpectMsg(new DataDeleted(KeyX));
                _replicator.Tell(Dsl.Get(KeyX, _readAll));
                ExpectMsg(new DataDeleted(KeyX));
                _replicator.Tell(Dsl.Update(KeyX, WriteLocal.Instance, x => x.Increment(_cluster)));
                ExpectMsg(new DataDeleted(KeyX));
                _replicator.Tell(Dsl.Delete(KeyX, WriteLocal.Instance));
                ExpectMsg(new DataDeleted(KeyX));

                _replicator.Tell(Dsl.GetKeyIds);
                ExpectMsg(new GetKeysIdsResult(ImmutableHashSet<string>.Empty.Add("A")));
            }, _first);

            EnterBarrierAfterTestStep();
        }

        public void Cluster_CRDT_should_merge_the_update_with_existing_value()
        {
            RunOn(() =>
            {
                var update = new Update(KeyJ, new GSet<string>(), WriteLocal.Instance, x => ((GSet<string>)x).Add("a").Add("b"));
                _replicator.Tell(update);
                ExpectMsg(new UpdateSuccess(KeyJ, null));
                var update2 = new Update(KeyJ, new GSet<string>(), WriteLocal.Instance, x => ((GSet<string>)x).Add("c"));
                _replicator.Tell(update2);
                ExpectMsg(new UpdateSuccess(KeyJ, null));
                _replicator.Tell(new Get(KeyJ, ReadLocal.Instance));
                ExpectMsg<GetSuccess>(x => x.Data.Equals(new GSet<string>(new[] { "a", "b", "c" }.ToImmutableHashSet())));
            }, _first);

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
                _replicator.Tell(new Update(KeyA, GCounter.Empty, WriteLocal.Instance, update));
                ExpectMsg<ModifyFailure>(x => x.Cause.Equals(exception));
            }, _first);

            EnterBarrierAfterTestStep();
        }

        public void Cluster_CRDT_should_replicate_values_to_new_node()
        {
            Join(_second, _first);

            RunOn(() =>
                Within(TimeSpan.FromSeconds(10), () =>
                    AwaitAssert(() =>
                    {
                        _replicator.Tell(Dsl.GetReplicaCount);
                        ExpectMsg(new ReplicaCount(2));
                    })),
            _first, _second);

            EnterBarrier("2-nodes");

            RunOn(() =>
            {
                var changedProbe = CreateTestProbe();
                _replicator.Tell(Dsl.Subscribe(KeyA, changedProbe.Ref));
                // "A" should be replicated via gossip to the new node
                Within(TimeSpan.FromSeconds(5), () =>
                    AwaitAssert(() =>
                    {
                        // for some reason result is returned before CRDT gets replicated
                        _replicator.Tell(Dsl.Get(KeyA, ReadLocal.Instance));
                        var c = ExpectMsg<GetSuccess>(g => Equals(g.Key, KeyA)).Get(KeyA);
                        c.Value.ShouldBe(6UL);
                    }));
                var c2 = changedProbe.ExpectMsg<Changed>(g => Equals(g.Key, KeyA)).Get(KeyA);
                c2.Value.ShouldBe(6UL);
            }, _second);

            EnterBarrierAfterTestStep();
        }

        public void Cluster_CRDT_should_work_in_2_node_cluster()
        {
            RunOn(() =>
            {
                // start with 20 on both nodes
                _replicator.Tell(Dsl.Update(KeyB, GCounter.Empty, WriteLocal.Instance, x => x.Increment(_cluster, 20)));
                ExpectMsg(new UpdateSuccess(KeyB, null));

                // add 1 on both nodes using WriteTwo
                _replicator.Tell(Dsl.Update(KeyB, GCounter.Empty, _writeTwo, x => x.Increment(_cluster, 1)));
                ExpectMsg(new UpdateSuccess(KeyB, null));

                // the total, after replication should be 42
                AwaitAssert(() =>
                {
                    _replicator.Tell(Dsl.Get(KeyB, _readTwo));
                    var c = ExpectMsg<GetSuccess>(g => Equals(g.Key, KeyB)).Get(KeyB);
                    c.Value.ShouldBe(42UL);
                });
            }, _first, _second);

            EnterBarrier("update-42");

            RunOn(() =>
            {
                // add 1 on both nodes using WriteAll
                _replicator.Tell(Dsl.Update(KeyB, GCounter.Empty, _writeAll, x => x.Increment(_cluster, 1)));
                ExpectMsg(new UpdateSuccess(KeyB, null));

                // the total, after replication should be 44
                AwaitAssert(() =>
                {
                    _replicator.Tell(Dsl.Get(KeyB, _readAll));
                    var c = ExpectMsg<GetSuccess>(g => Equals(g.Key, KeyB)).Get(KeyB);
                    c.Value.ShouldBe(44UL);
                });
            }, _first, _second);

            EnterBarrier("update-44");

            RunOn(() =>
            {
                // add 1 on both nodes using WriteMajority
                _replicator.Tell(Dsl.Update(KeyB, GCounter.Empty, _writeMajority, x => x.Increment(_cluster, 1)));
                ExpectMsg(new UpdateSuccess(KeyB, null));

                // the total, after replication should be 46
                AwaitAssert(() =>
                {
                    _replicator.Tell(Dsl.Get(KeyB, _readMajority));
                    var c = ExpectMsg<GetSuccess>(g => Equals(g.Key, KeyB)).Get(KeyB);
                    c.Value.ShouldBe(46UL);
                });
            }, _first, _second);

            EnterBarrierAfterTestStep();
        }

        public void Cluster_CRDT_should_be_replicated_after_successful_update()
        {
            var changedProbe = CreateTestProbe();
            RunOn(() =>
            {
                _replicator.Tell(Dsl.Subscribe(KeyC, changedProbe.Ref));
            }, _first, _second);

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Update(KeyC, GCounter.Empty, _writeTwo, x => x.Increment(_cluster, 30)));
                ExpectMsg(new UpdateSuccess(KeyC, null));
                changedProbe.ExpectMsg<Changed>(c => Equals(c.Key, KeyC)).Get(KeyC).Value.ShouldBe(30UL);

                _replicator.Tell(Dsl.Update(KeyY, GCounter.Empty, _writeTwo, x => x.Increment(_cluster, 30)));
                ExpectMsg(new UpdateSuccess(KeyY, null));

                _replicator.Tell(Dsl.Update(KeyZ, GCounter.Empty, _writeMajority, x => x.Increment(_cluster, 30)));
                ExpectMsg(new UpdateSuccess(KeyZ, null));
            }, _first);

            EnterBarrier("update-c30");

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Get(KeyC, ReadLocal.Instance));
                var c30 = ExpectMsg<GetSuccess>(c => Equals(c.Key, KeyC)).Get(KeyC);
                c30.Value.ShouldBe(30UL);
                changedProbe.ExpectMsg<Changed>(c => Equals(c.Key, KeyC)).Get(KeyC).Value.ShouldBe(30UL);

                // replicate with gossip after WriteLocal
                _replicator.Tell(Dsl.Update(KeyC, GCounter.Empty, WriteLocal.Instance, x => x.Increment(_cluster, 1)));
                ExpectMsg(new UpdateSuccess(KeyC, null));
                changedProbe.ExpectMsg<Changed>(c => Equals(c.Key, KeyC)).Get(KeyC).Value.ShouldBe(31UL);

                _replicator.Tell(Dsl.Delete(KeyY, WriteLocal.Instance, 777));
                ExpectMsg(new DeleteSuccess(KeyY, 777));

                _replicator.Tell(Dsl.Get(KeyZ, _readMajority));
                ExpectMsg<GetSuccess>(c => Equals(c.Key, KeyZ)).Get(KeyZ).Value.ShouldBe(30UL);
            }, _second);

            EnterBarrier("update-c31");

            RunOn(() =>
            {
                // KeyC and deleted KeyY should be replicated via gossip to the other node
                Within(TimeSpan.FromSeconds(5), () =>
                {
                    AwaitAssert(() =>
                    {
                        _replicator.Tell(Dsl.Get(KeyC, ReadLocal.Instance));
                        var c = ExpectMsg<GetSuccess>(g => Equals(g.Key, KeyC)).Get(KeyC);
                        c.Value.ShouldBe(31UL);

                        _replicator.Tell(Dsl.Get(KeyY, ReadLocal.Instance));
                        ExpectMsg(new DataDeleted(KeyY));
                    });
                });
                changedProbe.ExpectMsg<Changed>(c => Equals(c.Key, KeyC)).Get(KeyC).Value.ShouldBe(31UL);
            }, _first);

            EnterBarrier("verified-c31");

            // and also for concurrent updates
            RunOn(() =>
            {
                _replicator.Tell(Dsl.Get(KeyC, ReadLocal.Instance));
                var c31 = ExpectMsg<GetSuccess>(g => Equals(g.Key, KeyC)).Get(KeyC);
                c31.Value.ShouldBe(31UL);

                _replicator.Tell(Dsl.Update(KeyC, GCounter.Empty, WriteLocal.Instance, x => x.Increment(_cluster, 1)));
                ExpectMsg(new UpdateSuccess(KeyC, null));

                Within(TimeSpan.FromSeconds(5), () =>
                {
                    AwaitAssert(() =>
                    {
                        _replicator.Tell(Dsl.Get(KeyC, ReadLocal.Instance));
                        var c = ExpectMsg<GetSuccess>(g => Equals(g.Key, KeyC), TimeSpan.FromMilliseconds(300)).Get(KeyC);
                        c.Value.ShouldBe(33UL);
                    }, interval:TimeSpan.FromMilliseconds(300));
                });
            }, _first, _second);

            EnterBarrierAfterTestStep();
        }

        public void Cluster_CRDT_should_converge_after_partition()
        {
            RunOn(() =>
            {
                _replicator.Tell(Dsl.Update(KeyD, GCounter.Empty, _writeTwo, x => x.Increment(_cluster, 40)));
                ExpectMsg(new UpdateSuccess(KeyD, null));

                TestConductor.Blackhole(_first, _second, ThrottleTransportAdapter.Direction.Both)
                    .Wait(TimeSpan.FromSeconds(10));
            }, _first);

            EnterBarrier("blackhole-first-second");

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Get(KeyD, ReadLocal.Instance));
                var c40 = ExpectMsg<GetSuccess>(g => Equals(g.Key, KeyD)).Get(KeyD);
                c40.Value.ShouldBe(40UL);

                _replicator.Tell(Dsl.Update(KeyD, GCounter.Empty.Increment(_cluster, 1), _writeTwo, x => x.Increment(_cluster, 1)));
                ExpectMsg(new UpdateTimeout(KeyD, null), _timeOut.Add(TimeSpan.FromSeconds(1)));
                _replicator.Tell(Dsl.Update(KeyD, GCounter.Empty, _writeTwo, x => x.Increment(_cluster, 1)));
                ExpectMsg(new UpdateTimeout(KeyD, null), _timeOut.Add(TimeSpan.FromSeconds(1)));
            }, _first, _second);

            RunOn(() =>
            {
                for (ulong i = 1; i <= 30UL; i++)
                {
                    var n = i;
                    var keydn = new GCounterKey("D" + n);
                    _replicator.Tell(Dsl.Update(keydn, GCounter.Empty, WriteLocal.Instance, x => x.Increment(_cluster, n)));
                    ExpectMsg(new UpdateSuccess(keydn, null));
                }
            }, _first);

            EnterBarrier("updates-during-partion");

            RunOn(() =>
            {
                TestConductor.PassThrough(_first, _second, ThrottleTransportAdapter.Direction.Both)
                    .Wait(TimeSpan.FromSeconds(5));
            }, _first);

            EnterBarrier("passThrough-first-second");

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Get(KeyD, _readTwo));
                var c44 = ExpectMsg<GetSuccess>(g => Equals(g.Key, KeyD)).Get(KeyD);
                c44.Value.ShouldBe(44UL);

                Within(TimeSpan.FromSeconds(10), () =>
                    AwaitAssert(() =>
                    {
                        for (ulong i = 1; i <= 30UL; i++)
                        {
                            var keydn = new GCounterKey("D" + i);
                            _replicator.Tell(Dsl.Get(keydn, ReadLocal.Instance));
                            ExpectMsg<GetSuccess>(g => Equals(g.Key, keydn), TimeSpan.FromMilliseconds(50)).Get(keydn).Value.ShouldBe(i);
                        }
                    }));
            }, _first, _second);

            EnterBarrierAfterTestStep();
        }

        public void Cluster_CRDT_should_support_majority_quorum_write_and_read_with_3_nodes_with_1_unreachable()
        {
            Join(_third, _first);

            RunOn(() =>
            {
                Within(TimeSpan.FromSeconds(10), () => AwaitAssert(() =>
                {
                    _replicator.Tell(Dsl.GetReplicaCount);
                    ExpectMsg(new ReplicaCount(3));
                }));
            }, _first, _second, _third);

            EnterBarrier("3-nodes");

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Update(KeyE, GCounter.Empty, _writeMajority, x => x.Increment(_cluster, 50)));
                ExpectMsg(new UpdateSuccess(KeyE, null));
            }, _first, _second, _third);

            EnterBarrier("write-initial-majority");

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Get(KeyE, _readMajority));
                var c150 = ExpectMsg<GetSuccess>(g => Equals(g.Key, KeyE)).Get(KeyE);
                c150.Value.ShouldBe(150UL);
            }, _first, _second, _third);

            EnterBarrier("read-initial-majority");

            RunOn(() =>
            {
                TestConductor.Blackhole(_first, _third, ThrottleTransportAdapter.Direction.Both).Wait(TimeSpan.FromSeconds(5));
                TestConductor.Blackhole(_second, _third, ThrottleTransportAdapter.Direction.Both).Wait(TimeSpan.FromSeconds(5));
            }, _first);

            EnterBarrier("blackhole-third");

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Update(KeyE, GCounter.Empty, WriteLocal.Instance, x => x.Increment(_cluster, 1)));
                ExpectMsg(new UpdateSuccess(KeyE, null));
            }, _second);

            EnterBarrier("local-update-from-second");

            RunOn(() =>
            {
                // ReadMajority should retrieve the previous update from second, before applying the modification
                var probe1 = CreateTestProbe();
                var probe2 = CreateTestProbe();
                _replicator.Tell(Dsl.Get(KeyE, _readMajority), probe2.Ref);
                probe2.ExpectMsg<GetSuccess>();
                _replicator.Tell(Dsl.Update(KeyE, GCounter.Empty, _writeMajority, data =>
                {
                    probe1.Ref.Tell(data.Value);
                    return data.Increment(_cluster, 1);
                }), probe2.Ref);

                // verify read your own writes, without waiting for the UpdateSuccess reply
                // note that the order of the replies are not defined, and therefore we use separate probes
                var probe3 = CreateTestProbe();
                _replicator.Tell(Dsl.Get(KeyE, _readMajority), probe3.Ref);
                probe1.ExpectMsg(151UL);
                probe2.ExpectMsg(new UpdateSuccess(KeyE, null));
                var c152 = probe3.ExpectMsg<GetSuccess>(g => Equals(g.Key, KeyE)).Get(KeyE);
                c152.Value.ShouldBe(152UL);
            }, _first);

            EnterBarrier("majority-update-from-first");

            RunOn(() =>
            {
                var probe1 = CreateTestProbe();
                _replicator.Tell(Dsl.Get(KeyE, _readMajority), probe1.Ref);
                probe1.ExpectMsg<GetSuccess>();
                _replicator.Tell(Dsl.Update(KeyE, GCounter.Empty, _writeMajority, 153, x => x.Increment(_cluster, 1)), probe1.Ref);

                // verify read your own writes, without waiting for the UpdateSuccess reply
                // note that the order of the replies are not defined, and therefore we use separate probes
                var probe2 = CreateTestProbe();
                _replicator.Tell(Dsl.Update(KeyE, GCounter.Empty, _writeMajority, 154, x => x.Increment(_cluster, 1)), probe2.Ref);
                var probe3 = CreateTestProbe();
                _replicator.Tell(Dsl.Update(KeyE, GCounter.Empty, _writeMajority, 155, x => x.Increment(_cluster, 1)), probe3.Ref);
                var probe5 = CreateTestProbe();
                _replicator.Tell(Dsl.Get(KeyE, _readMajority), probe5.Ref);
                probe1.ExpectMsg(new UpdateSuccess(KeyE, 153));
                probe2.ExpectMsg(new UpdateSuccess(KeyE, 154));
                probe3.ExpectMsg(new UpdateSuccess(KeyE, 155));
                var c155 = probe5.ExpectMsg<GetSuccess>(g => Equals(g.Key, KeyE)).Get(KeyE);
                c155.Value.ShouldBe(155UL);
            }, _second);

            EnterBarrier("majority-update-from-second");

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Get(KeyE2, _readAll, 998));
                ExpectMsg(new GetFailure(KeyE2, 998), _timeOut.Add(TimeSpan.FromSeconds(1)));
                _replicator.Tell(Dsl.Get(KeyE2, Dsl.ReadLocal));
                ExpectMsg(new NotFound(KeyE2, null));
            }, _first, _second);

            EnterBarrier("read-all-fail-update");

            RunOn(() =>
            {
                Sys.Log.Info("Opening up traffic to third node again...");
                TestConductor.PassThrough(_first, _third, ThrottleTransportAdapter.Direction.Both).Wait(TimeSpan.FromSeconds(5));
                TestConductor.PassThrough(_second, _third, ThrottleTransportAdapter.Direction.Both).Wait(TimeSpan.FromSeconds(5));
                Sys.Log.Info("Traffic open to node 3.");
            }, _first);

            EnterBarrier("passThrough-third");

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Get(KeyE, _readMajority));

                var c155 = ExpectMsg<GetSuccess>(g => Equals(g.Key, KeyE)).Get(KeyE);
                c155.Value.ShouldBe(155UL);
            }, _third);

            EnterBarrierAfterTestStep();
        }

        public void Cluster_CRDT_should_converge_after_many_concurrent_updates()
        {
            Within(TimeSpan.FromSeconds(10), () =>
            {
                RunOn(() =>
                {
                    var c = GCounter.Empty;
                    for (ulong i = 0; i < 100UL; i++)
                    {
                        c = c.Increment(_cluster, i);
                        _replicator.Tell(Dsl.Update(KeyF, GCounter.Empty, _writeTwo, x => x.Increment(_cluster, 1)));
                    }

                    var results = ReceiveN(100);
                    results.All(x => x is UpdateSuccess).ShouldBeTrue();
                }, _first, _second, _third);

                EnterBarrier("100-updates-done");

                RunOn(() =>
                {
                    _replicator.Tell(Dsl.Get(KeyF, _readTwo));
                    var c = ExpectMsg<GetSuccess>(g => Equals(g.Key, KeyF)).Get(KeyF);
                    c.Value.ShouldBe(3 * 100UL);
                }, _first, _second, _third);

                EnterBarrierAfterTestStep();
            });
        }

        public void Cluster_CRDT_should_read_repair_happens_before_GetSuccess()
        {
            RunOn(() =>
            {
                _replicator.Tell(Dsl.Update(KeyG, ORSet<string>.Empty, _writeTwo, x => x
                    .Add(_cluster, "a")
                    .Add(_cluster, "b")));
                ExpectMsg<UpdateSuccess>();
            }, _first);

            EnterBarrier("a-b-added-to-G");

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Get(KeyG, _readAll));
                ExpectMsg<GetSuccess>(g => Equals(g.Key, KeyG)).Get(KeyG).Elements.SetEquals(new[] { "a", "b" });
                _replicator.Tell(Dsl.Get(KeyG, ReadLocal.Instance));
                ExpectMsg<GetSuccess>(g => Equals(g.Key, KeyG)).Get(KeyG).Elements.SetEquals(new[] { "a", "b" });
            }, _second);

            EnterBarrierAfterTestStep();
        }

        public void Cluster_CRDT_should_check_that_remote_update_and_local_update_both_cause_a_change_event_to_emit_with_the_merged_data()
        {
            var changedProbe = CreateTestProbe();

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Subscribe(KeyH, changedProbe.Ref));
                _replicator.Tell(Dsl.Update(KeyH, ORDictionary<string, Flag>.Empty, _writeTwo, x => x.SetItem(_cluster, "a", Flag.False)));
                changedProbe.ExpectMsg<Changed>(g => Equals(g.Key, KeyH)).Get(KeyH).Entries.SequenceEqual(ImmutableDictionary.CreateRange(new[]
                {
                    new KeyValuePair<string, Flag>("a", Flag.False),
                })).ShouldBeTrue();
            }, _second);

            EnterBarrier("update-h1");

            RunOn(() =>
            {
                _replicator.Tell(Dsl.Update(KeyH, ORDictionary<string, Flag>.Empty, _writeTwo, x => x.SetItem(_cluster, "a", Flag.True)));
            }, _first);

            RunOn(() =>
            {
                changedProbe.ExpectMsg<Changed>(g => Equals(g.Key, KeyH)).Get(KeyH).Entries.SequenceEqual(ImmutableDictionary.CreateRange(new[]
                {
                    new KeyValuePair<string, Flag>("a", Flag.True)
                })).ShouldBeTrue();

                _replicator.Tell(Dsl.Update(KeyH, ORDictionary<string, Flag>.Empty, _writeTwo, x => x.SetItem(_cluster, "b", Flag.True)));
                changedProbe.ExpectMsg<Changed>(g => Equals(g.Key, KeyH)).Get(KeyH).Entries.SequenceEqual(ImmutableDictionary.CreateRange(new[]
                {
                    new KeyValuePair<string, Flag>("a", Flag.True),
                    new KeyValuePair<string, Flag>("b", Flag.True)
                })).ShouldBeTrue();
            }, _second);

            EnterBarrierAfterTestStep();
        }

        public void Cluster_CRDT_should_avoid_duplicate_change_events_for_same_data()
        {
            var changedProbe = CreateTestProbe();
            _replicator.Tell(Dsl.Subscribe(KeyI, changedProbe.Ref));

            EnterBarrier("subscribed-I");

            RunOn(() => _replicator.Tell(Dsl.Update(KeyI, GSet<string>.Empty, _writeTwo, a => a.Add("a"))),
                _second);

            Within(TimeSpan.FromSeconds(5), () =>
            {
                
                var changed =  changedProbe.ExpectMsg<Changed>(c =>
                        c.Get(KeyI).Elements.ShouldBe(ImmutableHashSet.Create("a")));
                var keyIData = changed.Get(KeyI);
                Sys.Log.Debug("DEBUG: Received Changed {0}", changed);
            });

            EnterBarrier("update-I");

            RunOn(() => _replicator.Tell(Dsl.Update(KeyI, GSet<string>.Empty, _writeTwo, a => a.Add("a"))),
                _first);

            changedProbe.ExpectNoMsg(TimeSpan.FromSeconds(1));

            EnterBarrierAfterTestStep();

        }

        protected override int InitialParticipantsValueFactory => Roles.Count;
        private void EnterBarrierAfterTestStep()
        {
            _afterCounter++;
            EnterBarrier("after-" + _afterCounter);
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

}
