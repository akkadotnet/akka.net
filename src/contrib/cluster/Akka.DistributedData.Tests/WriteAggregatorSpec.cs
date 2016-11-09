//-----------------------------------------------------------------------
// <copyright file="WriteAggregatorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.DistributedData.Internal;
using Xunit;
using Xunit.Abstractions;

namespace Akka.DistributedData.Tests
{
    [Collection("DistributedDataSpec")]
    public class WriteAggregatorSpec : Akka.TestKit.Xunit2.TestKit
    {
        internal class TestWriteAggregator : WriteAggregator
        {
            private readonly IImmutableDictionary<Address, IActorRef> _probes;

            public TestWriteAggregator(
                GSet<string> data, 
                IWriteConsistency consistency, 
                IImmutableDictionary<Address, IActorRef> probes,
                IImmutableSet<Address> nodes,
                IActorRef replyTo) 
                : base(Key, new DataEnvelope(data), consistency, null, nodes, replyTo)
            {
                _probes = probes;
            }

            protected override ActorSelection Replica(Address address) => Context.ActorSelection(_probes[address].Path);
            protected override Address SenderAddress => _probes.First(kv => Equals(kv.Value, Sender)).Key;
        }

        internal class WriteAckAdapter : ReceiveActor
        {
            public WriteAckAdapter(IActorRef replica)
            {
                IActorRef replicator = null;
                Receive<WriteAck>(ack => replicator?.Tell(ack));
                ReceiveAny(msg =>
                {
                    replicator = Sender;
                    replica.Tell(msg);
                });
            }
        }

        private static readonly GSetKey<string> Key = new GSetKey<string>("a");

        private readonly Address _nodeA = new Address("akka.tcp", "Sys", "a", 2552);
        private readonly Address _nodeB = new Address("akka.tcp", "Sys", "b", 2552);
        private readonly Address _nodeC = new Address("akka.tcp", "Sys", "c", 2552);
        private readonly Address _nodeD = new Address("akka.tcp", "Sys", "d", 2552);
        private readonly IImmutableSet<Address> _nodes;

        private readonly GSet<string> _data = GSet.Create("A", "B");
        private readonly WriteTo _writeTwo = new WriteTo(2, TimeSpan.FromSeconds(3));
        private readonly WriteMajority _writeMajority = new WriteMajority(TimeSpan.FromSeconds(3));


        public WriteAggregatorSpec(ITestOutputHelper output) : base(ConfigurationFactory.ParseString(@"
            akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
            akka.remote.helios.tcp.port = 0"), "WriteAggregatorSpec", output)
        {
            _nodes = ImmutableHashSet.CreateRange(new[] {_nodeA, _nodeB, _nodeC, _nodeD});
        }

        [Fact]
        public void WriteAggregator_should_send_at_least_half_N_plus_1_replicas_when_WriteMajority()
        {
            var probe = CreateTestProbe();
            var aggregator = Sys.ActorOf(Props.Create(() => new TestWriteAggregator(_data, _writeMajority, Probes(probe.Ref), _nodes, TestActor)));

            probe.ExpectMsg<Write>();
            probe.LastSender.Tell(WriteAck.Instance);
            probe.ExpectMsg<Write>();
            probe.LastSender.Tell(WriteAck.Instance);
            ExpectMsg(new Replicator.UpdateSuccess(Key, null));
            Watch(aggregator);
            ExpectTerminated(aggregator);
        }

        [Fact]
        public void WriteAggregator_should_send_to_more_when_no_immediate_reply()
        {
            var probe = CreateTestProbe();
            var aggregator = Sys.ActorOf(Props.Create(() => new TestWriteAggregator(_data, _writeMajority, Probes(probe.Ref), _nodes, TestActor)));

            probe.ExpectMsg<Write>();
            // no reply
            probe.ExpectMsg<Write>();
            // no reply
            probe.LastSender.Tell(WriteAck.Instance);
            probe.ExpectMsg<Write>();
            probe.LastSender.Tell(WriteAck.Instance);
            probe.ExpectMsg<Write>();
            probe.LastSender.Tell(WriteAck.Instance);
            ExpectMsg(new Replicator.UpdateSuccess(Key, null));
            Watch(aggregator);
            ExpectTerminated(aggregator);
        }

        [Fact]
        public void WriteAggregator_should_timeout_when_less_than_required_ACKs()
        {
            var probe = CreateTestProbe();
            var aggregator = Sys.ActorOf(Props.Create(() => new TestWriteAggregator(_data, _writeMajority, Probes(probe.Ref), _nodes, TestActor)));

            probe.ExpectMsg<Write>();
            // no reply
            probe.ExpectMsg<Write>();
            probe.LastSender.Tell(WriteAck.Instance);
            probe.ExpectMsg<Write>();
            // no reply
            probe.ExpectMsg<Write>();
            // no reply
            ExpectMsg(new Replicator.UpdateTimeout(Key, null));
            Watch(aggregator);
            ExpectTerminated(aggregator);
        }

        private IImmutableDictionary<Address, IActorRef> Probes(IActorRef probe) =>
            _nodes.Select(address => 
                new KeyValuePair<Address, IActorRef>(
                    address,
                    Sys.ActorOf(Props.Create(() => new WriteAckAdapter(probe)))))
                .ToImmutableDictionary();
    }
}