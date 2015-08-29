using Akka.Actor;
using Akka.Configuration;
using Akka.MultiNodeTests;
using Akka.Remote.TestKit;
using Akka.TestKit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData.Tests.MultiNode
{
    public class ReplicatorSpecConfig : MultiNodeConfig
    {
        readonly RoleName _first;
        readonly RoleName _second;
        readonly RoleName _third;

        public RoleName First { get { return _first; } }
        public RoleName Second { get { return _second; } }
        public RoleName Third { get { return _third; } }

        public ReplicatorSpecConfig()
        {
            _first = Role("first");
            _second = Role("second");
            _third = Role("third");

            CommonConfig = DebugConfig(true).WithFallback(ConfigurationFactory.ParseString(@"
                akka.actor.provider=""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
                akka.test.timefactor=1.0
                akka.test.calling-thread-dispatcher.type=""Akka.TestKit.CallingThreadDispatcherConfigurator, Akka.TestKit""
                akka.test.calling-thread-dispatcher.throughput=2147483647
                akka.test.test-actor.dispatcher.type=""Akka.TestKit.CallingThreadDispatcherConfigurator, Akka.TestKit""
                akka.test.test-actor.dispatcher.throughput=2147483647
                akka.cluster.distributed-data.gossip-interval=2s
            ")).WithFallback(ReplicatorSettings.DefaultConfig());

            TestTransport = true;
        }
    }

    public abstract class ReplicatorSpec : MultiNodeSpec
    {
        readonly ReplicatorSpecConfig _config;
        readonly Cluster.Cluster _cluster;

        readonly IActorRef _replicator;

        readonly GCounterKey KeyA = new GCounterKey("A");
        readonly GCounterKey KeyB = new GCounterKey("B");
        readonly GCounterKey KeyC = new GCounterKey("C");
        readonly GCounterKey KeyD = new GCounterKey("D");
        readonly GCounterKey KeyE = new GCounterKey("E");
        readonly GCounterKey KeyE2 =new GCounterKey("E2");
        readonly GCounterKey KeyF = new GCounterKey("F");
        //readonly ORSetKey KeyG = new ORSetKey[String]("G")
        //readonly GCounterKey KeyH = new ORMapKey[Flag]("H")
        //readonly GCounterKey KeyI = new GSetKey[String]("I")
        //readonly GCounterKey KeyJ = new GSetKey[String]("J")
        readonly GCounterKey KeyX = new GCounterKey("X");
        readonly GCounterKey KeyY = new GCounterKey("Y");
        readonly GCounterKey KeyZ = new GCounterKey("Z");

        readonly TimeSpan _timeOut;
        readonly WriteTo _writeTwo;
        readonly WriteMajority _writeMajority;
        readonly WriteAll _writeAll;
        readonly ReadFrom _readFrom;
        readonly ReadMajority _readMajority;
        readonly ReadAll _readAll;

        public ReplicatorSpec()
            : this(new ReplicatorSpecConfig())
        { }

        public ReplicatorSpec(ReplicatorSpecConfig config)
            : base(config)
        {
            _config = config;
            _cluster = Cluster.Cluster.Get(Sys);
            var settings = new ReplicatorSettings(Sys).WithGossipInterval(TimeSpan.FromSeconds(1.0)).WithMaxDeltaElements(10);
            var props = Replicator.GetProps(settings);
            _replicator = Sys.ActorOf(props, "replicator");

            _timeOut = Dilated(TimeSpan.FromSeconds(2.0));
            _writeTwo = new WriteTo(2, _timeOut);
            _writeMajority = new WriteMajority(_timeOut);
            _writeAll = new WriteAll(_timeOut);
            _readFrom = new ReadFrom(2, _timeOut);
            _readMajority = new ReadMajority(_timeOut);
            _readAll = new ReadAll(_timeOut);
        }

        [MultiNodeFact]
        public void AClusterCRDTMustWorkInSingleNodeCluster()
        {
            RunOn(() =>
                {
                    Within(TimeSpan.FromSeconds(5.0), () =>
                        {
                            _replicator.Tell(new GetReplicaCount());
                            ExpectMsg<ReplicaCount>();
                        });

                    var changedProbe = this.CreateTestProbe();
                    _replicator.Tell(new Subscribe<GCounter>(KeyA, changedProbe.Ref));
                    _replicator.Tell(new Subscribe<GCounter>(KeyX, changedProbe.Ref));

                    Within(TimeSpan.FromSeconds(5.0), () =>
                        {
                            _replicator.Tell(new Get<GCounter>(KeyA, ReadLocal.Instance));
                            ExpectMsg(new NotFound<GCounter>(KeyA, null));
                        });

                    var c3 = GCounter.Empty.Increment(_cluster.SelfUniqueAddress, 3);
                    var update = new Update<GCounter>(KeyA, GCounter.Empty, WriteLocal.Instance, x => ((GCounter)x).Increment(_cluster.SelfUniqueAddress, 3));
                    _replicator.Tell(update);
                    ExpectMsg(new UpdateSuccess<GCounter>(KeyA, null));
                    changedProbe.ExpectMsg(new Changed<GCounter>(KeyA, c3));
                    _replicator.Tell(new Get<GCounter>(KeyA, ReadLocal.Instance));
                    ExpectMsg(new GetSuccess<GCounter>(KeyA, null, c3));

                    var changedProbe2 = CreateTestProbe();
                    _replicator.Tell(new Subscribe<GCounter>(KeyA, changedProbe2.Ref));
                    changedProbe2.ExpectMsg(new Changed<GCounter>(KeyA, c3));

                    
                    var c4 = c3.Increment(_cluster.SelfUniqueAddress);
                    _replicator.Tell(new Update<GCounter>(KeyA, _writeTwo, x => ((GCounter)x).Increment(_cluster.SelfUniqueAddress)));
                    ExpectMsg(new UpdateTimeout<GCounter>(KeyA, null));
                }, _config.First);
        }

        protected override int InitialParticipantsValueFactory
        {
            get { return Roles.Count; }
        }
    }

    public class ReplicatorSpecNode1 : ReplicatorSpec
    { }

    public class ReplicatorSpecNode2 : ReplicatorSpec
    { }

    public class ReplicatorSpecNode3 : ReplicatorSpec
    { }
}
