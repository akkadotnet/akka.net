//-----------------------------------------------------------------------
// <copyright file="ClusterAccrualFailureDetectorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Akka.TestKit;
using Akka.Cluster.Tests.MultiNode;

namespace Akka.Cluster.Tests.MultiNode
{
    public class ClusterAccrualFailureDetectorMultiSpec : MultiNodeConfig {
        public RoleName First { get; private set; }

        public RoleName Second { get; private set; }

        public RoleName Third { get; private set; }

        public ClusterAccrualFailureDetectorMultiSpec()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig= DebugConfig(false)
                            .WithFallback(ConfigurationFactory.ParseString("akka.cluster.failure-detector.threshold = 4"))
                            .WithFallback(MultiNodeClusterSpec.ClusterConfig());

            TestTransport = true;
        }
    }

    public class ClusterAccrualFailureDetectorSpec : MultiNodeClusterSpec
    {
        private readonly ClusterAccrualFailureDetectorMultiSpec _config;

        public ClusterAccrualFailureDetectorSpec()
            : this(new ClusterAccrualFailureDetectorMultiSpec())
        {
            MuteMarkingAsUnreachable();
        }

        protected ClusterAccrualFailureDetectorSpec(ClusterAccrualFailureDetectorMultiSpec config)
            : base(config, typeof(ClusterAccrualFailureDetectorSpec))
        {
            _config = config;
        }

        [MultiNodeFact]
        public void ClusterAccrualFailureDetectorSpecs()
        {
            A_heartbeat_driven_Failure_Detector_receive_heartbeats_so_that_all_member_nodes_in_the_cluster_are_marked_available
                ();
            A_heartbeat_driven_Failure_Detector_mark_node_as_unavailable_when_network_partition_and_then_back_to_available_when_partition_is_healed
                ();
            A_heartbeat_driven_Failure_Detector_mark_node_as_unavailable_if_a_node_in_the_cluster_is_shut_down_and_its_heartbeats_stops
                ();
        }

        public void
            A_heartbeat_driven_Failure_Detector_receive_heartbeats_so_that_all_member_nodes_in_the_cluster_are_marked_available
            ()
        {
            AwaitClusterUp(_config.First, _config.Second, _config.Third);

            Thread.Sleep(5);
            Cluster.FailureDetector.IsAvailable(GetAddress(_config.First)).ShouldBeTrue();
            Cluster.FailureDetector.IsAvailable(GetAddress(_config.Second)).ShouldBeTrue();
            Cluster.FailureDetector.IsAvailable(GetAddress(_config.Third)).ShouldBeTrue();

            EnterBarrier("after-1");
        }

        public void
            A_heartbeat_driven_Failure_Detector_mark_node_as_unavailable_when_network_partition_and_then_back_to_available_when_partition_is_healed
            ()
        {
            RunOn(() => {
                TestConductor.Blackhole(_config.First, _config.Second, ThrottleTransportAdapter.Direction.Both).Wait();
            }, _config.First);

            EnterBarrier("broken");

            RunOn(() =>
            {
                // detect failure...
                AwaitCondition(() => !Cluster.FailureDetector.IsAvailable(GetAddress(_config.Second)),
                    TimeSpan.FromSeconds(15));
                // other connections still ok
                Cluster.FailureDetector.IsAvailable(GetAddress(_config.Third)).ShouldBeTrue();
            }, _config.First);

            RunOn(() =>
            {
                // detect failure...
                AwaitCondition(() => !Cluster.FailureDetector.IsAvailable(GetAddress(_config.First)),
                    TimeSpan.FromSeconds(15));
                // other connections still ok
                Cluster.FailureDetector.IsAvailable(GetAddress(_config.Third)).ShouldBeTrue();
            }, _config.Second);


            EnterBarrier("partitioned");

            RunOn(() => {
                TestConductor.PassThrough(_config.First, _config.Second, ThrottleTransportAdapter.Direction.Both).Wait();
            }, _config.First);

            EnterBarrier("repaired");

            RunOn(() =>
            {
                AwaitCondition(() => Cluster.FailureDetector.IsAvailable(GetAddress(_config.Second)),
                    TimeSpan.FromSeconds(15));
            }, _config.First, _config.Third);

            RunOn(() =>
            {
                AwaitCondition(() => Cluster.FailureDetector.IsAvailable(GetAddress(_config.First)),
                    TimeSpan.FromSeconds(15));
            }, _config.Second);

            EnterBarrier("after-2");
        }
        
        public void
            A_heartbeat_driven_Failure_Detector_mark_node_as_unavailable_if_a_node_in_the_cluster_is_shut_down_and_its_heartbeats_stops
            ()
        {
            RunOn(() => {
                TestConductor.Exit(_config.Third, 0).Wait();
            }, _config.First);

            EnterBarrier("third-shutdown");

            RunOn(() =>
            {
                // remaining nodes should detect failure...
                AwaitCondition(() => !Cluster.FailureDetector.IsAvailable(GetAddress(_config.Third)), TimeSpan.FromSeconds(15));
                // other connections still ok
                Cluster.FailureDetector.IsAvailable(GetAddress(_config.First)).ShouldBeTrue();
                Cluster.FailureDetector.IsAvailable(GetAddress(_config.Second)).ShouldBeTrue();
            }, _config.First, _config.Second);

            EnterBarrier("after-3");
        }
    }
}
