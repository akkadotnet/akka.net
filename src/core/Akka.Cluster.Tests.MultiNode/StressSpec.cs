using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.TestKit;

namespace Akka.Cluster.Tests.MultiNode
{
    public class StressSpecConfig : MultiNodeConfig
    {
        public int TotalNumberOfNodes => Environment.GetEnvironmentVariable("MultiNode.Akka.Cluster.Stress.NrOfNodes") switch
        {
            string e when string.IsNullOrEmpty(e) => 13,
            string val => int.Parse(val)
        };

        public StressSpecConfig()
        {
            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.test.cluster-stress-spec {
      infolog = off
      # scale the nr-of-nodes* settings with this factor
      nr-of-nodes-factor = 1
      # not scaled
      nr-of-seed-nodes = 3
      nr-of-nodes-joining-to-seed-initially = 2
      nr-of-nodes-joining-one-by-one-small = 2
      nr-of-nodes-joining-one-by-one-large = 2
      nr-of-nodes-joining-to-one = 2
      nr-of-nodes-leaving-one-by-one-small = 1
      nr-of-nodes-leaving-one-by-one-large = 1
      nr-of-nodes-leaving = 2
      nr-of-nodes-shutdown-one-by-one-small = 1
      nr-of-nodes-shutdown-one-by-one-large = 1
      nr-of-nodes-partition = 2
      nr-of-nodes-shutdown = 2
      nr-of-nodes-join-remove = 2
      # not scaled
      # scale the *-duration settings with this factor
      duration-factor = 1
      join-remove-duration = 90s
      idle-gossip-duration = 10s
      expected-test-duration = 600s
      # scale convergence within timeouts with this factor
      convergence-within-factor = 1.0
    }
    akka.actor.provider = cluster
    akka.cluster {
      failure-detector.acceptable-heartbeat-pause =  3s
      downing-provider-class = ""Akka.Cluster.SBR.SplitBrainResolverProvider, Akka.Cluster""
      split-brain-resolver {
          stable-after = 10s
      }
      publish-stats-interval = 1s
    }
    akka.loggers = [""Akka.TestKit.TestEventListener, Akka.TestKit""]
            akka.loglevel = INFO
            akka.remote.log-remote-lifecycle-events = off
            akka.actor.default-dispatcher.fork-join-executor {
                parallelism - min = 8
                parallelism - max = 8
            }
            ");

            TestTransport = true;
        }

        public class Settings
        {
            private readonly Config _testConfig;

