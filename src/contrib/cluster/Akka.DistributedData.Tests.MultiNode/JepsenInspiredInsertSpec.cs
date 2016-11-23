//-----------------------------------------------------------------------
// <copyright file="JepsenInspiredInsertSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Akka.TestKit;
using Akka.Util;

namespace Akka.DistributedData.Tests.MultiNode
{
    public class JepsenInspiredInsertSpec : MultiNodeSpec
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
        public JepsenInspiredInsertSpec(JepsenInspiredInsertSpecConfig config) : base(config)
        {
            _cluster = Cluster.Cluster.Get(Sys);
            _replicator = DistributedData.Get(Sys).Replicator;
            _nodes = Roles.Remove(Controller);
            _nodeCount = _nodes.Count;
            _timeout = Dilated(TimeSpan.FromSeconds(3));
            _expectedData = Enumerable.Range(0, _totalCount).ToArray();
            var nodeindex = _nodes.Zip(Enumerable.Range(0, _nodes.Count - 1), (name, i) => new KeyValuePair<int, RoleName>(i, name))
                .ToImmutableDictionary();
            _data = Enumerable.Range(0, _totalCount).GroupBy(i => nodeindex[i % _nodeCount])
                .ToImmutableDictionary(x => x.Key, x => (IEnumerable<int>)x.ToArray());
        }

        //[MultiNodeFact]
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
                    ExpectMsg(new Replicator.ReplicaCount(_nodes.Count));
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
                var successWriteAcks = writeAcks.OfType<Replicator.UpdateSuccess>().ToArray();
                var failureWriteAcks = writeAcks.OfType<Replicator.IUpdateFailure>().ToArray();
                successWriteAcks.Select(x => (int)x.Request).ShouldBe(MyData.ToArray());
                successWriteAcks.Length.ShouldBe(MyData.Count());
                failureWriteAcks.ShouldBe(new Replicator.IUpdateFailure[0]);
                (successWriteAcks.Length + failureWriteAcks.Length).ShouldBe(MyData.Count());

                // eventually all nodes will have the data
                Within(TimeSpan.FromSeconds(15), () =>
                {
                    AwaitAssert(() =>
                    {
                        var readProbe = CreateTestProbe();
                        _replicator.Tell(Dsl.Get(key, ReadLocal.Instance), readProbe.Ref);
                        var result = readProbe.ExpectMsg<Replicator.GetSuccess>(g => Equals(g.Key, key)).Get(key);
                        result.Elements.ShouldBe(_expectedData);
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
                var successWriteAcks = writeAcks.OfType<Replicator.UpdateSuccess>().ToArray();
                var failureWriteAcks = writeAcks.OfType<Replicator.IUpdateFailure>().ToArray();
                successWriteAcks.Select(x => (int)x.Request).ShouldBe(MyData.ToArray());
                successWriteAcks.Length.ShouldBe(MyData.Count());
                failureWriteAcks.ShouldBe(new Replicator.IUpdateFailure[0]);
                (successWriteAcks.Length + failureWriteAcks.Length).ShouldBe(MyData.Count());

                EnterBarrier("data-written-2");

                // read from majority of nodes, which is enough to retrieve all data
                var readProbe = CreateTestProbe();
                _replicator.Tell(Dsl.Get(key, readMajority), readProbe.Ref);
                var result = readProbe.ExpectMsg<Replicator.GetSuccess>(g => Equals(g.Key, key)).Get(key);
                result.Elements.ShouldBe(_expectedData);
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
                var successWriteAcks = writeAcks.OfType<Replicator.UpdateSuccess>().ToArray();
                var failureWriteAcks = writeAcks.OfType<Replicator.IUpdateFailure>().ToArray();
                successWriteAcks.Select(x => (int)x.Request).ShouldBe(MyData.ToArray());
                successWriteAcks.Length.ShouldBe(MyData.Count());
                failureWriteAcks.ShouldBe(new Replicator.IUpdateFailure[0]);
                (successWriteAcks.Length + failureWriteAcks.Length).ShouldBe(MyData.Count());

                EnterBarrier("partition-healed-3");

                // eventually all nodes will have the data
                Within(TimeSpan.FromSeconds(15), () => AwaitAssert(() =>
                {
                    var readProbe = CreateTestProbe();
                    _replicator.Tell(Dsl.Get(key, ReadLocal.Instance), readProbe.Ref);
                    var result = readProbe.ExpectMsg<Replicator.GetSuccess>(g => Equals(g.Key, key)).Get(key);
                    result.Elements.ShouldBe(_expectedData);
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
                var successWriteAcks = writeAcks.OfType<Replicator.UpdateSuccess>().ToArray();
                var failureWriteAcks = writeAcks.OfType<Replicator.IUpdateFailure>().ToArray();

                RunOn(() =>
                {
                    successWriteAcks.Select(x => (int)x.Request).ShouldBe(MyData.ToArray());
                    successWriteAcks.Length.ShouldBe(MyData.Count());
                    failureWriteAcks.ShouldBe(new Replicator.IUpdateFailure[0]);
                }, N1, N4, N5);

                RunOn(() =>
                {
                    // without delays all could teoretically have been written before the blackhole
                    if (_delayMillis != 0)
                        failureWriteAcks.ShouldNotBe(new Replicator.IUpdateFailure[0]);
                }, N2, N3);

                (successWriteAcks.Length + failureWriteAcks.Length).ShouldBe(MyData.Count());

                EnterBarrier("partition-healed-4");

                // on the 2 node side, read from majority of nodes is enough to read all writes
                RunOn(() =>
                {
                    var readProbe = CreateTestProbe();
                    _replicator.Tell(Dsl.Get(key, readMajority), readProbe.Ref);
                    var result = readProbe.ExpectMsg<Replicator.GetSuccess>(g => Equals(g.Key, key)).Get(key);
                    result.Elements.ShouldBe(_expectedData);
                }, N2, N3);

                // but on the 3 node side, read from majority doesn't mean that we are guaranteed to see
                // the writes from the other side, yet

                // eventually all nodes will have the data
                Within(TimeSpan.FromSeconds(15), () => AwaitAssert(() =>
                {
                    var readProbe = CreateTestProbe();
                    _replicator.Tell(Dsl.Get(key, ReadLocal.Instance), readProbe.Ref);
                    var result = readProbe.ExpectMsg<Replicator.GetSuccess>(g => Equals(g.Key, key)).Get(key);
                    result.Elements.ShouldBe(_expectedData);
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

    public class JepsenInspiredInsertSpecNode1 : JepsenInspiredInsertSpec { }
    public class JepsenInspiredInsertSpecNode2 : JepsenInspiredInsertSpec { }
    public class JepsenInspiredInsertSpecNode3 : JepsenInspiredInsertSpec { }
    public class JepsenInspiredInsertSpecNode4 : JepsenInspiredInsertSpec { }
    public class JepsenInspiredInsertSpecNode5 : JepsenInspiredInsertSpec { }
    public class JepsenInspiredInsertSpecNode6 : JepsenInspiredInsertSpec { }

    public class JepsenInspiredInsertSpecConfig : MultiNodeConfig
    {
        public JepsenInspiredInsertSpecConfig()
        {
            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = INFO
                akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
                akka.log-dead-letters = off
                akka.log-dead-letters-during-shutdown = off
                akka.remote.log-remote-lifecycle-events = ERROR
                akka.testconductor.barrier-timeout = 60s");

            TestTransport = true;
        }
    }
}