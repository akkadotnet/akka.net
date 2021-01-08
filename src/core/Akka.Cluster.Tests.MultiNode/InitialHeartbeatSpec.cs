//-----------------------------------------------------------------------
// <copyright file="InitialHeartbeatSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Xunit;

namespace Akka.Cluster.Tests.MultiNode
{
    public class InitialHeartbeatMultiNodeConfig : MultiNodeConfig
    {
        private readonly RoleName _controller;

        public RoleName Controller
        {
            get { return _controller; }
        }

        private readonly RoleName _first;

        public RoleName First
        {
            get { return _first; }
        }

        private readonly RoleName _second;

        public RoleName Second
        {
            get { return _second; }
        }

        public InitialHeartbeatMultiNodeConfig()
        {
            _controller = Role("controller");
            _first = Role("first");
            _second = Role("second");

            CommonConfig = DebugConfig(false)
                .WithFallback(MultiNodeLoggingConfig.LoggingConfig)
                .WithFallback(
                ConfigurationFactory.ParseString(@"
                    akka.testconductor.barrier-timeout = 60 s
                    akka.stdout-loglevel = INFO
                    akka.cluster.failure-detector.threshold = 4
                    akka.remote.log-remote-lifecycle-events = on
                    ").WithFallback(MultiNodeClusterSpec.ClusterConfig()));

            TestTransport = true;
        }
        
        public class InitialHeartbeatSpec : MultiNodeClusterSpec
        {
            private readonly InitialHeartbeatMultiNodeConfig _config;

            public InitialHeartbeatSpec() : this(new InitialHeartbeatMultiNodeConfig())
            {
            }

            private InitialHeartbeatSpec(InitialHeartbeatMultiNodeConfig config)
                : base(config, typeof(InitialHeartbeatSpec))
            {
                _config = config;
                MuteMarkingAsUnreachable();
            }

            [MultiNodeFact]
            public void A_member_must_detect_failure_even_though_no_heartbeats_have_been_received()
            {
                var firstAddress = GetAddress(_config.First);
                var secondAddress = GetAddress(_config.Second);

                AwaitClusterUp(_config.First);

                RunOn(() =>
                    AwaitAssert(() =>
                    {
                        Cluster.SendCurrentClusterState(TestActor);
                        Assert.Contains(secondAddress, ExpectMsg<ClusterEvent.CurrentClusterState>().Members.Select(m => m.Address));
                    }, TimeSpan.FromSeconds(20), TimeSpan.FromMilliseconds(50))
                    , _config.First);

                RunOn(() =>
                {
                    Cluster.Join(GetAddress(_config.First));
                    AwaitAssert(() =>
                    {
                        Cluster.SendCurrentClusterState(TestActor);
                        Assert.Contains(firstAddress, ExpectMsg<ClusterEvent.CurrentClusterState>().Members.Select(m => m.Address));
                    }, TimeSpan.FromSeconds(20), TimeSpan.FromMilliseconds(50));
                }, _config.Second);

                EnterBarrier("second-joined");

                // It is likely that second has not started heartbeating to first yet,
                // and when it does the messages doesn't go through and the first extra heartbeat is triggered.
                // If the first heartbeat arrives, it will detect the failure anyway but not really exercise the
                // part that we are trying to test here.
                RunOn(
                    () =>
                        TestConductor.Blackhole(_config.First, _config.Second, ThrottleTransportAdapter.Direction.Both)
                            .Wait(), _config.Controller);

                RunOn(
                    () => AwaitCondition(
                        () => !Cluster.FailureDetector.IsAvailable(GetAddress(_config.First))
                        , TimeSpan.FromSeconds(15))
                    , _config.Second);

                EnterBarrier("after-1");
            }
        }
    }
}

