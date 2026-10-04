//-----------------------------------------------------------------------
// <copyright file="ReplicatorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.TestKit;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Cluster;
using Akka.Cluster.TestKit;
using Akka.Event;
using Akka.MultiNode.TestAdapter;
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

        private readonly GCounterKey KeyA = new("A");
        private readonly GCounterKey KeyB = new("B");
        private readonly GCounterKey KeyC = new("C");
        private readonly GCounterKey KeyD = new("D");
        private readonly GCounterKey KeyE = new("E");
        private readonly GCounterKey KeyE2 = new("E2");
        private readonly GCounterKey KeyF = new("F");
        private readonly ORSetKey<string> KeyG = new("G");
        private readonly ORDictionaryKey<string, Flag> KeyH = new("H");
        private readonly GSetKey<string> KeyI = new("I");
        private readonly GSetKey<string> KeyJ = new("J");
        private readonly LWWRegisterKey<string> KeyK = new("K");
        private readonly GCounterKey KeyX = new("X");
        private readonly GCounterKey KeyY = new("Y");
        private readonly GCounterKey KeyZ = new("Z");

        private readonly TimeSpan _timeOut;
        private readonly WriteTo _writeTwo;
        private readonly WriteMajority _writeMajority;
        private readonly WriteAll _writeAll;
        private readonly ReadFrom _readTwo;
        private readonly ReadMajority _readMajority;
        private readonly ReadAll _readAll;

        /// <summary>
        /// Per-attempt budget for the reads inside the AwaitAssertAsync loops below. A fresh
        /// probe per attempt keeps a late reply from a timed-out attempt out of the next
        /// attempt's queue, where it would be read as the answer to a different request.
        /// </summary>
        private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Poll interval for the AwaitAssertAsync loops.
        /// </summary>
        private static readonly TimeSpan AttemptInterval = TimeSpan.FromMilliseconds(200);

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
        public async Task ReplicatorSpecTests()
        {
            await Cluster_CRDT_should_work_in_single_node_cluster();
            await Cluster_CRDT_should_merge_the_update_with_existing_value();
            await Cluster_CRDT_should_reply_with_ModifyFailure_if_exception_is_thrown_by_modify_function();
            await Cluster_CRDT_should_replicate_values_to_new_node();
            await Cluster_CRDT_should_work_in_2_node_cluster();
            await Cluster_CRDT_should_be_replicated_after_successful_update();
            await Cluster_CRDT_should_converge_after_partition();
            await Cluster_CRDT_should_support_majority_quorum_write_and_read_with_3_nodes_with_1_unreachable();
            await Cluster_CRDT_should_converge_after_many_concurrent_updates();
            await Cluster_CRDT_should_read_repair_happens_before_GetSuccess();
            await Cluster_CRDT_should_check_that_remote_update_and_local_update_both_cause_a_change_event_to_emit_with_the_merged_data();
            await Cluster_CRDT_should_avoid_duplicate_change_events_for_same_data();
        }

        public async Task Cluster_CRDT_should_work_in_single_node_cluster()
        {
            await JoinAsync(_first, _first);

            await RunOnAsync(async () =>
            {
                await WithinAsync(TimeSpan.FromSeconds(5.0), async () =>
                {
                    _replicator.Tell(Dsl.GetReplicaCount);
                    await ExpectMsgAsync(new ReplicaCount(1));
                });

                var changedProbe = CreateTestProbe();
                _replicator.Tell(Dsl.Subscribe(KeyA, changedProbe.Ref));
                _replicator.Tell(Dsl.Subscribe(KeyX, changedProbe.Ref));

                _replicator.Tell(Dsl.Get(KeyA, ReadLocal.Instance));
                await ExpectMsgAsync(new NotFound(KeyA, null));

                var c3 = GCounter.Empty.Increment(_cluster, 3);
                var update = Dsl.Update(KeyA, GCounter.Empty, WriteLocal.Instance, x => x.Increment(_cluster, 3));
                _replicator.Tell(update);
                await ExpectMsgAsync(new UpdateSuccess(KeyA, null));
                _replicator.Tell(Dsl.Get(KeyA, ReadLocal.Instance));
                await ExpectMsgAsync(new GetSuccess(KeyA, null, c3));
                await changedProbe.ExpectMsgAsync(new Changed(KeyA, c3));

                var changedProbe2 = CreateTestProbe();
                _replicator.Tell(new Subscribe(KeyA, changedProbe2.Ref));
                await changedProbe2.ExpectMsgAsync(new Changed(KeyA, c3));


                var c4 = c3.Increment(_cluster);
                // too strong consistency level
                _replicator.Tell(Dsl.Update(KeyA, _writeTwo, x => x.Increment(_cluster)));
                await ExpectMsgAsync(new UpdateTimeout(KeyA, null), _timeOut.Add(TimeSpan.FromSeconds(1)));
                _replicator.Tell(Dsl.Get(KeyA, ReadLocal.Instance));
                await ExpectMsgAsync(new GetSuccess(KeyA, null, c4));
                await changedProbe.ExpectMsgAsync(new Changed(KeyA, c4));

                var c5 = c4.Increment(_cluster);
                // too strong consistency level
                _replicator.Tell(Dsl.Update(KeyA, _writeMajority, x => x.Increment(_cluster)));
                await ExpectMsgAsync(new UpdateSuccess(KeyA, null));
                _replicator.Tell(Dsl.Get(KeyA, _readMajority));
                await ExpectMsgAsync(new GetSuccess(KeyA, null, c5));
                await changedProbe.ExpectMsgAsync(new Changed(KeyA, c5));

                var c6 = c5.Increment(_cluster);
                _replicator.Tell(Dsl.Update(KeyA, _writeAll, x => x.Increment(_cluster)));
                await ExpectMsgAsync(new UpdateSuccess(KeyA, null));
                _replicator.Tell(Dsl.Get(KeyA, _readAll));
                await ExpectMsgAsync(new GetSuccess(KeyA, null, c6));
                await changedProbe.ExpectMsgAsync(new Changed(KeyA, c6));

                var c9 = GCounter.Empty.Increment(_cluster, 9);
                _replicator.Tell(Dsl.Update(KeyX, GCounter.Empty, WriteLocal.Instance, x => x.Increment(_cluster, 9)));
                await ExpectMsgAsync(new UpdateSuccess(KeyX, null));
                await changedProbe.ExpectMsgAsync(new Changed(KeyX, c9));
                _replicator.Tell(Dsl.Delete(KeyX, WriteLocal.Instance));
                await ExpectMsgAsync(new DeleteSuccess(KeyX));
                await changedProbe.ExpectMsgAsync(new DataDeleted(KeyX));
                _replicator.Tell(Dsl.Get(KeyX, ReadLocal.Instance));
                await ExpectMsgAsync(new DataDeleted(KeyX));
                _replicator.Tell(Dsl.Get(KeyX, _readAll));
                await ExpectMsgAsync(new DataDeleted(KeyX));
                _replicator.Tell(Dsl.Update(KeyX, WriteLocal.Instance, x => x.Increment(_cluster)));
                await ExpectMsgAsync(new DataDeleted(KeyX));
                _replicator.Tell(Dsl.Delete(KeyX, WriteLocal.Instance));
                await ExpectMsgAsync(new DataDeleted(KeyX));

                _replicator.Tell(Dsl.GetKeyIds);
                await ExpectMsgAsync(new GetKeysIdsResult(ImmutableHashSet<string>.Empty.Add("A")));
            }, _first);

            await EnterBarrierAfterTestStepAsync();
        }

        public async Task Cluster_CRDT_should_merge_the_update_with_existing_value()
        {
            await RunOnAsync(async () =>
            {
                var update = new Update(KeyJ, new GSet<string>(), WriteLocal.Instance, x => ((GSet<string>)x).Add("a").Add("b"));
                _replicator.Tell(update);
                await ExpectMsgAsync(new UpdateSuccess(KeyJ, null));
                var update2 = new Update(KeyJ, new GSet<string>(), WriteLocal.Instance, x => ((GSet<string>)x).Add("c"));
                _replicator.Tell(update2);
                await ExpectMsgAsync(new UpdateSuccess(KeyJ, null));
                _replicator.Tell(new Get(KeyJ, ReadLocal.Instance));
                await ExpectMsgAsync<GetSuccess>(x => x.Data.Equals(new GSet<string>(new[] { "a", "b", "c" }.ToImmutableHashSet())));
            }, _first);

            await EnterBarrierAfterTestStepAsync();
        }

        public async Task Cluster_CRDT_should_reply_with_ModifyFailure_if_exception_is_thrown_by_modify_function()
        {
            await RunOnAsync(async () =>
            {
                var exception = new Exception("Test exception");
                Func<IReplicatedData, IReplicatedData> update = _ =>
                {
                    throw exception;
                };
                _replicator.Tell(new Update(KeyA, GCounter.Empty, WriteLocal.Instance, update));
                await ExpectMsgAsync<ModifyFailure>(x => x.Cause.Equals(exception));
            }, _first);

            await EnterBarrierAfterTestStepAsync();
        }

        public async Task Cluster_CRDT_should_replicate_values_to_new_node()
        {
            await JoinAsync(_second, _first);

            await RunOnAsync(async () =>
            {
                await AwaitAssertAsync(async () =>
                {
                    // Fresh probe per attempt - see the note on AttemptTimeout.
                    var probe = CreateTestProbe();
                    _replicator.Tell(Dsl.GetReplicaCount, probe.Ref);
                    await probe.ExpectMsgAsync(new ReplicaCount(2), AttemptTimeout);
                }, TimeSpan.FromSeconds(10), AttemptInterval);
            }, _first, _second);

            await EnterBarrierAsync("2-nodes");

            await RunOnAsync(async () =>
            {
                var changedProbe = CreateTestProbe();
                _replicator.Tell(Dsl.Subscribe(KeyA, changedProbe.Ref));
                // "A" should be replicated via gossip to the new node
                await AwaitAssertAsync(async () =>
                {
                    // for some reason result is returned before CRDT gets replicated
                    var probe = CreateTestProbe();
                    _replicator.Tell(Dsl.Get(KeyA, ReadLocal.Instance), probe.Ref);
                    var c = (await probe.ExpectMsgAsync<GetSuccess>(g => Equals(g.Key, KeyA), AttemptTimeout)).Get(KeyA);
                    c.Value.ShouldBe(6UL);
                }, TimeSpan.FromSeconds(5), AttemptInterval);
                var c2 = (await changedProbe.ExpectMsgAsync<Changed>(g => Equals(g.Key, KeyA))).Get(KeyA);
                c2.Value.ShouldBe(6UL);
            }, _second);

            await EnterBarrierAfterTestStepAsync();
        }

        public async Task Cluster_CRDT_should_work_in_2_node_cluster()
        {
            await RunOnAsync(async () =>
            {
                // start with 20 on both nodes
                _replicator.Tell(Dsl.Update(KeyB, GCounter.Empty, WriteLocal.Instance, x => x.Increment(_cluster, 20)));
                await ExpectMsgAsync(new UpdateSuccess(KeyB, null));

                // add 1 on both nodes using WriteTwo
                _replicator.Tell(Dsl.Update(KeyB, GCounter.Empty, _writeTwo, x => x.Increment(_cluster, 1)));
                await ExpectMsgAsync(new UpdateSuccess(KeyB, null));

                // the total, after replication should be 42
                await AssertKeyBAsync(_readTwo, 42UL);
            }, _first, _second);

            await EnterBarrierAsync("update-42");

            await RunOnAsync(async () =>
            {
                // add 1 on both nodes using WriteAll
                _replicator.Tell(Dsl.Update(KeyB, GCounter.Empty, _writeAll, x => x.Increment(_cluster, 1)));
                await ExpectMsgAsync(new UpdateSuccess(KeyB, null));

                // the total, after replication should be 44
                await AssertKeyBAsync(_readAll, 44UL);
            }, _first, _second);

            await EnterBarrierAsync("update-44");

            await RunOnAsync(async () =>
            {
                // add 1 on both nodes using WriteMajority
                _replicator.Tell(Dsl.Update(KeyB, GCounter.Empty, _writeMajority, x => x.Increment(_cluster, 1)));
                await ExpectMsgAsync(new UpdateSuccess(KeyB, null));

                // the total, after replication should be 46
                await AssertKeyBAsync(_readMajority, 46UL);
            }, _first, _second);

            await EnterBarrierAfterTestStepAsync();
        }

        /// <summary>
        /// Reads KeyB with a multi-node read consistency until it reaches <paramref name="expected"/>.
        /// The per-attempt bound covers the replicator's own read timeout plus a margin, and the
        /// 10 s budget allows several attempts. Each attempt uses a fresh probe.
        /// </summary>
        private async Task AssertKeyBAsync(IReadConsistency consistency, ulong expected)
        {
            await AwaitAssertAsync(async () =>
            {
                var probe = CreateTestProbe();
                _replicator.Tell(Dsl.Get(KeyB, consistency), probe.Ref);
                var c = (await probe.ExpectMsgAsync<GetSuccess>(g => Equals(g.Key, KeyB), _timeOut.Add(TimeSpan.FromSeconds(1)))).Get(KeyB);
                c.Value.ShouldBe(expected);
            }, TimeSpan.FromSeconds(10), AttemptInterval);
        }

        public async Task Cluster_CRDT_should_be_replicated_after_successful_update()
        {
            var changedProbe = CreateTestProbe();
            await RunOnAsync(() =>
            {
                _replicator.Tell(Dsl.Subscribe(KeyC, changedProbe.Ref));
                return Task.CompletedTask;
            }, _first, _second);

            await RunOnAsync(async () =>
            {
                _replicator.Tell(Dsl.Update(KeyC, GCounter.Empty, _writeTwo, x => x.Increment(_cluster, 30)));
                await ExpectMsgAsync(new UpdateSuccess(KeyC, null));
                (await changedProbe.ExpectMsgAsync<Changed>(c => Equals(c.Key, KeyC))).Get(KeyC).Value.ShouldBe(30UL);

                _replicator.Tell(Dsl.Update(KeyY, GCounter.Empty, _writeTwo, x => x.Increment(_cluster, 30)));
                await ExpectMsgAsync(new UpdateSuccess(KeyY, null));

                _replicator.Tell(Dsl.Update(KeyZ, GCounter.Empty, _writeMajority, x => x.Increment(_cluster, 30)));
                await ExpectMsgAsync(new UpdateSuccess(KeyZ, null));
            }, _first);

            await EnterBarrierAsync("update-c30");

            await RunOnAsync(async () =>
            {
                _replicator.Tell(Dsl.Get(KeyC, ReadLocal.Instance));
                var c30 = (await ExpectMsgAsync<GetSuccess>(c => Equals(c.Key, KeyC))).Get(KeyC);
                c30.Value.ShouldBe(30UL);
                (await changedProbe.ExpectMsgAsync<Changed>(c => Equals(c.Key, KeyC))).Get(KeyC).Value.ShouldBe(30UL);

                // replicate with gossip after WriteLocal
                _replicator.Tell(Dsl.Update(KeyC, GCounter.Empty, WriteLocal.Instance, x => x.Increment(_cluster, 1)));
                await ExpectMsgAsync(new UpdateSuccess(KeyC, null));
                (await changedProbe.ExpectMsgAsync<Changed>(c => Equals(c.Key, KeyC))).Get(KeyC).Value.ShouldBe(31UL);

                _replicator.Tell(Dsl.Delete(KeyY, WriteLocal.Instance, 777));
                await ExpectMsgAsync(new DeleteSuccess(KeyY, 777));

                _replicator.Tell(Dsl.Get(KeyZ, _readMajority));
                (await ExpectMsgAsync<GetSuccess>(c => Equals(c.Key, KeyZ))).Get(KeyZ).Value.ShouldBe(30UL);
            }, _second);

            await EnterBarrierAsync("update-c31");

            await RunOnAsync(async () =>
            {
                // KeyC and deleted KeyY should be replicated via gossip to the other node
                await AwaitAssertAsync(async () =>
                {
                    // Fresh probe per attempt - see the note on AttemptTimeout.
                    var probe = CreateTestProbe();
                    _replicator.Tell(Dsl.Get(KeyC, ReadLocal.Instance), probe.Ref);
                    var c = (await probe.ExpectMsgAsync<GetSuccess>(g => Equals(g.Key, KeyC), AttemptTimeout)).Get(KeyC);
                    c.Value.ShouldBe(31UL);

                    _replicator.Tell(Dsl.Get(KeyY, ReadLocal.Instance), probe.Ref);
                    await probe.ExpectMsgAsync(new DataDeleted(KeyY), AttemptTimeout);
                }, TimeSpan.FromSeconds(5), AttemptInterval);
                (await changedProbe.ExpectMsgAsync<Changed>(c => Equals(c.Key, KeyC))).Get(KeyC).Value.ShouldBe(31UL);
            }, _first);

            await EnterBarrierAsync("verified-c31");

            // and also for concurrent updates
            await RunOnAsync(async () =>
            {
                _replicator.Tell(Dsl.Get(KeyC, ReadLocal.Instance));
                var c31 = (await ExpectMsgAsync<GetSuccess>(g => Equals(g.Key, KeyC))).Get(KeyC);
                c31.Value.ShouldBe(31UL);

                _replicator.Tell(Dsl.Update(KeyC, GCounter.Empty, WriteLocal.Instance, x => x.Increment(_cluster, 1)));
                await ExpectMsgAsync(new UpdateSuccess(KeyC, null));

                await AwaitAssertAsync(async () =>
                {
                    // Fresh probe per attempt. The old 300 ms ExpectMsg on the shared TestActor
                    // left a stray GetSuccess(KeyC) in its queue after one late reply, which
                    // broke the KeyD expectations in the next step.
                    var probe = CreateTestProbe();
                    _replicator.Tell(Dsl.Get(KeyC, ReadLocal.Instance), probe.Ref);
                    var c = (await probe.ExpectMsgAsync<GetSuccess>(g => Equals(g.Key, KeyC), AttemptTimeout)).Get(KeyC);
                    c.Value.ShouldBe(33UL);
                }, TimeSpan.FromSeconds(5), AttemptInterval);
            }, _first, _second);

            await EnterBarrierAfterTestStepAsync();
        }

        public async Task Cluster_CRDT_should_converge_after_partition()
        {
            await RunOnAsync(async () =>
            {
                _replicator.Tell(Dsl.Update(KeyD, GCounter.Empty, _writeTwo, x => x.Increment(_cluster, 40)));
                await ExpectMsgAsync(new UpdateSuccess(KeyD, null));

                await TestConductor.Blackhole(_first, _second, ThrottleTransportAdapter.Direction.Both);
            }, _first);

            await EnterBarrierAsync("blackhole-first-second");

            await RunOnAsync(async () =>
            {
                _replicator.Tell(Dsl.Get(KeyD, ReadLocal.Instance));
                var c40 = (await ExpectMsgAsync<GetSuccess>(g => Equals(g.Key, KeyD))).Get(KeyD);
                c40.Value.ShouldBe(40UL);

                _replicator.Tell(Dsl.Update(KeyD, GCounter.Empty.Increment(_cluster, 1), _writeTwo, x => x.Increment(_cluster, 1)));
                await ExpectMsgAsync(new UpdateTimeout(KeyD, null), _timeOut.Add(TimeSpan.FromSeconds(1)));
                _replicator.Tell(Dsl.Update(KeyD, GCounter.Empty, _writeTwo, x => x.Increment(_cluster, 1)));
                await ExpectMsgAsync(new UpdateTimeout(KeyD, null), _timeOut.Add(TimeSpan.FromSeconds(1)));
            }, _first, _second);

            await RunOnAsync(async () =>
            {
                for (ulong i = 1; i <= 30UL; i++)
                {
                    var n = i;
                    var keydn = new GCounterKey("D" + n);
                    _replicator.Tell(Dsl.Update(keydn, GCounter.Empty, WriteLocal.Instance, x => x.Increment(_cluster, n)));
                    await ExpectMsgAsync(new UpdateSuccess(keydn, null));
                }
            }, _first);

            await EnterBarrierAsync("updates-during-partion");

            await RunOnAsync(async () =>
            {
                await TestConductor.PassThrough(_first, _second, ThrottleTransportAdapter.Direction.Both);
            }, _first);

            await EnterBarrierAsync("passThrough-first-second");

            await RunOnAsync(async () =>
            {
                _replicator.Tell(Dsl.Get(KeyD, _readTwo));
                var c44 = (await ExpectMsgAsync<GetSuccess>(g => Equals(g.Key, KeyD))).Get(KeyD);
                c44.Value.ShouldBe(44UL);

                await AwaitAssertAsync(async () =>
                {
                    // Fresh probe per attempt. The old loop read 30 replies from the shared
                    // TestActor with a 50 ms bound each; one late reply shifted every later
                    // read onto the previous request's reply until the window closed. Send
                    // all 30 reads first, then take the replies in order (one sender, one
                    // receiver, local delivery: FIFO).
                    var probe = CreateTestProbe();
                    for (ulong i = 1; i <= 30UL; i++)
                        _replicator.Tell(Dsl.Get(new GCounterKey("D" + i), ReadLocal.Instance), probe.Ref);

                    for (ulong i = 1; i <= 30UL; i++)
                    {
                        var keydn = new GCounterKey("D" + i);
                        var reply = await probe.ExpectMsgAsync<GetSuccess>(g => Equals(g.Key, keydn), AttemptTimeout);
                        reply.Get(keydn).Value.ShouldBe(i);
                    }
                }, TimeSpan.FromSeconds(10), AttemptInterval);
            }, _first, _second);

            await EnterBarrierAfterTestStepAsync();
        }

        public async Task Cluster_CRDT_should_support_majority_quorum_write_and_read_with_3_nodes_with_1_unreachable()
        {
            await JoinAsync(_third, _first);

            await RunOnAsync(async () =>
            {
                await AwaitAssertAsync(async () =>
                {
                    // Fresh probe per attempt - see the note on AttemptTimeout.
                    var probe = CreateTestProbe();
                    _replicator.Tell(Dsl.GetReplicaCount, probe.Ref);
                    await probe.ExpectMsgAsync(new ReplicaCount(3), AttemptTimeout);
                }, TimeSpan.FromSeconds(10), AttemptInterval);
            }, _first, _second, _third);

            await EnterBarrierAsync("3-nodes");

            await RunOnAsync(async () =>
            {
                _replicator.Tell(Dsl.Update(KeyE, GCounter.Empty, _writeMajority, x => x.Increment(_cluster, 50)));
                await ExpectMsgAsync(new UpdateSuccess(KeyE, null));
            }, _first, _second, _third);

            await EnterBarrierAsync("write-initial-majority");

            await RunOnAsync(async () =>
            {
                _replicator.Tell(Dsl.Get(KeyE, _readMajority));
                var c150 = (await ExpectMsgAsync<GetSuccess>(g => Equals(g.Key, KeyE))).Get(KeyE);
                c150.Value.ShouldBe(150UL);
            }, _first, _second, _third);

            await EnterBarrierAsync("read-initial-majority");

            await RunOnAsync(async () =>
            {
                await TestConductor.Blackhole(_first, _third, ThrottleTransportAdapter.Direction.Both);
                await TestConductor.Blackhole(_second, _third, ThrottleTransportAdapter.Direction.Both);
            }, _first);

            await EnterBarrierAsync("blackhole-third");

            await RunOnAsync(async () =>
            {
                _replicator.Tell(Dsl.Update(KeyE, GCounter.Empty, WriteLocal.Instance, x => x.Increment(_cluster, 1)));
                await ExpectMsgAsync(new UpdateSuccess(KeyE, null));
            }, _second);

            await EnterBarrierAsync("local-update-from-second");

            await RunOnAsync(async () =>
            {
                // ReadMajority should retrieve the previous update from second, before applying the modification
                var probe1 = CreateTestProbe();
                var probe2 = CreateTestProbe();
                _replicator.Tell(Dsl.Get(KeyE, _readMajority), probe2.Ref);
                await probe2.ExpectMsgAsync<GetSuccess>();
                _replicator.Tell(Dsl.Update(KeyE, GCounter.Empty, _writeMajority, data =>
                {
                    probe1.Ref.Tell(data.Value);
                    return data.Increment(_cluster, 1);
                }), probe2.Ref);

                // verify read your own writes, without waiting for the UpdateSuccess reply
                // note that the order of the replies are not defined, and therefore we use separate probes
                var probe3 = CreateTestProbe();
                _replicator.Tell(Dsl.Get(KeyE, _readMajority), probe3.Ref);
                await probe1.ExpectMsgAsync(151UL);
                await probe2.ExpectMsgAsync(new UpdateSuccess(KeyE, null));
                var c152 = (await probe3.ExpectMsgAsync<GetSuccess>(g => Equals(g.Key, KeyE))).Get(KeyE);
                c152.Value.ShouldBe(152UL);
            }, _first);

            await EnterBarrierAsync("majority-update-from-first");

            await RunOnAsync(async () =>
            {
                var probe1 = CreateTestProbe();
                _replicator.Tell(Dsl.Get(KeyE, _readMajority), probe1.Ref);
                await probe1.ExpectMsgAsync<GetSuccess>();
                _replicator.Tell(Dsl.Update(KeyE, GCounter.Empty, _writeMajority, 153, x => x.Increment(_cluster, 1)), probe1.Ref);

                // verify read your own writes, without waiting for the UpdateSuccess reply
                // note that the order of the replies are not defined, and therefore we use separate probes
                var probe2 = CreateTestProbe();
                _replicator.Tell(Dsl.Update(KeyE, GCounter.Empty, _writeMajority, 154, x => x.Increment(_cluster, 1)), probe2.Ref);
                var probe3 = CreateTestProbe();
                _replicator.Tell(Dsl.Update(KeyE, GCounter.Empty, _writeMajority, 155, x => x.Increment(_cluster, 1)), probe3.Ref);
                var probe5 = CreateTestProbe();
                _replicator.Tell(Dsl.Get(KeyE, _readMajority), probe5.Ref);
                await probe1.ExpectMsgAsync(new UpdateSuccess(KeyE, 153));
                await probe2.ExpectMsgAsync(new UpdateSuccess(KeyE, 154));
                await probe3.ExpectMsgAsync(new UpdateSuccess(KeyE, 155));
                var c155 = (await probe5.ExpectMsgAsync<GetSuccess>(g => Equals(g.Key, KeyE))).Get(KeyE);
                c155.Value.ShouldBe(155UL);
            }, _second);

            await EnterBarrierAsync("majority-update-from-second");

            await RunOnAsync(async () =>
            {
                _replicator.Tell(Dsl.Get(KeyE2, _readAll, 998));
                await ExpectMsgAsync(new GetFailure(KeyE2, 998), _timeOut.Add(TimeSpan.FromSeconds(1)));
                _replicator.Tell(Dsl.Get(KeyE2, Dsl.ReadLocal));
                await ExpectMsgAsync(new NotFound(KeyE2, null));
            }, _first, _second);

            await EnterBarrierAsync("read-all-fail-update");

            await RunOnAsync(async () =>
            {
                Sys.Log.Info("Opening up traffic to third node again...");
                await TestConductor.PassThrough(_first, _third, ThrottleTransportAdapter.Direction.Both);
                await TestConductor.PassThrough(_second, _third, ThrottleTransportAdapter.Direction.Both);
                Sys.Log.Info("Traffic open to node 3.");
            }, _first);

            await EnterBarrierAsync("passThrough-third");

            await RunOnAsync(async () =>
            {
                _replicator.Tell(Dsl.Get(KeyE, _readMajority));

                var c155 = (await ExpectMsgAsync<GetSuccess>(g => Equals(g.Key, KeyE))).Get(KeyE);
                c155.Value.ShouldBe(155UL);
            }, _third);

            await EnterBarrierAfterTestStepAsync();
        }

        public async Task Cluster_CRDT_should_converge_after_many_concurrent_updates()
        {
            await WithinAsync(TimeSpan.FromSeconds(10), async () =>
            {
                await RunOnAsync(async () =>
                {
                    var c = GCounter.Empty;
                    for (ulong i = 0; i < 100UL; i++)
                    {
                        c = c.Increment(_cluster, i);
                        _replicator.Tell(Dsl.Update(KeyF, GCounter.Empty, _writeTwo, x => x.Increment(_cluster, 1)));
                    }

                    var results = new List<object>(100);
                    await foreach (var message in ReceiveNAsync(100))
                        results.Add(message);
                    results.All(x => x is UpdateSuccess).ShouldBeTrue();
                }, _first, _second, _third);

                await EnterBarrierAsync("100-updates-done");

                await RunOnAsync(async () =>
                {
                    _replicator.Tell(Dsl.Get(KeyF, _readTwo));
                    var c = (await ExpectMsgAsync<GetSuccess>(g => Equals(g.Key, KeyF))).Get(KeyF);
                    c.Value.ShouldBe(3 * 100UL);
                }, _first, _second, _third);

                await EnterBarrierAfterTestStepAsync();
            });
        }

        public async Task Cluster_CRDT_should_read_repair_happens_before_GetSuccess()
        {
            await RunOnAsync(async () =>
            {
                _replicator.Tell(Dsl.Update(KeyG, ORSet<string>.Empty, _writeTwo, x => x
                    .Add(_cluster, "a")
                    .Add(_cluster, "b")));
                await ExpectMsgAsync<UpdateSuccess>();
            }, _first);

            await EnterBarrierAsync("a-b-added-to-G");

            await RunOnAsync(async () =>
            {
                _replicator.Tell(Dsl.Get(KeyG, _readAll));
                (await ExpectMsgAsync<GetSuccess>(g => Equals(g.Key, KeyG))).Get(KeyG).Elements.SetEquals(new[] { "a", "b" });
                _replicator.Tell(Dsl.Get(KeyG, ReadLocal.Instance));
                (await ExpectMsgAsync<GetSuccess>(g => Equals(g.Key, KeyG))).Get(KeyG).Elements.SetEquals(new[] { "a", "b" });
            }, _second);

            await EnterBarrierAfterTestStepAsync();
        }

        public async Task Cluster_CRDT_should_check_that_remote_update_and_local_update_both_cause_a_change_event_to_emit_with_the_merged_data()
        {
            var changedProbe = CreateTestProbe();

            await RunOnAsync(async () =>
            {
                _replicator.Tell(Dsl.Subscribe(KeyH, changedProbe.Ref));
                _replicator.Tell(Dsl.Update(KeyH, ORDictionary<string, Flag>.Empty, _writeTwo, x => x.SetItem(_cluster, "a", Flag.False)));
                (await changedProbe.ExpectMsgAsync<Changed>(g => Equals(g.Key, KeyH))).Get(KeyH).Entries.SequenceEqual(ImmutableDictionary.CreateRange(new[]
                {
                    new KeyValuePair<string, Flag>("a", Flag.False),
                })).ShouldBeTrue();
            }, _second);

            await EnterBarrierAsync("update-h1");

            await RunOnAsync(() =>
            {
                _replicator.Tell(Dsl.Update(KeyH, ORDictionary<string, Flag>.Empty, _writeTwo, x => x.SetItem(_cluster, "a", Flag.True)));
                return Task.CompletedTask;
            }, _first);

            await RunOnAsync(async () =>
            {
                (await changedProbe.ExpectMsgAsync<Changed>(g => Equals(g.Key, KeyH))).Get(KeyH).Entries.SequenceEqual(ImmutableDictionary.CreateRange(new[]
                {
                    new KeyValuePair<string, Flag>("a", Flag.True)
                })).ShouldBeTrue();

                _replicator.Tell(Dsl.Update(KeyH, ORDictionary<string, Flag>.Empty, _writeTwo, x => x.SetItem(_cluster, "b", Flag.True)));
                (await changedProbe.ExpectMsgAsync<Changed>(g => Equals(g.Key, KeyH))).Get(KeyH).Entries.SequenceEqual(ImmutableDictionary.CreateRange(new[]
                {
                    new KeyValuePair<string, Flag>("a", Flag.True),
                    new KeyValuePair<string, Flag>("b", Flag.True)
                })).ShouldBeTrue();
            }, _second);

            await EnterBarrierAfterTestStepAsync();
        }

        public async Task Cluster_CRDT_should_avoid_duplicate_change_events_for_same_data()
        {
            var changedProbe = CreateTestProbe();
            _replicator.Tell(Dsl.Subscribe(KeyI, changedProbe.Ref));

            await EnterBarrierAsync("subscribed-I");

            await RunOnAsync(() =>
            {
                _replicator.Tell(Dsl.Update(KeyI, GSet<string>.Empty, _writeTwo, a => a.Add("a")));
                return Task.CompletedTask;
            }, _second);

            await WithinAsync(TimeSpan.FromSeconds(5), async () =>
            {
                var changed = await changedProbe.ExpectMsgAsync<Changed>(c =>
                       c.Get(KeyI).Elements.ShouldBe(ImmutableHashSet.Create("a")));
                var keyIData = changed.Get(KeyI);
                Sys.Log.Debug("DEBUG: Received Changed {0}", changed);
            });

            await EnterBarrierAsync("update-I");

            await RunOnAsync(() =>
            {
                _replicator.Tell(Dsl.Update(KeyI, GSet<string>.Empty, _writeTwo, a => a.Add("a")));
                return Task.CompletedTask;
            }, _first);

            await changedProbe.ExpectNoMsgAsync(TimeSpan.FromSeconds(1));

            await EnterBarrierAfterTestStepAsync();

        }

        public async Task Cluster_CRDT_should_support_prefer_oldest_members()
        {
            // disable gossip and delta replication to only verify the write and read operations
            var oldestReplicator = Sys.ActorOf(
              Replicator.Props(
                ReplicatorSettings.Create(Sys).WithPreferOldest(true).WithGossipInterval(TimeSpan.FromMinutes(1))),//.withDeltaCrdtEnabled(false)),
              "oldestReplicator");
            await AwaitAssertAsync(async () =>
            {
                var countProbe = CreateTestProbe();
                oldestReplicator.Tell(GetReplicaCount.Instance, countProbe.Ref);
                await countProbe.ExpectMsgAsync(new ReplicaCount(3), AttemptTimeout);
            }, TimeSpan.FromSeconds(5), AttemptInterval);
            await EnterBarrierAsync("oldest-replicator-started");

            var probe = CreateTestProbe();

            await RunOnAsync(async () =>
            {
                oldestReplicator.Tell(
                    Dsl.Update(KeyK, new LWWRegister<string>(Cluster.SelfUniqueAddress, "0"), _writeTwo, a => a.WithValue(Cluster.SelfUniqueAddress, "1")),
                    probe.Ref);
                await probe.ExpectMsgAsync(new UpdateSuccess(KeyK, null));
            }, _second);
            await EnterBarrierAsync("updated-1");

            await RunOnAsync(async () =>
            {
                // replicated to oldest
                oldestReplicator.Tell(new Get(KeyK, ReadLocal.Instance), probe.Ref);
                var msg = await probe.ExpectMsgAsync<GetSuccess>(m => m.Data is LWWRegister<string>);
                ((LWWRegister<string>)msg.Data).Value.Should().Be("1");
                //probe.ExpectMsg<GetSuccess[LWWRegister[String]]>.dataValue.value should === ("1");
            }, _first);

            await RunOnAsync(async () =>
            {
                // not replicated to third (not among the two oldest)
                oldestReplicator.Tell(Dsl.Get(KeyK, ReadLocal.Instance), probe.Ref);
                await probe.ExpectMsgAsync(new NotFound(KeyK, null));

                // read from oldest
                oldestReplicator.Tell(Dsl.Get(KeyK, _readTwo), probe.Ref);
                var msg = await probe.ExpectMsgAsync<GetSuccess>(m => m.Data is LWWRegister<string>);
                ((LWWRegister<string>)msg.Data).Value.Should().Be("1");
                //probe.ExpectMsg<GetSuccess[LWWRegister[String]]>.dataValue.value should === ("1");
            }, _third);

            await EnterBarrierAfterTestStepAsync();
        }

        protected override int InitialParticipantsValueFactory => Roles.Count;

        private async Task EnterBarrierAfterTestStepAsync()
        {
            _afterCounter++;
            await EnterBarrierAsync("after-" + _afterCounter);
        }

        private async Task JoinAsync(RoleName from, RoleName to)
        {
            await RunOnAsync(() =>
            {
                _cluster.Join(Node(to).Address);
                return Task.CompletedTask;
            }, from);
            await EnterBarrierAsync(from.Name + "-joined");
        }
    }

}
