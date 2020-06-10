//-----------------------------------------------------------------------
// <copyright file="JepsenInspiredInsertSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;

namespace Akka.DistributedData.Tests.MultiNode
{
    public class JepsenInspiredInsertSpecConfig : MultiNodeConfig
    {
        public RoleName Controller { get; }
        public RoleName N1 { get; }
        public RoleName N2 { get; }
        public RoleName N3 { get; }
        public RoleName N4 { get; }
        public RoleName N5 { get; }
        public JepsenInspiredInsertSpecConfig()
        {
            Controller = Role("controller");
            N1 = Role("n1");
            N2 = Role("n2");
            N3 = Role("n3");
            N4 = Role("n4");
            N5 = Role("n5");

            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = INFO
                akka.actor.provider = cluster
                akka.log-dead-letters = off
                akka.log-dead-letters-during-shutdown = off
                akka.remote.log-remote-lifecycle-events = ERROR
                akka.testconductor.barrier-timeout = 60s")
                .WithFallback(DistributedData.DefaultConfig());

            TestTransport = true;
        }
    }

    public class JepsenInspiredInsertSpec : MultiNodeClusterSpec
    {
        public static readonly RoleName Controller = new RoleName("controller");
        public static readonly RoleName N1 = new RoleName("n1");
        public static readonly RoleName N2 = new RoleName("n2");
        public static readonly RoleName N3 = new RoleName("n3");
        public static readonly RoleName N4 = new RoleName("n4");
        public static readonly RoleName N5 = new RoleName("n5");

        private readonly Cluster.Cluster _cluster;
        private readonly IActorRef _replicator;
        private readonly IImmutableList<RoleName> _nodes;
        private readonly int _nodeCount;
        private readonly TimeSpan _timeout;
        private readonly int _delayMillis = 0;
        private readonly int _totalCount = 200;
        private readonly int[] _expectedData;
        private readonly IImmutableDictionary<RoleName, IEnumerable<int>> _data;

        public IEnumerable<int> MyData => _data[Myself];

        public JepsenInspiredInsertSpec() : this(new JepsenInspiredInsertSpecConfig()) { }
        protected JepsenInspiredInsertSpec(JepsenInspiredInsertSpecConfig config) : base(config, typeof(JepsenInspiredInsertSpec))
        {
            _cluster = Akka.Cluster.Cluster.Get(Sys);
            _replicator = DistributedData.Get(Sys).Replicator;
            _nodes = Roles.Remove(Controller);
            _nodeCount = _nodes.Count;
            _timeout = Dilated(TimeSpan.FromSeconds(3));
            _expectedData = Enumerable.Range(0, _totalCount).ToArray();
            var nodeindex = _nodes.Zip(Enumerable.Range(0, _nodes.Count), (name, i) => new KeyValuePair<int, RoleName>(i, name))
                .ToImmutableDictionary();
            _data = Enumerable.Range(0, _totalCount).GroupBy(i => nodeindex[i % _nodeCount])
                .ToImmutableDictionary(x => x.Key, x => (IEnumerable<int>)x.Reverse().ToArray());
        }

        [MultiNodeFact]
        public void JepsenInspiredInsert_Tests()
        {
            Insert_from_5_nodes_should_setup_cluster();
            Insert_from_5_nodes_should_replicate_values_when_all_nodes_connected();
            Insert_from_5_nodes_should_read_write_to_majority_when_all_nodes_connected();
            Insert_from_5_nodes_should_replicate_values_after_partition();
            Insert_from_5_nodes_should_write_to_majority_during_3_and_2_partition_and_read_from_majority_after_partition();
        }

        public void Insert_from_5_nodes_should_setup_cluster()
        {
            RunOn(() =>
            {
                foreach (var node in _nodes)
                    Join(node, N1);

                Within(TimeSpan.FromSeconds(10), () => AwaitAssert(() =>
                {
                    _replicator.Tell(Dsl.GetReplicaCount);
                    ExpectMsg(new ReplicaCount(_nodes.Count));
                }));
            }, _nodes.ToArray());

            RunOn(() =>
            {
                foreach (var node in _nodes)
                    EnterBarrier(node.Name + "-joined");
            }, Controller);

            EnterBarrier("after-setup");
        }

