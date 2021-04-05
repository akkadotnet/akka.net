using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote;
using Akka.Remote.TestKit;
using Akka.Util;
using Google.Protobuf.WellKnownTypes;

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

    internal sealed class ClusterResult
    {
        public ClusterResult(Address address, TimeSpan duration, GossipStats clusterStats)
        {
            Address = address;
            Duration = duration;
            ClusterStats = clusterStats;
        }

        public Address Address { get; } 
        public TimeSpan Duration { get; } 
        public GossipStats ClusterStats { get; }
    }

    internal sealed class AggregatedClusterResult
    {
        public AggregatedClusterResult(string title, TimeSpan duration, GossipStats clusterStats)
        {
            Title = title;
            Duration = duration;
            ClusterStats = clusterStats;
        }

        public string Title { get; }

        public TimeSpan Duration { get; }

        public GossipStats ClusterStats { get; }
    }

    /// <summary>
    /// Central aggregator of cluster statistics and metrics.
    ///
    /// Reports the result via log periodically and when all
    /// expected results has been collected. It shuts down
    /// itself when expected results has been collected.
    /// </summary>
    internal class ClusterResultAggregator : ReceiveActor
    {
        private readonly string _title;
        private readonly int _expectedResults;
        private readonly StressSpecConfig.Settings _settings;

        private readonly ILoggingAdapter _log = Context.GetLogger();

        private Option<IActorRef> _reportTo = Option<IActorRef>.None;
        private ImmutableList<ClusterResult> _results = ImmutableList<ClusterResult>.Empty;
        private ImmutableSortedDictionary<Address, ImmutableSortedSet<PhiValue>> _phiValuesObservedByNode = 
            ImmutableSortedDictionary<Address, ImmutableSortedSet<PhiValue>>.Empty.WithComparers(Member.AddressOrdering);
        private ImmutableSortedDictionary<Address, ClusterEvent.CurrentInternalStats> _clusterStatsObservedByNode = 
            ImmutableSortedDictionary<Address, ClusterEvent.CurrentInternalStats>.Empty.WithComparers(Member.AddressOrdering);

        public static readonly string FormatPhiHeader = "[Monitor]\t[Subject]\t[count]\t[count phi > 1.0]\t[max phi]";

        public string FormatPhiLine(Address monitor, Address subject, PhiValue phi)
        {
            return $"{monitor}\t{subject}\t{phi.Count}\t{phi.CountAboveOne}\t{phi.Max:F2}";
        }

        public string FormatPhi()
        {
            if (_phiValuesObservedByNode.IsEmpty) return string.Empty;
            else
            {
                //var lines = _phiValuesObservedByNode.Select(
                //    x => x.Value.SelectMany(y => FormatPhiLine(x.Key, y.Address, y)));
                var lines = (from mon in _phiValuesObservedByNode from phi in mon.Value select FormatPhiLine(mon.Key, phi.Address, phi));
                return FormatPhiHeader + Environment.NewLine + string.Join(Environment.NewLine, lines);
            }
        }

        public TimeSpan MaxDuration => _results.Max(x => x.Duration);

        public GossipStats TotalGossipStats =>
            _results.Aggregate(new GossipStats(), (stats, result) => stats += result.ClusterStats);

        public string FormatStats()
        {
            string F(ClusterEvent.CurrentInternalStats stats)
            {
                return
                    $"CurrentClusterStats({stats.GossipStats.ReceivedGossipCount}, {stats.GossipStats.MergeCount}, " +
                    $"{stats.GossipStats.SameCount}, {stats.GossipStats.NewerCount}, {stats.GossipStats.OlderCount}," +
                    $"{stats.SeenBy.VersionSize}, {stats.SeenBy.SeenLatest})";
            }

            return string.Join(Environment.NewLine, "ClusterStats(gossip, merge, same, newer, older, vclockSize, seenLatest)"+ 
                                                    Environment.NewLine + 
                                                    _clusterStatsObservedByNode.Select(x => $"{x.Key}\t{F(x.Value)}"));
        }

        public ClusterResultAggregator(string title, int expectedResults, StressSpecConfig.Settings settings)
        {
            _title = title;
            _expectedResults = expectedResults;
            _settings = settings;

            Receive<PhiResult>(phi =>
            {
                _phiValuesObservedByNode = _phiValuesObservedByNode.SetItem(phi.Address, phi.PhiValues);
            });

            Receive<StatsResult>(stats =>
            {
                _clusterStatsObservedByNode = _clusterStatsObservedByNode.SetItem(stats.Address, stats.Stats);
            });

            Receive<ReportTick>(_ =>
            {
                if (_settings.Infolog)
                {
                    _log.Info("[{0}] in progress" + Environment.NewLine + "{1}" + Environment.NewLine + "{2}", _title,
                        FormatPhi(), FormatStats());
                }
            });

            Receive<ClusterResult>(r =>
            {
                _results = _results.Add(r);
                if (_results.Count == _expectedResults)
                {
                    var aggregated = new AggregatedClusterResult(_title, MaxDuration, TotalGossipStats);
                    if (_settings.Infolog)
                    {
                        _log.Info("[{0}] completed in [{1}] ms" + Environment.NewLine + "{2}" +
                                  Environment.NewLine + "{3}" + Environment.NewLine + "{4}", _title, aggregated.Duration.TotalMilliseconds,
                        aggregated.ClusterStats, FormatPhi(), FormatStats());
                    }
                    _reportTo.OnSuccess(r => r.Tell(aggregated));
                    Context.Stop(Self);
                }
            });

            Receive<ClusterEvent.CurrentClusterState>(_ => { });
            Receive<ReportTo>(re =>
            {
                _reportTo = re.Ref;
            });
        }
    }

    internal class ClusterResultHistory : ReceiveActor
    {
        private ILoggingAdapter _log = Context.GetLogger();
        private ImmutableList<AggregatedClusterResult> _history = ImmutableList<AggregatedClusterResult>.Empty;

        public ClusterResultHistory()
        {
            Receive<AggregatedClusterResult>(result =>
            {
                _history = _history.Add(result);
            });
        }

        public static readonly string FormatHistoryHeader = "[Title]\t[Duration (ms)]\t[GossipStats(gossip, merge, same, newer, older)]";

        public string FormatHistoryLine(AggregatedClusterResult result)
        {
            return $"{result.Title}\t{result.Duration.TotalMilliseconds}\t{result.ClusterStats}";
        }

        public string FormatHistory => FormatHistoryHeader + Environment.NewLine +
                                       string.Join(Environment.NewLine, _history.Select(x => FormatHistoryLine(x)));
    }

    /// <summary>
    /// Collect phi values of the failure detector and report to the central <see cref="ClusterResultAggregator"/>
    /// </summary>
    internal class PhiObserver : ReceiveActor
    {
        private readonly Cluster _cluster = Cluster.Get(Context.System);
        private readonly ILoggingAdapter _log = Context.GetLogger();
        private ImmutableDictionary<Address, PhiValue> _phiByNode = ImmutableDictionary<Address, PhiValue>.Empty;

        private Option<IActorRef> _reportTo = Option<IActorRef>.None;
        private HashSet<Address> _nodes = new HashSet<Address>();

        private ICancelable _checkPhiTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1), Context.Self, PhiTick.Instance, ActorRefs.NoSender);

        private double Phi(Address address)
        {
            return _cluster.FailureDetector switch
            {
                DefaultFailureDetectorRegistry<Address> reg => (reg.GetFailureDetector(address)) switch
                {
                    PhiAccrualFailureDetector fd => fd.CurrentPhi,
                    _ => 0.0d
                },
                _ => 0.0d
            };
        }

        private PhiValue PhiByNodeDefault(Address address)
        {
            if (!_phiByNode.ContainsKey(address))
            {
                // populate default value
                _phiByNode = _phiByNode.Add(address, new PhiValue(address, 0, 0, 0.0d));
            }

            return _phiByNode[address];
        }

        public PhiObserver()
        {
            Receive<PhiTick>(_ =>
            {
                foreach (var node in _nodes)
                {
                    var previous = PhiByNodeDefault(node);
                    var p = Phi(node);
                    if (p > 0 || _cluster.FailureDetector.IsMonitoring(node))
                    {
                        var aboveOne = !double.IsInfinity(p) && p > 1.0d ? 1 : 0;
                        _phiByNode = _phiByNode.SetItem(node, new PhiValue(node,
                            previous.CountAboveOne + aboveOne,
                            previous.Count + 1,
                            Math.Max(previous.Max, p)));
                    }
                }

                var phiSet = _phiByNode.Values.ToImmutableSortedSet();
                _reportTo.OnSuccess(r => r.Tell(new PhiResult(_cluster.SelfAddress, phiSet)));
            });

            Receive<ClusterEvent.CurrentClusterState>(state =>
            {
                _nodes = new HashSet<Address>(state.Members.Select(x => x.Address));
            });

            Receive<ClusterEvent.IMemberEvent>(m =>
            {
                _nodes.Add(m.Member.Address);
            });

            Receive<ReportTo>(r =>
            {
                _reportTo.OnSuccess(o => Context.Unwatch(o));
                _reportTo = r.Ref;
                _reportTo.OnSuccess(n => Context.Watch(n));
            });

            Receive<Terminated>(t =>
            {
                if (_reportTo.HasValue)
                    _reportTo = Option<IActorRef>.None;
            });

            Receive<Reset>(_ =>
            {
                _phiByNode = ImmutableDictionary<Address, PhiValue>.Empty;
                _nodes.Clear();
                _cluster.Unsubscribe(Self);
                _cluster.Subscribe(Self, typeof(ClusterEvent.IMemberEvent));
            });
        }

        protected override void PreStart()
        {
            _cluster.Subscribe(Self, typeof(ClusterEvent.IMemberEvent));
        }

        protected override void PostStop()
        {
            _cluster.Unsubscribe(Self);
            _checkPhiTask.Cancel();
            base.PostStop();
        }

        public ITimerScheduler Timers { get; set; }
    }

    internal readonly struct PhiValue : IComparable<PhiValue>
    {
        public PhiValue(Address address, int countAboveOne, int count, double max)
        {
            Address = address;
            CountAboveOne = countAboveOne;
            Count = count;
            Max = max;
        }

        public Address Address { get; }
        public int CountAboveOne { get; }
        public int Count { get; }
        public double Max { get; }

        public int CompareTo(PhiValue other)
        {
            return Member.AddressOrdering.Compare(Address, other.Address);
        }
    }

    internal readonly struct PhiResult
    {
        public PhiResult(Address address, ImmutableSortedSet<PhiValue> phiValues)
        {
            Address = address;
            PhiValues = phiValues;
        }

        public Address Address { get; }

        public ImmutableSortedSet<PhiValue> PhiValues { get; }
    }

    internal class StatsObserver : ReceiveActor
    {
        private readonly Cluster _cluster = Cluster.Get(Context.System);
        private Option<IActorRef> _reportTo = Option<IActorRef>.None;
        private Option<GossipStats> _startStats = Option<GossipStats>.None;

        protected override void PreStart()
        {
            _cluster.Subscribe(Self, typeof(ClusterEvent.CurrentInternalStats));
        }

        protected override void PostStop()
        {
            _cluster.Unsubscribe(Self);
        }

        public StatsObserver()
        {
            Receive<ClusterEvent.CurrentInternalStats>(stats =>
            {
                var gossipStats = stats.GossipStats;
                var vclockStats = stats.SeenBy;

                GossipStats MatchStats()
                {
                    if (!_startStats.HasValue)
                    {
                        _startStats = gossipStats;
                        return gossipStats;
                    }

                    return _startStats.Value - gossipStats;
                }

                var diff = MatchStats();
                var res = new StatsResult(_cluster.SelfAddress, new ClusterEvent.CurrentInternalStats(diff, vclockStats));
                _reportTo.OnSuccess(a => a.Tell(res));
            });

            Receive<ReportTo>(r =>
            {
                _reportTo.OnSuccess(o => Context.Unwatch(o));
                _reportTo = r.Ref;
                _reportTo.OnSuccess(n => Context.Watch(n));
            });

            Receive<Terminated>(t =>
            {
                if(_reportTo.HasValue)
                    _reportTo = Option<IActorRef>.None;
            });

            Receive<Reset>(_ =>
            {
                _startStats = Option<GossipStats>.None;
            });

            // nothing interesting here
            Receive<ClusterEvent.CurrentClusterState>(_ => { });
        }
    }

    /// <summary>
    /// Used for remote death watch testing
    /// </summary>
    internal class Watchee : ActorBase
    {
        protected override bool Receive(object message)
        {
            return true;
        }
    }

    internal sealed class Begin
    {
        public static readonly Begin Instance = new Begin();
        private Begin(){}
    }

    internal sealed class End
    {
        public static readonly End Instance = new End();
        private End(){}
    }

    internal sealed class RetryTick
    {
        public static readonly RetryTick Instance = new RetryTick();
        private RetryTick(){}
    }

    internal sealed class ReportTick
    {
        public static readonly ReportTick Instance = new ReportTick();
        private ReportTick(){}
    }

    internal sealed class PhiTick
    {
        public static readonly PhiTick Instance = new PhiTick();
        private PhiTick(){}
    }

    internal sealed class ReportTo
    {
        public ReportTo(Option<IActorRef> @ref)
        {
            Ref = @ref;
        }

        public Option<IActorRef> Ref { get; }
    }

    internal sealed class StatsResult
    {
        public StatsResult(Address address, ClusterEvent.CurrentInternalStats stats)
        {
            Address = address;
            Stats = stats;
        }

        public Address Address { get; }

        public Akka.Cluster.ClusterEvent.CurrentInternalStats Stats { get; }
    }

    internal sealed class Reset
    {
        public static readonly Reset Instance = new Reset();
        private Reset(){}
    }

    internal class MeasureDurationUntilDown : ReceiveActor
    {
        private readonly Cluster _cluster = Cluster.Get(Context.System);
        private readonly long _startTime;
        private readonly ILoggingAdapter _log = Context.GetLogger();
        public MeasureDurationUntilDown()
        {
            _startTime = MonotonicClock.GetTicks();

            Receive<ClusterEvent.MemberDowned>(d =>
            {
                var m = d.Member;
                if (m.UniqueAddress == _cluster.SelfUniqueAddress)
                {
                    _log.Info("Downed [{0}] after [{1} ms]", _cluster.SelfAddress, TimeSpan.FromTicks(MonotonicClock.GetTicks() - _startTime).TotalMilliseconds);
                }
            });

            Receive<ClusterEvent.CurrentClusterState>(_ => { });
        }

        protected override void PreStart()
        {
            _cluster.Subscribe(Self, ClusterEvent.SubscriptionInitialStateMode.InitialStateAsSnapshot, typeof(ClusterEvent.MemberDowned));
        }
    }

    class StressSpec
    {
    }
}
