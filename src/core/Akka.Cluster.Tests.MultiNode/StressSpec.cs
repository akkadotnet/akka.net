//-----------------------------------------------------------------------
// <copyright file="StressSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Akka.TestKit;
using Akka.TestKit.Internal;
using Akka.TestKit.Internal.StringMatcher;
using Akka.TestKit.TestEvent;
using Akka.Util;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using Environment = System.Environment;

namespace Akka.Cluster.Tests.MultiNode
{
    public class StressSpecConfig : MultiNodeConfig
    {
        public int TotalNumberOfNodes => Environment.GetEnvironmentVariable("MNTR_STRESSSPEC_NODECOUNT") switch
        {
            string e when string.IsNullOrEmpty(e) => 13,
            string val => int.Parse(val),
            _ => 13
        };

        public StressSpecConfig()
        {
            foreach (var i in Enumerable.Range(1, TotalNumberOfNodes))
                Role("node-" + i);

            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.test.cluster-stress-spec {
      infolog = on
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
      failure-detector.acceptable-heartbeat-pause = 3s
      downing-provider-class = ""Akka.Cluster.SplitBrainResolver, Akka.Cluster""
      split-brain-resolver {
          active-strategy = keep-majority #TODO: remove this once it's been made default
          stable-after = 10s
      }
      publish-stats-interval = 1s
    }
    akka.loggers = [""Akka.TestKit.TestEventListener, Akka.TestKit""]
            akka.loglevel = INFO
            akka.remote.log-remote-lifecycle-events = off
            akka.actor.default-dispatcher = {
                executor = channel-executor
              fork-join-executor {
                parallelism-min = 2
                parallelism-factor = 1
                parallelism-max = 64
              }
            }
            akka.actor.internal-dispatcher = {
              executor = channel-executor
              fork-join-executor {
                parallelism-min = 2
                parallelism-factor = 1
                parallelism-max = 64
              }
            }
akka.remote.default-remote-dispatcher {
	  executor = channel-executor
      fork-join-executor {
        parallelism-min = 2
        parallelism-factor = 0.5
        parallelism-max = 16
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
                    $"CurrentClusterStats({stats.GossipStats?.ReceivedGossipCount}, {stats.GossipStats?.MergeCount}, " +
                    $"{stats.GossipStats?.SameCount}, {stats.GossipStats?.NewerCount}, {stats.GossipStats?.OlderCount}," +
                    $"{stats.SeenBy?.VersionSize}, {stats.SeenBy?.SeenLatest})";
            }

            return string.Join(Environment.NewLine, "ClusterStats(gossip, merge, same, newer, older, vclockSize, seenLatest)" +
                                                    Environment.NewLine +
                                                    string.Join(Environment.NewLine, _clusterStatsObservedByNode.Select(x => $"{x.Key}\t{F(x.Value)}")));
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
                    _log.Info("BEGIN CLUSTER OPERATION: [{0}] in progress" + Environment.NewLine + "{1}" + Environment.NewLine + "{2}", _title,
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
                        _log.Info("END CLUSTER OPERATION: [{0}] completed in [{1}] ms" + Environment.NewLine + "{2}" +
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

    /// <summary>
    /// Keeps cluster statistics and metrics reported by <see cref="ClusterResultAggregator"/>.
    ///
    /// Logs the list of historical results when a new <see cref="AggregatedClusterResult"/> is received.
    /// </summary>
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
                        if (double.IsInfinity(p))
                        {
                            _log.Warning("Detected phi value of infinity for [{0}] - ", node);
                            var (history, time) = _cluster.FailureDetector.GetFailureDetector(node) switch
                            {
                                PhiAccrualFailureDetector fd => (fd.state.History, fd.state.TimeStamp),
                                _ => (HeartbeatHistory.Apply(1), null)
                            };
                            _log.Warning("PhiValues: (Timestamp={0}, Mean={1}, Variance={2}, StdDeviation={3}, Intervals=[{4}])",time, 
                                history.Mean, history.Variance, history.StdDeviation,
                                string.Join(",", history.Intervals));
                        }

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

                    return gossipStats -_startStats.Value;
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
                if (_reportTo.HasValue)
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
        private Begin() { }
    }

    internal sealed class End
    {
        public static readonly End Instance = new End();
        private End() { }
    }

    internal sealed class RetryTick
    {
        public static readonly RetryTick Instance = new RetryTick();
        private RetryTick() { }
    }

    internal sealed class ReportTick
    {
        public static readonly ReportTick Instance = new ReportTick();
        private ReportTick() { }
    }

    internal sealed class PhiTick
    {
        public static readonly PhiTick Instance = new PhiTick();
        private PhiTick() { }
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
        private Reset() { }
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

    public class StressSpec : MultiNodeClusterSpec
    {
        public StressSpecConfig.Settings Settings { get; }
        public TestProbe IdentifyProbe;

        protected override TimeSpan ShutdownTimeout => Dilated(TimeSpan.FromSeconds(30));

        public int Step = 0;
        public int NbrUsedRoles = 0;

        public override void MuteLog(ActorSystem sys = null)
        {
            sys ??= Sys;
            base.MuteLog(sys);
            Sys.EventStream.Publish(new Mute(new ErrorFilter(typeof(ApplicationException), new ContainsString("Simulated exception"))));
            MuteDeadLetters(sys, typeof(AggregatedClusterResult), typeof(StatsResult), typeof(PhiResult), typeof(RetryTick));
        }

        public StressSpec() : this(new StressSpecConfig()){ }

        protected StressSpec(StressSpecConfig config) : base(config, typeof(StressSpec))
        {
            Settings = new StressSpecConfig.Settings(Sys.Settings.Config, config.TotalNumberOfNodes);
            ClusterResultHistory = new Lazy<IActorRef>(() =>
            {
                if (Settings.Infolog)
                    return Sys.ActorOf(Props.Create(() => new ClusterResultHistory()), "resultHistory");
                return Sys.DeadLetters;
            });

            PhiObserver = new Lazy<IActorRef>(() =>
            {
                return Sys.ActorOf(Props.Create(() => new PhiObserver()), "phiObserver");
            });

            StatsObserver = new Lazy<IActorRef>(() =>
            {
                return Sys.ActorOf(Props.Create(() => new StatsObserver()), "statsObserver");
            });
        }

        protected override void AtStartup()
        {
            IdentifyProbe = CreateTestProbe();
            base.AtStartup();
        }

        public string ClrInfo()
        {
            var sb = new StringBuilder();
            sb.Append("Operating System: ")
                .Append(Environment.OSVersion.Platform)
                .Append(", ")
                .Append(RuntimeInformation.ProcessArchitecture.ToString())
                .Append(", ")
                .Append(Environment.OSVersion.VersionString)
                .AppendLine();

            sb.Append("CLR: ")
                .Append(RuntimeInformation.FrameworkDescription)
                .AppendLine();

            sb.Append("Processors: ").Append(Environment.ProcessorCount)
                .AppendLine()
                .Append("Load average: ").Append("can't be easily measured on .NET Core") // TODO: fix
                .AppendLine()
                .Append("Thread count: ")
                .Append(Process.GetCurrentProcess().Threads.Count)
                .AppendLine();

            sb.Append("Memory: ")
                .Append(" (")
                .Append(Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024)
                .Append(" - ")
                .Append(Process.GetCurrentProcess().PeakWorkingSet64 / 1024 / 1024)
                .Append(") MB [working set / peak working set]");

            sb.AppendLine("Args: ").Append(string.Join(Environment.NewLine, Environment.GetCommandLineArgs()))
                .AppendLine();

            return sb.ToString();
        }

        public ImmutableList<RoleName> SeedNodes => Roles.Take(Settings.NumberOfSeedNodes).ToImmutableList();

        internal GossipStats LatestGossipStats => Cluster.ReadView.LatestStats.GossipStats;

        public Lazy<IActorRef> ClusterResultHistory { get; }

        public Lazy<IActorRef> PhiObserver { get; }

        public Lazy<IActorRef> StatsObserver { get; }

        public Option<IActorRef> ClusterResultAggregator()
        {
            Sys.ActorSelection(new RootActorPath(GetAddress(Roles.First())) / "user" / ("result" + Step))
                .Tell(new Identify(Step), IdentifyProbe.Ref);
            return new Option<IActorRef>(IdentifyProbe.ExpectMsg<ActorIdentity>().Subject);
        }

        public void CreateResultAggregator(string title, int expectedResults, bool includeInHistory)
        {
            RunOn(() =>
                {
                    var aggregator = Sys.ActorOf(
                        Props.Create(() => new ClusterResultAggregator(title, expectedResults, Settings))
                            .WithDeploy(Deploy.Local), "result" + Step);

                    if (includeInHistory && Settings.Infolog)
                    {
                        aggregator.Tell(new ReportTo(new Option<IActorRef>(ClusterResultHistory.Value)));
                    }
                    else
                    {
                        aggregator.Tell(new ReportTo(Option<IActorRef>.None));
                    }
                },
                Roles.First());
            EnterBarrier("result-aggregator-created-" + Step);

            RunOn(() =>
            {
                var resultAggregator = ClusterResultAggregator();
                PhiObserver.Value.Tell(new ReportTo(resultAggregator));
                StatsObserver.Value.Tell(Reset.Instance);
                StatsObserver.Value.Tell(new ReportTo(resultAggregator));
            }, Roles.Take(NbrUsedRoles).ToArray());

        }

        public void AwaitClusterResult()
        {
            RunOn(() =>
            {
                ClusterResultAggregator().OnSuccess(r =>
                {
                    Watch(r);
                    ExpectMsg<Terminated>(t => t.ActorRef.Path == r.Path);
                });
            }, Roles.First());
            EnterBarrier("cluster-result-done-" + Step);
        }

        public void JoinOneByOne(int numberOfNodes)
        {
            foreach (var i in Enumerable.Range(0, numberOfNodes))
            {
                JoinOne();
                NbrUsedRoles += 1;
                Step += 1;
            }
        }

        public TimeSpan ConvergenceWithin(TimeSpan baseDuration, int nodes)
        {
            return TimeSpan.FromMilliseconds(baseDuration.TotalMilliseconds * Settings.ConvergenceWithinFactor * nodes);
        }

        public void JoinOne()
        {
            Within(TimeSpan.FromSeconds(5) + ConvergenceWithin(TimeSpan.FromSeconds(2), NbrUsedRoles + 1), () =>
            {
                var currentRoles = Roles.Take(NbrUsedRoles + 1).ToArray();
                var title = $"join one to {NbrUsedRoles} nodes cluster";
                CreateResultAggregator(title, expectedResults: currentRoles.Length, includeInHistory: true);
                RunOn(() =>
                {
                    ReportResult(() =>
                    {
                        RunOn(() =>
                        {
                            Cluster.Join(GetAddress(Roles.First()));
                        }, currentRoles.Last());
                        AwaitMembersUp(currentRoles.Length, timeout: RemainingOrDefault);
                        return true;
                    });
                }, currentRoles);
                AwaitClusterResult();
                EnterBarrier("join-one-" + Step);
            });
        }

        public void JoinSeveral(int numberOfNodes, bool toSeedNodes)
        {
            string FormatSeedJoin()
            {
                return toSeedNodes ? "seed nodes" : "one node";
            }

            Within(TimeSpan.FromSeconds(10) + ConvergenceWithin(TimeSpan.FromSeconds(3), NbrUsedRoles + numberOfNodes),
                () =>
                {
                    var currentRoles = Roles.Take(NbrUsedRoles + numberOfNodes).ToArray();
                    var joiningRoles = currentRoles.Skip(NbrUsedRoles).ToArray();
                    var title = $"join {numberOfNodes} to {FormatSeedJoin()}, in {NbrUsedRoles} nodes cluster";
                    CreateResultAggregator(title, expectedResults: currentRoles.Length, true);
                    RunOn(() =>
                    {
                        ReportResult<bool>(() =>
                        {
                            RunOn(() =>
                            {
                                if (toSeedNodes)
                                {
                                    Cluster.JoinSeedNodes(SeedNodes.Select(x => GetAddress(x)));
                                }
                                else
                                {
                                    Cluster.Join(GetAddress(Roles.First()));
                                }
                            }, joiningRoles);
                            AwaitMembersUp(currentRoles.Length, timeout: RemainingOrDefault);
                            return true;
                        });
                    }, currentRoles);
                    AwaitClusterResult();
                    EnterBarrier("join-several-" + Step);
                });
        }

        public void RemoveOneByOne(int numberOfNodes, bool shutdown)
        {
            foreach (var i in Enumerable.Range(0, numberOfNodes))
            {
                RemoveOne(shutdown);
                NbrUsedRoles -= 1;
                Step += 1;
            }
        }

        public void RemoveOne(bool shutdown)
        {
            string FormatNodeLeave()
            {
                return shutdown ? "shutdown" : "remove";
            }

            Within(TimeSpan.FromSeconds(25) + ConvergenceWithin(TimeSpan.FromSeconds(3), NbrUsedRoles - 1), ()
                =>
            {
                var currentRoles = Roles.Take(NbrUsedRoles - 1).ToArray();
                var title = $"{FormatNodeLeave()} one from {NbrUsedRoles} nodes cluster";
                CreateResultAggregator(title, expectedResults:currentRoles.Length, true);
               
                var removeRole = Roles[NbrUsedRoles - 1];
                var removeAddress = GetAddress(removeRole);
                Console.WriteLine($"Preparing to {FormatNodeLeave()}[{removeAddress}] role [{removeRole.Name}] out of [{Roles.Count}]");
                RunOn(() =>
                {
                    var watchee = Sys.ActorOf(Props.Create(() => new Watchee()), "watchee");
                    Console.WriteLine("Created watchee [{0}]", watchee);
                }, removeRole);

                EnterBarrier("watchee-created-" + Step);

                RunOn(() =>
                {
                    AwaitAssert(() =>
                    {
                        Sys.ActorSelection(new RootActorPath(removeAddress) / "user" / "watchee").Tell(new Identify("watchee"), IdentifyProbe.Ref);
                        var watchee = IdentifyProbe.ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(1)).Subject;
                        Watch(watchee);
                    }, interval:TimeSpan.FromSeconds(1.25d));
                   
                }, Roles.First());
                EnterBarrier("watchee-established-" + Step);

                RunOn(() =>
                {
                    if (!shutdown)
                        Cluster.Leave(GetAddress(Myself));
                }, removeRole);

                RunOn(() =>
                {
                    ReportResult(() =>
                    {
                        RunOn(() =>
                        {
                            if (shutdown)
                            {
                                if (Settings.Infolog)
                                {
                                    Log.Info("Shutting down [{0}]", removeAddress);
                                }

                                TestConductor.Exit(removeRole, 0).Wait();
                            }
                        }, Roles.First());

                        AwaitMembersUp(currentRoles.Length, timeout: RemainingOrDefault);
                        AwaitAllReachable();
                        return true;
                    });
                }, currentRoles);

                RunOn(() =>
                {
                    var expectedPath = new RootActorPath(removeAddress) / "user" / "watchee";
                    ExpectMsg<Terminated>(t => t.ActorRef.Path == expectedPath);
                }, Roles.First());

                EnterBarrier("watch-verified-" + Step);

                AwaitClusterResult();
                EnterBarrier("remove-one-" + Step);
            });
        }

        public void RemoveSeveral(int numberOfNodes, bool shutdown)
        {
            string FormatNodeLeave()
            {
                return shutdown ? "shutdown" : "remove";
            }

            Within(TimeSpan.FromSeconds(25) + ConvergenceWithin(TimeSpan.FromSeconds(5), NbrUsedRoles - numberOfNodes),
                () =>
                {
                    var currentRoles = Roles.Take(NbrUsedRoles - numberOfNodes).ToArray();
                    var removeRoles = Roles.Skip(currentRoles.Length).Take(numberOfNodes).ToArray();
                    var title = $"{FormatNodeLeave()} {numberOfNodes} in {NbrUsedRoles} nodes cluster";
                    CreateResultAggregator(title, expectedResults: currentRoles.Length, includeInHistory: true);

                    RunOn(() =>
                    {
                        if (!shutdown)
                        {
                            Cluster.Leave(GetAddress(Myself));
                        }
                    }, removeRoles);

                    RunOn(() =>
                    {
                        ReportResult<bool>(() =>
                        {
                            RunOn(() =>
                            {
                                if (shutdown)
                                {
                                    foreach (var role in removeRoles)
                                    {
                                        if (Settings.Infolog)
                                            Log.Info("Shutting down [{0}]", GetAddress(role));
                                        TestConductor.Exit(role, 0).Wait(RemainingOrDefault);
                                    }
                                }
                            }, Roles.First());
                            AwaitMembersUp(currentRoles.Length, timeout: RemainingOrDefault);
                            AwaitAllReachable();
                            return true;
                        });
                    }, currentRoles);

                    AwaitClusterResult();
                    EnterBarrier("remove-several-" + Step);
                });
        }

        public void PartitionSeveral(int numberOfNodes)
        {
            Within(TimeSpan.FromSeconds(25) + ConvergenceWithin(TimeSpan.FromSeconds(5), NbrUsedRoles - numberOfNodes),
                () =>
                {
                    var currentRoles = Roles.Take(NbrUsedRoles - numberOfNodes).ToArray();
                    var removeRoles = Roles.Skip(currentRoles.Length).Take(numberOfNodes).ToArray();
                    var title = $"partition {numberOfNodes} in {NbrUsedRoles} nodes cluster";
                    Console.WriteLine(title);
                    Console.WriteLine("[{0}] are blackholing [{1}]", string.Join(",", currentRoles.Select(x => x.ToString())), string.Join(",", removeRoles.Select(x => x.ToString())));
                    CreateResultAggregator(title, expectedResults: currentRoles.Length, includeInHistory: true);

                    RunOn(() =>
                    {
                        foreach (var x in currentRoles)
                        {
                            foreach (var y in removeRoles)
                            {
                                TestConductor.Blackhole(x, y, ThrottleTransportAdapter.Direction.Both).Wait();
                            }
                        }
                    }, Roles.First());
                    EnterBarrier("partition-several-blackhole");

                    RunOn(() =>
                    {
                        ReportResult<bool>(() =>
                        {
                            var startTime = MonotonicClock.GetTicks();
                            AwaitMembersUp(currentRoles.Length, timeout:RemainingOrDefault);
                            Sys.Log.Info("Removed [{0}] members after [{0} ms]",
                                removeRoles.Length, TimeSpan.FromTicks(MonotonicClock.GetTicks() - startTime).TotalMilliseconds);
                            AwaitAllReachable();
                            return true;
                        });
                    }, currentRoles);

                    RunOn(() =>
                    {
                        Sys.ActorOf(Props.Create<MeasureDurationUntilDown>());
                        AwaitAssert(() =>
                        {
                            Cluster.IsTerminated.Should().BeTrue();
                        });
                    }, removeRoles);
                    AwaitClusterResult();
                    EnterBarrier("partition-several-" + Step);
                });
        }

        public T ReportResult<T>(Func<T> thunk)
        {
            var startTime = MonotonicClock.GetTicks();
            var startStats = ClusterView.LatestStats.GossipStats;

            var returnValue = thunk();

            ClusterResultAggregator().OnSuccess(r =>
            {
                r.Tell(new ClusterResult(Cluster.SelfAddress, TimeSpan.FromTicks(MonotonicClock.GetTicks() - startTime), LatestGossipStats - startStats));
            });

            return returnValue;
        }

        public void ExerciseJoinRemove(string title, TimeSpan duration)
        {
            var activeRoles = Roles.Take(Settings.NumberOfNodesJoinRemove).ToArray();
            var loopDuration = TimeSpan.FromSeconds(10) +
                               ConvergenceWithin(TimeSpan.FromSeconds(4), NbrUsedRoles + activeRoles.Length);
            var rounds = (int)Math.Max(1.0d, (duration - loopDuration).TotalMilliseconds / loopDuration.TotalMilliseconds);
            var usedRoles = Roles.Take(NbrUsedRoles).ToArray();
            var usedAddresses = usedRoles.Select(x => GetAddress(x)).ToImmutableHashSet();

            Option<ActorSystem> Loop(int counter, Option<ActorSystem> previousAs,
                ImmutableHashSet<Address> allPreviousAddresses)
            {
                if (counter > rounds) return previousAs;

                var t = title + " round " + counter;
                RunOn(() =>
                {
                    PhiObserver.Value.Tell(Reset.Instance);
                    StatsObserver.Value.Tell(Reset.Instance);
                }, usedRoles);
                CreateResultAggregator(t, expectedResults:NbrUsedRoles, includeInHistory:true);

                var nextAs = Option<ActorSystem>.None;
                var nextAddresses = ImmutableHashSet<Address>.Empty;
                Within(loopDuration, () =>
                {
                   var (nextAsy, nextAddr) = ReportResult(() =>
                    {
                        Option<ActorSystem> nextAs;

                        if (activeRoles.Contains(Myself))
                        {
                            previousAs.OnSuccess(s =>
                            {
                                Shutdown(s);
                            });

                            var sys = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
                            MuteLog(sys);
                            Akka.Cluster.Cluster.Get(sys).JoinSeedNodes(SeedNodes.Select(x => GetAddress(x)));
                            nextAs = new Option<ActorSystem>(sys);
                        }
                        else
                        {
                            nextAs = previousAs;
                        }

                        RunOn(() =>
                        {
                            AwaitMembersUp(NbrUsedRoles + activeRoles.Length, 
                                canNotBePartOfMemberRing: allPreviousAddresses,
                                timeout: RemainingOrDefault);
                            AwaitAllReachable();
                        }, usedRoles);

                        nextAddresses = ClusterView.Members.Select(x => x.Address).ToImmutableHashSet()
                            .Except(usedAddresses);

                        RunOn(() =>
                        {
                            nextAddresses.Count.Should().Be(Settings.NumberOfNodesJoinRemove);
                        }, usedRoles);

                        return (nextAs, nextAddresses);
                    });

                   nextAs = nextAsy;
                   nextAddresses = nextAddr;
                });

                AwaitClusterResult();
                Step += 1;
                return Loop(counter + 1, nextAs, nextAddresses);
            }

            Loop(1, Option<ActorSystem>.None, ImmutableHashSet<Address>.Empty).OnSuccess(aSys =>
            {
                Shutdown(aSys);
            });

            Within(loopDuration, () =>
            {
                RunOn(() =>
                {
                    AwaitMembersUp(NbrUsedRoles, timeout: RemainingOrDefault);
                    AwaitAllReachable();
                    PhiObserver.Value.Tell(Reset.Instance);
                    StatsObserver.Value.Tell(Reset.Instance);
                }, usedRoles);
            });
            EnterBarrier("join-remove-shutdown-" + Step);
        }

        public void IdleGossip(string title)
        {
            CreateResultAggregator(title, expectedResults: NbrUsedRoles, includeInHistory: true);
            ReportResult(() =>
            {
                ClusterView.Members.Count.Should().Be(NbrUsedRoles);
                Thread.Sleep(Settings.IdleGossipDuration);
                ClusterView.Members.Count.Should().Be(NbrUsedRoles);
                return true;
            });
            AwaitClusterResult();
        }

        public void IncrementStep()
        {
            Step += 1;
        }

        [MultiNodeFact]
        public void Cluster_under_stress()
        {
            MustLogSettings();
            IncrementStep();
            MustJoinSeedNodes();
            IncrementStep();
            MustJoinSeedNodesOneByOneToSmallCluster();
            IncrementStep();
            MustJoinSeveralNodesToOneNode();
            IncrementStep();
            MustJoinSeveralNodesToSeedNodes();
            IncrementStep();
            MustJoinNodesOneByOneToLargeCluster();
            IncrementStep();
            MustExerciseJoinRemoveJoinRemove();
            IncrementStep();
            MustGossipWhenIdle();
            IncrementStep();
            MustDownPartitionedNodes();
            IncrementStep();
            MustLeaveNodesOneByOneFromLargeCluster();
            IncrementStep();
            MustShutdownNodesOneByOneFromLargeCluster();
            IncrementStep();
            MustLeaveSeveralNodes();
            IncrementStep();
            MustShutdownSeveralNodes();
            IncrementStep();
            MustShutdownNodesOneByOneFromSmallCluster();
            IncrementStep();
            MustLeaveNodesOneByOneFromSmallCluster();
            IncrementStep();
            MustLogClrInfo();
        }

        public void MustLogSettings()
        {
            if (Settings.Infolog)
            {
                Log.Info("StressSpec CLR:" + Environment.NewLine + ClrInfo());
                RunOn(() =>
                {
                    Log.Info("StressSpec settings:" + Environment.NewLine + Settings);
                });
            }
            EnterBarrier("after-" + Step);
        }

        public void MustJoinSeedNodes()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                var otherNodesJoiningSeedNodes = Roles.Skip(Settings.NumberOfSeedNodes)
                    .Take(Settings.NumberOfNodesJoiningToSeedNodesInitially).ToArray();
                var size = SeedNodes.Count + otherNodesJoiningSeedNodes.Length;

                CreateResultAggregator("join seed nodes", expectedResults: size, includeInHistory: true);

                RunOn(() =>
                {
                    ReportResult(() =>
                    {
                        Cluster.JoinSeedNodes(SeedNodes.Select(x => GetAddress(x)));
                        AwaitMembersUp(size, timeout: RemainingOrDefault);
                        return true;
                    });
                }, SeedNodes.AddRange(otherNodesJoiningSeedNodes).ToArray());

                AwaitClusterResult();
                NbrUsedRoles += size;
                EnterBarrier("after-" + Step);
            });
        }

        public void MustJoinSeedNodesOneByOneToSmallCluster()
        {
            JoinOneByOne(Settings.NumberOfNodesJoiningOneByOneSmall);
            EnterBarrier("after-" + Step);
        }

        public void MustJoinSeveralNodesToOneNode()
        {
            JoinSeveral(Settings.NumberOfNodesJoiningToOneNode, false);
            NbrUsedRoles += Settings.NumberOfNodesJoiningToOneNode;
            EnterBarrier("after-" + Step);
        }

        public void MustJoinSeveralNodesToSeedNodes()
        {
            if (Settings.NumberOfNodesJoiningToSeedNodes > 0)
            {
                JoinSeveral(Settings.NumberOfNodesJoiningToSeedNodes, true);
                NbrUsedRoles += Settings.NumberOfNodesJoiningToSeedNodes;
            }
            EnterBarrier("after-" + Step);
        }

        public void MustJoinNodesOneByOneToLargeCluster()
        {
            JoinOneByOne(Settings.NumberOfNodesJoiningOneByOneLarge);
            EnterBarrier("after-" + Step);
        }

        public void MustExerciseJoinRemoveJoinRemove()
        {
            ExerciseJoinRemove("exercise join/remove", Settings.JoinRemoveDuration);
            EnterBarrier("after-" + Step);
        }

        public void MustGossipWhenIdle()
        {
            IdleGossip("idle gossip");
            EnterBarrier("after-" + Step);
        }

        public void MustDownPartitionedNodes()
        {
            PartitionSeveral(Settings.NumberOfNodesPartition);
            NbrUsedRoles -= Settings.NumberOfNodesPartition;
            EnterBarrier("after-" + Step);
        }

        public void MustLeaveNodesOneByOneFromLargeCluster()
        {
            RemoveOneByOne(Settings.NumberOfNodesLeavingOneByOneLarge, shutdown:false);
            EnterBarrier("after-" + Step);
        }

        public void MustShutdownNodesOneByOneFromLargeCluster()
        {
            RemoveOneByOne(Settings.NumberOfNodesShutdownOneByOneLarge, shutdown: true);
            EnterBarrier("after-" + Step);
        }

        public void MustLeaveSeveralNodes()
        {
            RemoveSeveral(Settings.NumberOfNodesLeaving, shutdown: false);
            NbrUsedRoles -= Settings.NumberOfNodesLeaving;
            EnterBarrier("after-" + Step);
        }

        public void MustShutdownSeveralNodes()
        {
            RemoveSeveral(Settings.NumberOfNodesShutdown, shutdown: true);
            NbrUsedRoles -= Settings.NumberOfNodesShutdown;
            EnterBarrier("after-" + Step);
        }

        public void MustShutdownNodesOneByOneFromSmallCluster()
        {
            RemoveOneByOne(Settings.NumberOfNodesShutdownOneByOneSmall, true);
            EnterBarrier("after-" + Step);
        }

        public void MustLeaveNodesOneByOneFromSmallCluster()
        {
            RemoveOneByOne(Settings.NumberOfNodesLeavingOneByOneSmall, false);
            EnterBarrier("after-" + Step);
        }

        public void MustLogClrInfo()
        {
            if (Settings.Infolog)
            {
                Log.Info("StressSpec CLR: " + Environment.NewLine + "{0}", ClrInfo());
            }
            EnterBarrier("after-" + Step);
        }
    }
}