            public Settings(Config config, int totalNumberOfNodes)
            {
                TotalNumberOfNodes = totalNumberOfNodes;
                _testConfig = config.GetConfig("akka.test.cluster-stress-spec");
                Infolog = _testConfig.GetBoolean("infolog");
                NFactor = _testConfig.GetInt("nr-of-nodes-factor");
                NumberOfSeedNodes = _testConfig.GetInt("nr-of-seed-nodes");
                NumberOfNodesJoiningToSeedNodesInitially =
                    _testConfig.GetInt("nr-of-nodes-joining-to-seed-initially") * NFactor;
                NumberOfNodesJoiningOneByOneSmall = _testConfig.GetInt("nr-of-nodes-joining-one-by-one-small") * NFactor;
                NumberOfNodesJoiningOneByOneLarge = _testConfig.GetInt("nr-of-nodes-joining-one-by-one-large") * NFactor;
                NumberOfNodesJoiningToOneNode = _testConfig.GetInt("nr-of-nodes-joining-to-one") * NFactor;
                // remaining will join to seed nodes
                NumberOfNodesJoiningToSeedNodes = (totalNumberOfNodes - NumberOfSeedNodes -
                                                   NumberOfNodesJoiningToSeedNodesInitially -
                                                   NumberOfNodesJoiningOneByOneSmall -
                                                   NumberOfNodesJoiningOneByOneLarge - NumberOfNodesJoiningToOneNode);
                if (NumberOfNodesJoiningToSeedNodes < 0)
                    throw new ArgumentOutOfRangeException("nr-of-nodes-joining-*",
                        $"too many configured nr-of-nodes-joining-*, total should be <= {totalNumberOfNodes}");

                NumberOfNodesLeavingOneByOneSmall = _testConfig.GetInt("nr-of-nodes-leaving-one-by-one-small") * NFactor;
                NumberOfNodesLeavingOneByOneLarge = _testConfig.GetInt("nr-of-nodes-leaving-one-by-one-large") * NFactor;
                NumberOfNodesLeaving = _testConfig.GetInt("nr-of-nodes-leaving") * NFactor;
                NumberOfNodesShutdownOneByOneSmall = _testConfig.GetInt("nr-of-nodes-shutdown-one-by-one-small") * NFactor;
                NumberOfNodesShutdownOneByOneLarge = _testConfig.GetInt("nr-of-nodes-shutdown-one-by-one-large") * NFactor;
                NumberOfNodesShutdown = _testConfig.GetInt("nr-of-nodes-shutdown") * NFactor;
                NumberOfNodesPartition = _testConfig.GetInt("nr-of-nodes-partition") * NFactor;
                NumberOfNodesJoinRemove = _testConfig.GetInt("nr-of-nodes-join-remove"); // not scaled by nodes factor

                DFactor = _testConfig.GetInt("duration-factor");
                JoinRemoveDuration = TimeSpan.FromMilliseconds(_testConfig.GetTimeSpan("join-remove-duration").TotalMilliseconds * DFactor);
                IdleGossipDuration = TimeSpan.FromMilliseconds(_testConfig.GetTimeSpan("idle-gossip-duration").TotalMilliseconds * DFactor);
                ExpectedTestDuration = TimeSpan.FromMilliseconds(_testConfig.GetTimeSpan("expected-test-duration").TotalMilliseconds * DFactor);
                ConvergenceWithinFactor = _testConfig.GetDouble("convergence-within-factor");

                if (NumberOfSeedNodes + NumberOfNodesJoiningToSeedNodesInitially + NumberOfNodesJoiningOneByOneSmall +
                    NumberOfNodesJoiningOneByOneLarge + NumberOfNodesJoiningToOneNode +
                    NumberOfNodesJoiningToSeedNodes > totalNumberOfNodes)
                {
                    throw new ArgumentOutOfRangeException("nr-of-nodes-joining-*",
                        $"specified number of joining nodes <= {totalNumberOfNodes}");
                }

                // don't shutdown the 3 nodes hosting the master actors
                if (NumberOfNodesLeavingOneByOneSmall + NumberOfNodesLeavingOneByOneLarge + NumberOfNodesLeaving +
                      NumberOfNodesShutdownOneByOneSmall + NumberOfNodesShutdownOneByOneLarge + NumberOfNodesShutdown >
                      totalNumberOfNodes - 3)
                {
                    throw new ArgumentOutOfRangeException("nr-of-nodes-leaving-*",
                        $"specified number of leaving/shutdown nodes <= {totalNumberOfNodes - 3}");
                }

                if (NumberOfNodesJoinRemove > totalNumberOfNodes)
                {
                    throw new ArgumentOutOfRangeException("nr-of-nodes-join-remove*",
                        $"nr-of-nodes-join-remove should be <= {totalNumberOfNodes}");
                }
            }

            public int TotalNumberOfNodes { get; }

            public bool Infolog { get; }
            public int NFactor { get; }

            public int NumberOfSeedNodes { get; }

            public int NumberOfNodesJoiningToSeedNodesInitially { get; }

            public int NumberOfNodesJoiningOneByOneSmall { get; }

            public int NumberOfNodesJoiningOneByOneLarge { get; }

            public int NumberOfNodesJoiningToOneNode { get; }

            public int NumberOfNodesJoiningToSeedNodes { get; }

            public int NumberOfNodesLeavingOneByOneSmall { get; }

            public int NumberOfNodesLeavingOneByOneLarge { get; }

            public int NumberOfNodesLeaving { get; }

            public int NumberOfNodesShutdownOneByOneSmall { get; }

            public int NumberOfNodesShutdownOneByOneLarge { get; }

            public int NumberOfNodesShutdown { get; }

            public int NumberOfNodesPartition { get; }

            public int NumberOfNodesJoinRemove { get; }

            public int DFactor { get; }

            public TimeSpan JoinRemoveDuration { get; }

            public TimeSpan IdleGossipDuration { get; }

            public TimeSpan ExpectedTestDuration { get; }

            public double ConvergenceWithinFactor { get; }

            public override string ToString()
            {
                return _testConfig.WithFallback($"nrOfNodes={TotalNumberOfNodes}").Root.ToString(2);
            }
        }
    }

    record ClusterResult(Address address, TimeSpan duration, GossipStats clusterStats);

    class StressSpec
    {
    }
}