        public void Insert_from_5_nodes_should_replicate_values_when_all_nodes_connected()
        {
            var key = new ORSetKey<int>("A");
            RunOn(() =>
            {
                var writeProbe = CreateTestProbe();
                var writeAcks = MyData.Select(i =>
                {
                    SleepDelay();
                    _replicator.Tell(Dsl.Update(key, ORSet<int>.Empty, WriteLocal.Instance, i, x => x.Add(_cluster.SelfUniqueAddress, i)), writeProbe.Ref);
                    return writeProbe.ReceiveOne(TimeSpan.FromSeconds(3));
                }).ToArray();
                var successWriteAcks = writeAcks.OfType<UpdateSuccess>().ToArray();
                var failureWriteAcks = writeAcks.OfType<IUpdateFailure>().ToArray();
                successWriteAcks.Select(x => (int)x.Request).ShouldBe(MyData.ToArray());
                successWriteAcks.Length.Should().Be(MyData.Count());
                failureWriteAcks.Should().BeEmpty();
                (successWriteAcks.Length + failureWriteAcks.Length).Should().Be(MyData.Count());

                // eventually all nodes will have the data
                Within(TimeSpan.FromSeconds(15), () =>
                {
                    AwaitAssert(() =>
                    {
                        var readProbe = CreateTestProbe();
                        _replicator.Tell(Dsl.Get(key, ReadLocal.Instance), readProbe.Ref);
                        var result = readProbe.ExpectMsg<GetSuccess>(g => Equals(g.Key, key)).Get(key);
                        result.Elements.Should().BeEquivalentTo(_expectedData);
                    });
                });
            }, _nodes.ToArray());

            EnterBarrier("after-test-1");
        }

        public void Insert_from_5_nodes_should_read_write_to_majority_when_all_nodes_connected()
        {
            var key = new ORSetKey<int>("B");
            var readMajority = new ReadMajority(_timeout);
            var writeMajority = new WriteMajority(_timeout);

            RunOn(() =>
            {
                var writeProbe = CreateTestProbe();
                var writeAcks = MyData.Select(i =>
                {
                    SleepDelay();
                    _replicator.Tell(Dsl.Update(key, ORSet<int>.Empty, writeMajority, i, x => x.Add(_cluster.SelfUniqueAddress, i)), writeProbe.Ref);
                    return writeProbe.ReceiveOne(_timeout.Add(TimeSpan.FromSeconds(1)));
                }).ToArray();
                var successWriteAcks = writeAcks.OfType<UpdateSuccess>().ToArray();
                var failureWriteAcks = writeAcks.OfType<IUpdateFailure>().ToArray();
                successWriteAcks.Select(x => (int)x.Request).Should().BeEquivalentTo(MyData.ToArray());
                successWriteAcks.Length.Should().Be(MyData.Count());
                failureWriteAcks.Should().BeEmpty();
                (successWriteAcks.Length + failureWriteAcks.Length).Should().Be(MyData.Count());

                EnterBarrier("data-written-2");

                // read from majority of nodes, which is enough to retrieve all data
                var readProbe = CreateTestProbe();
                _replicator.Tell(Dsl.Get(key, readMajority), readProbe.Ref);
                var result = readProbe.ExpectMsg<GetSuccess>(g => Equals(g.Key, key)).Get(key);
                result.Elements.Should().BeEquivalentTo(_expectedData);
            }, _nodes.ToArray());

            RunOn(() => EnterBarrier("data-written-2"), Controller);

            EnterBarrier("after-test-2");
        }

        public void Insert_from_5_nodes_should_replicate_values_after_partition()
        {
            var key = new ORSetKey<int>("C");
            RunOn(() =>
            {
                SleepBeforePartition();

                foreach (var a in new List<RoleName> {N1, N4, N5})
                    foreach (var b in new List<RoleName> {N2, N3})
                        TestConductor.Blackhole(a, b, ThrottleTransportAdapter.Direction.Both).Wait(TimeSpan.FromSeconds(3));

                SleepDuringPartition();

                foreach (var a in new List<RoleName> { N1, N4, N5 })
                    foreach (var b in new List<RoleName> { N2, N3 })
                        TestConductor.PassThrough(a, b, ThrottleTransportAdapter.Direction.Both).Wait(TimeSpan.FromSeconds(3));

                EnterBarrier("partition-healed-3");
            }, Controller);

            RunOn(() =>
            {
                var writeProbe = CreateTestProbe();
                var writeAcks = MyData.Select(i =>
                {
                    SleepDelay();
                    _replicator.Tell(Dsl.Update(key, ORSet<int>.Empty, WriteLocal.Instance, i, x => x.Add(_cluster.SelfUniqueAddress, i)), writeProbe.Ref);
                    return writeProbe.ReceiveOne(TimeSpan.FromSeconds(3));
                }).ToArray();
                var successWriteAcks = writeAcks.OfType<UpdateSuccess>().ToArray();
                var failureWriteAcks = writeAcks.OfType<IUpdateFailure>().ToArray();
                successWriteAcks.Select(x => (int)x.Request).Should().BeEquivalentTo(MyData.ToArray());
                successWriteAcks.Length.Should().Be(MyData.Count());
                failureWriteAcks.Should().BeEmpty();
                (successWriteAcks.Length + failureWriteAcks.Length).Should().Be(MyData.Count());

                EnterBarrier("partition-healed-3");

                // eventually all nodes will have the data
                Within(TimeSpan.FromSeconds(15), () => AwaitAssert(() =>
                {
                    var readProbe = CreateTestProbe();
                    _replicator.Tell(Dsl.Get(key, ReadLocal.Instance), readProbe.Ref);
                    var result = readProbe.ExpectMsg<GetSuccess>(g => Equals(g.Key, key)).Get(key);
                    result.Elements.Should().BeEquivalentTo(_expectedData);
                }));
            }, _nodes.ToArray());

            EnterBarrier("after-test-3");
        }

