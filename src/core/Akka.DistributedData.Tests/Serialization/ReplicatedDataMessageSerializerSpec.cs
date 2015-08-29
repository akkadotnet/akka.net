using Akka.Actor;
using Akka.Cluster;
using Akka.Configuration;
using Akka.DistributedData.Proto;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Akka.DistributedData.Tests.Serialization
{
    public class ReplicatedDataMessageSerializerSpec : TestKit.Xunit2.TestKit
    {
        readonly ReplicatedDataSerializer _serializer;
        readonly ActorSystem _system;

        readonly UniqueAddress _address1;
        readonly UniqueAddress _address2;
        readonly UniqueAddress _address3;

        readonly GSetKey<string> _keyA;

        private void CheckSerialization(Object any)
        {
            var blob = _serializer.ToBinary(any);
            var @ref = _serializer.FromBinary(blob, _serializer.Manifest(any));
            Assert.Equal(any, @ref);
        }

        public ReplicatedDataMessageSerializerSpec()
            : this(ActorSystem.Create("ReplicatorMessageSerializerSpec", ConfigurationFactory.ParseString(@"
                akka.actor.provider=""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
                akka.remote.helios.tcp.port=0
                akka.test.timefactor=1.0
                akka.test.calling-thread-dispatcher.type=""Akka.TestKit.CallingThreadDispatcherConfigurator, Akka.TestKit""
                akka.test.calling-thread-dispatcher.throughput=2147483647
                akka.test.test-actor.dispatcher.type=""Akka.TestKit.CallingThreadDispatcherConfigurator, Akka.TestKit""
                akka.test.test-actor.dispatcher.throughput=2147483647
            ")))
        {
        }

        private ReplicatedDataMessageSerializerSpec(ActorSystem system)
            : base(system)
        {

            _keyA = new GSetKey<string>("A");

            _serializer = new ReplicatedDataSerializer((ExtendedActorSystem)system);
            _system = system;

            _address1 = new UniqueAddress(new Address("akka.tcp", system.Name, "some.host.org", 4711), 1);
            _address2 = new UniqueAddress(new Address("akka.tcp", system.Name, "other.host.org", 4711), 2);
            _address3 = new UniqueAddress(new Address("akka.tcp", system.Name, "some.host.org", 4712), 3);
        }

        [Fact]
        public void ReplicatedDataSerializerMustSerializeReplicatedData()
        {
            var ref1 = _system.ActorOf(Props.Empty, "ref1");
            var data1 = new GSet<string>().Add("a");

            CheckSerialization(new Flag());
            CheckSerialization(new Flag().SwitchOn());

            CheckSerialization(new GCounter());
            CheckSerialization(new GCounter().Increment(_address1, 3));
            CheckSerialization(new GCounter().Increment(_address1, 2).Increment(_address2, 5));

            CheckSerialization(new GSet<string>());
            CheckSerialization(new GSet<string>().Add("Hello").Add("World"));

            _system.Shutdown();
        }

        protected override void AfterAll()
        {
            _system.Shutdown();
        }
    }
}