        public void Insert_from_5_nodes_should_write_to_majority_during_3_and_2_partition_and_read_from_majority_after_partition()
        {
            var key = new ORSetKey<int>("D");
            var readMajority = new ReadMajority(_timeout);
            var writeMajority = new WriteMajority(_timeout);
            RunOn(() =>
            {
                SleepBeforePartition();

                foreach (var a in new List<RoleName> { N1, N4, N5 })
                    foreach (var b in new List<RoleName> { N2, N3 })
                        TestConductor.Blackhole(a, b, ThrottleTransportAdapter.Direction.Both).Wait(TimeSpan.FromSeconds(3));

                SleepDuringPartition();

                foreach (var a in new List<RoleName> { N1, N4, N5 })
                    foreach (var b in new List<RoleName> { N2, N3 })
                        TestConductor.PassThrough(a, b, ThrottleTransportAdapter.Direction.Both).Wait(TimeSpan.FromSeconds(3));

                EnterBarrier("partition-healed-4");
            }, Controller);

            RunOn(() =>
            {
                var writeProbe = CreateTestProbe();
                var writeAcks = MyData.Select(i =>
                {
                    SleepDelay();
                    _replicator.Tell(Dsl.Update(key, ORSet<int>.Empty, writeMajority, i, x => x.Add(_cluster.SelfUniqueAddress, i)), writeProbe.Ref);
                    return writeProbe.ReceiveOne(_timeout.Add(TimeSpan.FromSeconds(1)));
                }).ToArray();
                var successWriteAcks = writeAcks.OfType<UpdateSuccess>().ToArray();
                var failureWriteAcks = writeAcks.OfType<IUpdateFailure>().ToArray();

                RunOn(() =>
                {
                    successWriteAcks.Select(x => (int)x.Request).Should().BeEquivalentTo(MyData.ToArray());
                    successWriteAcks.Length.Should().Be(MyData.Count());
                    failureWriteAcks.Should().BeEmpty();
                }, N1, N4, N5);

                RunOn(() =>
                {
                    // without delays all could theoretically have been written before the blackhole
                    if (_delayMillis != 0)
                        failureWriteAcks.Should().NotBeEmpty();
                }, N2, N3);

                (successWriteAcks.Length + failureWriteAcks.Length).Should().Be(MyData.Count());

                EnterBarrier("partition-healed-4");

                // on the 2 node side, read from majority of nodes is enough to read all writes
                RunOn(() =>
                {
                    var readProbe = CreateTestProbe();
                    _replicator.Tell(Dsl.Get(key, readMajority), readProbe.Ref);
                    var result = readProbe.ExpectMsg<GetSuccess>(g => Equals(g.Key, key)).Get(key);
                    result.Elements.Should().BeEquivalentTo(_expectedData);
                }, N2, N3);

                // but on the 3 node side, read from majority doesn't mean that we are guaranteed to see
                // the writes from the other side, yet

                // eventually all nodes will have the data
                Within(TimeSpan.FromSeconds(15), () => AwaitAssert(() =>
                {
                    var readProbe = CreateTestProbe();
                    _replicator.Tell(Dsl.Get(key, ReadLocal.Instance), readProbe.Ref);
                    var result = readProbe.ExpectMsg<GetSuccess>(g => Equals(g.Key, key)).Get(key);
                    result.Elements.Should().BeEquivalentTo(_expectedData);
                }));
            }, _nodes.ToArray());

            EnterBarrier("after-test-4");
        }

        protected override int InitialParticipantsValueFactory => Roles.Count;

        private void Join(RoleName from, RoleName to)
        {
            RunOn(() => _cluster.Join(Node(to).Address), from);
            EnterBarrier(from.Name + "-joined");
        }

        private void SleepDelay()
        {
            if (_delayMillis != 0)
            {
                var randDelay = ThreadLocalRandom.Current.Next(_delayMillis);
                if (randDelay != 0) Thread.Sleep(randDelay);
            }
        }

        private void SleepBeforePartition()
        {
            if (_delayMillis != 0)
                Thread.Sleep(_delayMillis * _totalCount / _nodeCount / 10);
        }

        private void SleepDuringPartition()
        {
            Thread.Sleep(Math.Max(5000, _delayMillis * _totalCount / _nodeCount / 2));
        }
    }
}
