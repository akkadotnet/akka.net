//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonManager.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Coordination;
using Akka.Event;
using Akka.Pattern;
using Akka.Remote;
using Akka.Util.Internal;
using static Akka.Cluster.ClusterEvent;

namespace Akka.Cluster.Tools.Singleton
{
    /// <summary>
    /// Control messages used for the cluster singleton
    /// </summary>
    public interface IClusterSingletonMessage { }

    /// <summary>
    /// Sent from new oldest to previous oldest to initiate the
    /// hand-over process. <see cref="HandOverInProgress"/>  and <see cref="HandOverDone"/>
    /// are expected replies.
    /// </summary>
    [Serializable]
    internal sealed class HandOverToMe : IClusterSingletonMessage, IDeadLetterSuppression
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static HandOverToMe Instance { get; } = new HandOverToMe();
        private HandOverToMe() { }
    }

    /// <summary>
    /// Confirmation by the previous oldest that the hand
    /// over process, shut down of the singleton actor, has
    /// started.
    /// </summary>
    [Serializable]
    internal sealed class HandOverInProgress : IClusterSingletonMessage
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static HandOverInProgress Instance { get; } = new HandOverInProgress();
        private HandOverInProgress() { }
    }

    /// <summary>
    /// Confirmation by the previous oldest that the singleton
    /// actor has been terminated and the hand-over process is
    /// completed.
    /// </summary>
    [Serializable]
    internal sealed class HandOverDone : IClusterSingletonMessage
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static HandOverDone Instance { get; } = new HandOverDone();
        private HandOverDone() { }
    }

    /// <summary>
    /// TBD
    /// Sent from from previous oldest to new oldest to
    /// initiate the normal hand-over process.
    /// Especially useful when new node joins and becomes
    /// oldest immediately, without knowing who was previous
    /// oldest.
    /// </summary>
    [Serializable]
    internal sealed class TakeOverFromMe : IClusterSingletonMessage, IDeadLetterSuppression
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static TakeOverFromMe Instance { get; } = new TakeOverFromMe();
        private TakeOverFromMe() { }
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class Cleanup
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static Cleanup Instance { get; } = new Cleanup();
        private Cleanup() { }
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class StartOldestChangedBuffer
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static StartOldestChangedBuffer Instance { get; } = new StartOldestChangedBuffer();
        private StartOldestChangedBuffer() { }
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class HandOverRetry
    {
        /// <summary>
        /// TBD
        /// </summary>
        public int Count { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="count">TBD</param>
        public HandOverRetry(int count)
        {
            Count = count;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class TakeOverRetry
    {
        /// <summary>
        /// TBD
        /// </summary>
        public int Count { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="count">TBD</param>
        public TakeOverRetry(int count)
        {
            Count = count;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class LeaseRetry: INoSerializationVerificationNeeded
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static LeaseRetry Instance { get; } = new LeaseRetry();
        private LeaseRetry() { }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public interface IClusterSingletonData { }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class Uninitialized : IClusterSingletonData
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static Uninitialized Instance { get; } = new Uninitialized();
        private Uninitialized() { }
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class YoungerData : IClusterSingletonData
    {
        /// <summary>
        /// TBD
        /// </summary>
        public List<UniqueAddress> Oldest { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="oldest">TBD</param>
        public YoungerData(List<UniqueAddress> oldest)
        {
            Oldest = oldest;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class BecomingOldestData : IClusterSingletonData
    {
        /// <summary>
        /// TBD
        /// </summary>
        public List<UniqueAddress> PreviousOldest { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="previousOldest">TBD</param>
        public BecomingOldestData(List<UniqueAddress> previousOldest)
        {
            PreviousOldest = previousOldest;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class OldestData : IClusterSingletonData
    {
        /// <summary>
        /// TBD
        /// </summary>
        public IActorRef Singleton { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="singleton">TBD</param>
        public OldestData(IActorRef singleton)
        {
            Singleton = singleton;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class WasOldestData : IClusterSingletonData
    {
        /// <summary>
        /// TBD
        /// </summary>
        public IActorRef Singleton { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public UniqueAddress NewOldest { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="singleton">TBD</param>
        /// <param name="newOldest">TBD</param>
        public WasOldestData(IActorRef singleton, UniqueAddress newOldest)
        {
            Singleton = singleton;
            NewOldest = newOldest;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class HandingOverData : IClusterSingletonData
    {

        /// <summary>
        /// TBD
        /// </summary>
        public IActorRef Singleton { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public IActorRef HandOverTo { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="singleton">TBD</param>
        /// <param name="handOverTo">TBD</param>
        public HandingOverData(IActorRef singleton, IActorRef handOverTo)
        {
            Singleton = singleton;
            HandOverTo = handOverTo;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class StoppingData : IClusterSingletonData
    {
        /// <summary>
        /// TBD
        /// </summary>
        public IActorRef Singleton { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="singleton">TBD</param>
        public StoppingData(IActorRef singleton)
        {
            Singleton = singleton;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class EndData : IClusterSingletonData
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static EndData Instance { get; } = new EndData();
        private EndData() { }
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class AcquiringLeaseData : IClusterSingletonData
    {
        /// <summary>
        /// TBD
        /// </summary>
        public bool LeaseRequestInProgress { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public IActorRef Singleton { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="leaseRequestInProgress">TBD</param>
        /// <param name="singleton">TBD</param>
        public AcquiringLeaseData(bool leaseRequestInProgress, IActorRef singleton)
        {
            LeaseRequestInProgress = leaseRequestInProgress;
            Singleton = singleton;
        }
    }


    [Serializable]
    internal sealed class AcquireLeaseResult : IDeadLetterSuppression, INoSerializationVerificationNeeded
    {
        public bool HoldingLease { get; }

        public AcquireLeaseResult(bool holdingLease)
        {
            HoldingLease = holdingLease;
        }
    }

    [Serializable]
    internal sealed class ReleaseLeaseResult : IDeadLetterSuppression, INoSerializationVerificationNeeded
    {
        public bool Released { get; }

        public ReleaseLeaseResult(bool released)
        {
            Released = released;
        }
    }

    [Serializable]
    internal sealed class AcquireLeaseFailure : IDeadLetterSuppression, INoSerializationVerificationNeeded
    {
        public Exception Failure { get; }

        public AcquireLeaseFailure(Exception failure)
        {
            Failure = failure;
        }
    }

    [Serializable]
    internal sealed class ReleaseLeaseFailure : IDeadLetterSuppression, INoSerializationVerificationNeeded
    {
        public Exception Failure { get; }

        public ReleaseLeaseFailure(Exception failure)
        {
            Failure = failure;
        }
    }

    [Serializable]
    internal sealed class LeaseLost : IDeadLetterSuppression, INoSerializationVerificationNeeded
    {
        public Exception Reason { get; }

        public LeaseLost(Exception reason)
        {
            Reason = reason;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class DelayedMemberRemoved
    {
        /// <summary>
        /// TBD
        /// </summary>
        public Member Member { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="member">TBD</param>
        public DelayedMemberRemoved(Member member)
        {
            Member = member;
        }
    }

    /// <summary>
    /// INTERNAL API
    ///
    /// Used for graceful termination as part of <see cref="CoordinatedShutdown"/>.
    /// </summary>
    internal sealed class SelfExiting
    {
        private SelfExiting() { }

        /// <summary>
        /// Singleton instance
        /// </summary>
        public static SelfExiting Instance { get; } = new SelfExiting();
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    public enum ClusterSingletonState
    {
        /// <summary>
        /// TBD
        /// </summary>
        Start,
        /// <summary>
        /// TBD
        /// </summary>
        AcquiringLease,
        /// <summary>
        /// TBD
        /// </summary>
        Oldest,
        /// <summary>
        /// TBD
        /// </summary>
        Younger,
        /// <summary>
        /// TBD
        /// </summary>
        BecomingOldest,
        /// <summary>
        /// TBD
        /// </summary>
        WasOldest,
        /// <summary>
        /// TBD
        /// </summary>
        HandingOver,
        /// <summary>
        /// TBD
        /// </summary>
        TakeOver,
        /// <summary>
        /// TBD
        /// </summary>
        Stopping,
        /// <summary>
        /// TBD
        /// </summary>
        End
    }

    /// <summary>
    /// Thrown when a consistent state can't be determined within the defined retry limits.
    /// Eventually it will reach a stable state and can continue, and that is simplified
    /// by starting over with a clean state. Parent supervisor should typically restart the actor, i.e. default decision.
    /// </summary>
    public sealed class ClusterSingletonManagerIsStuckException : AkkaException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterSingletonManagerIsStuckException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public ClusterSingletonManagerIsStuckException(string message) : base(message) { }

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterSingletonManagerIsStuckException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
        public ClusterSingletonManagerIsStuckException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
#endif
    }

    /// <summary>
    /// <para>
    /// Manages singleton actor instance among all cluster nodes or a group of nodes tagged with a specific role.
    /// At most one singleton instance is running at any point in time.
    /// </para>
    /// <para>
    /// The ClusterSingletonManager is supposed to be started on all nodes, or all nodes with specified role,
    /// in the cluster with <see cref="ActorSystem.ActorOf"/>. The actual singleton is started on the oldest node
    /// by creating a child actor from the supplied `singletonProps`.
    /// </para>
    /// <para>
    /// The singleton actor is always running on the oldest member with specified role. The oldest member is determined
    /// by <see cref="Member.IsOlderThan"/>. This can change when removing members. A graceful hand over can normally
    /// be performed when current oldest node is leaving the cluster. Be aware that there is a short time period when
    /// there is no active singleton during the hand-over process.
    /// </para>
    /// <para>
    /// The cluster failure detector will notice when oldest node becomes unreachable due to things like CLR crash,
    /// hard shut down, or network failure. When the crashed node has been removed (via down) from the cluster then
    /// a new oldest node will take over and a new singleton actor is created.For these failure scenarios there
    /// will not be a graceful hand-over, but more than one active singletons is prevented by all reasonable means.
    /// Some corner cases are eventually resolved by configurable timeouts.
    /// </para>
    /// <para>
    /// You access the singleton actor with <see cref="ClusterSingletonProxy"/>. Alternatively the singleton actor may
    /// broadcast its existence when it is started.
    /// </para>
    /// <para>
    /// Use one of the factory methods <see cref="ClusterSingletonManager.Props(Actor.Props, ClusterSingletonManagerSettings)">ClusterSingletonManager.Props</see> to create the <see cref="Actor.Props"/> for the actor.
    /// </para>
    /// </summary>
    public sealed class ClusterSingletonManager : FSM<ClusterSingletonState, IClusterSingletonData>
    {
        /// <summary>
        /// Returns default HOCON configuration for the cluster singleton.
        /// </summary>
        /// <returns>TBD</returns>
        public static Config DefaultConfig()
        {
            return ConfigurationFactory.FromResource<ClusterSingletonManager>("Akka.Cluster.Tools.Singleton.reference.conf");
        }

        /// <summary>
        /// Creates props for the current cluster singleton manager using <see cref="PoisonPill"/>
        /// as the default termination message.
        /// </summary>
        /// <param name="singletonProps"><see cref="Actor.Props"/> of the singleton actor instance.</param>
        /// <param name="settings">Cluster singleton manager settings.</param>
        /// <returns>TBD</returns>
        public static Props Props(Props singletonProps, ClusterSingletonManagerSettings settings)
        {
            return Props(singletonProps, PoisonPill.Instance, settings);
        }

        /// <summary>
        /// Creates props for the current cluster singleton manager.
        /// </summary>
        /// <param name="singletonProps"><see cref="Actor.Props"/> of the singleton actor instance.</param>
        /// <param name="terminationMessage">
        /// When handing over to a new oldest node this <paramref name="terminationMessage"/> is sent to the singleton actor
        /// to tell it to finish its work, close resources, and stop. The hand-over to the new oldest node
        /// is completed when the singleton actor is terminated. Note that <see cref="PoisonPill"/> is a
        /// perfectly fine <paramref name="terminationMessage"/> if you only need to stop the actor.
        /// </param>
        /// <param name="settings">Cluster singleton manager settings.</param>
        /// <returns>TBD</returns>
        public static Props Props(Props singletonProps, object terminationMessage, ClusterSingletonManagerSettings settings)
        {
            return Actor.Props.Create(() => new ClusterSingletonManager(singletonProps, terminationMessage, settings)).WithDeploy(Deploy.Local);
        }

        private readonly Props _singletonProps;
        private readonly object _terminationMessage;
        private readonly ClusterSingletonManagerSettings _settings;

        private const string HandOverRetryTimer = "hand-over-retry";
        private const string TakeOverRetryTimer = "take-over-retry";
        private const string CleanupTimer = "cleanup";
        private const string LeaseRetryTimer = "lease-retry";

        // Previous GetNext request delivered event and new GetNext is to be sent
        private bool _oldestChangedReceived = true;
        private bool _selfExited;

        // started when when self member is Up
        private IActorRef _oldestChangedBuffer;
        // keep track of previously removed members
        private ImmutableDictionary<UniqueAddress, Deadline> _removed = ImmutableDictionary<UniqueAddress, Deadline>.Empty;
        private readonly TimeSpan _removalMargin;
        private readonly int _maxHandOverRetries;
        private readonly int _maxTakeOverRetries;
        private readonly Cluster _cluster = Cluster.Get(Context.System);
        private readonly UniqueAddress _selfUniqueAddress;
        private ILoggingAdapter _log;

        private readonly CoordinatedShutdown _coordShutdown = CoordinatedShutdown.Get(Context.System);
        private readonly TaskCompletionSource<Done> _memberExitingProgress = new TaskCompletionSource<Done>();

        private readonly string singletonLeaseName;
        private readonly Lease lease;
        private readonly TimeSpan leaseRetryInterval = TimeSpan.FromSeconds(5); // won't be used

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="singletonProps">TBD</param>
        /// <param name="terminationMessage">TBD</param>
        /// <param name="settings">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <exception cref="ConfigurationException">TBD</exception>
        /// <returns>TBD</returns>
        public ClusterSingletonManager(Props singletonProps, object terminationMessage, ClusterSingletonManagerSettings settings)
        {
            var role = settings.Role;
            if (!string.IsNullOrEmpty(role) && !_cluster.SelfRoles.Contains(role))
                throw new ArgumentException($"This cluster member [{_cluster.SelfAddress}] doesn't have the role [{role}]");

            _singletonProps = singletonProps;
            _terminationMessage = terminationMessage;
            _settings = settings;
            singletonLeaseName = $"{Context.System.Name}-singleton-{Self.Path}";

            if (settings.LeaseSettings != null)
            {
                lease = LeaseProvider.Get(Context.System)
                    .GetLease(singletonLeaseName, settings.LeaseSettings.LeaseImplementation, _cluster.SelfAddress.HostPort());
                leaseRetryInterval = settings.LeaseSettings.LeaseRetryInterval;
            }

            _removalMargin = (settings.RemovalMargin <= TimeSpan.Zero) ? _cluster.DowningProvider.DownRemovalMargin : settings.RemovalMargin;

            var n = (int)(_removalMargin.TotalMilliseconds / _settings.HandOverRetryInterval.TotalMilliseconds);

            var minRetries = Context.System.Settings.Config.GetInt("akka.cluster.singleton.min-number-of-hand-over-retries", 0);
            if (minRetries < 1)
                throw new ConfigurationException("min-number-of-hand-over-retries must be >= 1");

            _maxHandOverRetries = Math.Max(minRetries, n + 3);
            _maxTakeOverRetries = Math.Max(1, _maxHandOverRetries - 3);

            _selfUniqueAddress = _cluster.SelfUniqueAddress;
            SetupCoordinatedShutdown();

            InitializeFSM();
        }

        private void SetupCoordinatedShutdown()
        {
            var self = Self;
            _coordShutdown.AddTask(CoordinatedShutdown.PhaseClusterExiting, "wait-singleton-exiting", () =>
            {
                if (_cluster.IsTerminated || _cluster.SelfMember.Status == MemberStatus.Down)
                    return Task.FromResult(Done.Instance);
                else
                    return _memberExitingProgress.Task;
            });
            _coordShutdown.AddTask(CoordinatedShutdown.PhaseClusterExiting, "singleton-exiting-2", () =>
            {
                if (_cluster.IsTerminated || _cluster.SelfMember.Status == MemberStatus.Down)
                {
                    return Task.FromResult(Done.Instance);
                }
                else
                {
                    var timeout = _coordShutdown.Timeout(CoordinatedShutdown.PhaseClusterExiting);
                    return self.Ask(SelfExiting.Instance, timeout).ContinueWith(tr => Done.Instance);
                }
            });
        }

        private ILoggingAdapter Log { get { return _log ?? (_log = Context.GetLogger()); } }

        /// <inheritdoc cref="ActorBase.PreStart"/>
        protected override void PreStart()
        {
            if (_cluster.IsTerminated)
                throw new ActorInitializationException("Cluster node must not be terminated");

            // subscribe to cluster changes, re-subscribe when restart
            _cluster.Subscribe(Self, ClusterEvent.InitialStateAsEvents, typeof(ClusterEvent.MemberRemoved), typeof(ClusterEvent.MemberDowned));

            SetTimer(CleanupTimer, Cleanup.Instance, TimeSpan.FromMinutes(1.0), repeat: true);

            // defer subscription to avoid some jitter when
            // starting/joining several nodes at the same time
            var self = Self;
            _cluster.RegisterOnMemberUp(() => self.Tell(StartOldestChangedBuffer.Instance));
        }

        /// <inheritdoc cref="ActorBase.PostStop"/>
        protected override void PostStop()
        {
            CancelTimer(CleanupTimer);
            _cluster.Unsubscribe(Self);
            _memberExitingProgress.TrySetResult(Done.Instance);
            base.PostStop();
        }

        // HACK: this is to patch issue #4474 (https://github.com/akkadotnet/akka.net/issues/4474), but it doesn't guarantee that it fixes the underlying bug.
        // There is no spec for this fix, no reproduction spec was possible.
        private void AddRemoved(UniqueAddress node)
        {
            if(_removed.TryGetValue(node, out _))
            {
                _removed = _removed.SetItem(node, Deadline.Now + TimeSpan.FromMinutes(15.0));
            } 
            else
            {
                _removed = _removed.Add(node, Deadline.Now + TimeSpan.FromMinutes(15.0));
            }
        }

        private void CleanupOverdueNotMemberAnyMore()
        {
            _removed = _removed.Where(kv => kv.Value.IsOverdue).ToImmutableDictionary();
        }

        private ActorSelection Peer(Address at)
        {
            return Context.ActorSelection(Self.Path.ToStringWithAddress(at));
        }

        private void GetNextOldestChanged()
        {
            if (!_oldestChangedReceived) return;
            _oldestChangedReceived = false;
            _oldestChangedBuffer.Tell(OldestChangedBuffer.GetNext.Instance);
        }

        private State<ClusterSingletonState, IClusterSingletonData> TryAcquireLease()
        {
            var self = Self;
            lease.Acquire(reason =>
            {
                self.Tell(new LeaseLost(reason));
            }).ContinueWith(r =>
            {
                if (r.IsFaulted || r.IsCanceled)
                    return (object)new AcquireLeaseFailure(r.Exception);
                return new AcquireLeaseResult(r.Result);
            }).PipeTo(Self);

            return GoTo(ClusterSingletonState.AcquiringLease).Using(new AcquiringLeaseData(true, null));
        }

        // Try and go to oldest, taking the lease if needed
        private State<ClusterSingletonState, IClusterSingletonData> TryGotoOldest()
        {
            // check if lease
            if (lease == null)
                return GoToOldest();
            else
            {
                Log.Info("Trying to acquire lease before starting singleton");
                return TryAcquireLease();
            }
        }

        private State<ClusterSingletonState, IClusterSingletonData> GoToOldest()
        {
            var singleton = Context.Watch(Context.ActorOf(_singletonProps, _settings.SingletonName));
            Log.Info("Singleton manager started singleton actor [{0}] ", singleton.Path);
            return
                GoTo(ClusterSingletonState.Oldest).Using(new OldestData(singleton));
        }

        private State<ClusterSingletonState, IClusterSingletonData> HandleOldestChanged(IActorRef singleton, UniqueAddress oldest)
        {
            _oldestChangedReceived = true;
            Log.Info("{0} observed OldestChanged: [{1} -> {2}]", StateName, _cluster.SelfAddress, oldest?.Address);
            switch (oldest)
            {
                case UniqueAddress a when a.Equals(_cluster.SelfUniqueAddress):
                    // already oldest
                    return Stay();
                case UniqueAddress a when !_selfExited && _removed.ContainsKey(a):
                    // The member removal was not completed and the old removed node is considered
                    // oldest again. Safest is to terminate the singleton instance and goto Younger.
                    // This node will become oldest again when the other is removed again.
                    return GoToHandingOver(singleton, null);
                case UniqueAddress a:
                    // send TakeOver request in case the new oldest doesn't know previous oldest
                    Peer(a.Address).Tell(TakeOverFromMe.Instance);
                    SetTimer(TakeOverRetryTimer, new TakeOverRetry(1), _settings.HandOverRetryInterval, repeat: false);
                    return GoTo(ClusterSingletonState.WasOldest)
                        .Using(new WasOldestData(singleton, a));
                case null:
                    // new oldest will initiate the hand-over
                    SetTimer(TakeOverRetryTimer, new TakeOverRetry(1), _settings.HandOverRetryInterval, repeat: false);
                    return GoTo(ClusterSingletonState.WasOldest)
                        .Using(new WasOldestData(singleton, newOldest: null));
            }
        }

        private State<ClusterSingletonState, IClusterSingletonData> HandleHandOverDone(IActorRef handOverTo)
        {
            var newOldest = handOverTo?.Path.Address;
            Log.Info("Singleton terminated, hand-over done [{0} -> {1}]", _cluster.SelfAddress, newOldest);
            handOverTo?.Tell(HandOverDone.Instance);
            _memberExitingProgress.TrySetResult(Done.Instance);
            if (_removed.ContainsKey(_cluster.SelfUniqueAddress))
            {
                Log.Info("Self removed, stopping ClusterSingletonManager");
                return Stop();
            }
            else if (handOverTo == null)
            {
                return GoTo(ClusterSingletonState.Younger).Using(new YoungerData(null));
            }
            else
            {
                return GoTo(ClusterSingletonState.End).Using(EndData.Instance);
            }
        }

        private State<ClusterSingletonState, IClusterSingletonData> GoToHandingOver(IActorRef singleton, IActorRef handOverTo)
        {
            if (singleton == null)
            {
                return HandleHandOverDone(handOverTo);
            }

            handOverTo?.Tell(HandOverInProgress.Instance);
            Log.Info("Singleton manager stopping singleton actor [{0}]", singleton.Path);
            singleton.Tell(_terminationMessage);
            return GoTo(ClusterSingletonState.HandingOver).Using(new HandingOverData(singleton, handOverTo));
        }

        private State<ClusterSingletonState, IClusterSingletonData> GoToStopping(IActorRef singleton)
        {
            Log.Info("Singleton manager stopping singleton actor [{0}]", singleton.Path);
            singleton.Tell(_terminationMessage);
            return GoTo(ClusterSingletonState.Stopping).Using(new StoppingData(singleton));
        }

        private void InitializeFSM()
        {
            When(ClusterSingletonState.Start, e =>
            {
                switch (e.FsmEvent)
                {
                    case StartOldestChangedBuffer _:
                        {
                            _oldestChangedBuffer = Context.ActorOf(Actor.Props.Create<OldestChangedBuffer>(_settings.Role).WithDispatcher(Context.Props.Dispatcher));
                            GetNextOldestChanged();
                            return Stay();
                        }
                    case OldestChangedBuffer.InitialOldestState initialOldestState:
                        {
                            _oldestChangedReceived = true;
                            if (initialOldestState.Oldest.Head() == _selfUniqueAddress && initialOldestState.SafeToBeOldest)
                                // oldest immediately
                                return TryGotoOldest();
                            return initialOldestState.Oldest.Head() == _selfUniqueAddress
                                ? GoTo(ClusterSingletonState.BecomingOldest).Using(new BecomingOldestData(initialOldestState.Oldest.FindAll(u => !u.Equals(_selfUniqueAddress))))
                                : GoTo(ClusterSingletonState.Younger).Using(new YoungerData(initialOldestState.Oldest.FindAll(u => !u.Equals(_selfUniqueAddress))));
                        }
                    default:
                        return null;
                }
            });

            When(ClusterSingletonState.Younger, e =>
            {
                if (e.FsmEvent is OldestChangedBuffer.OldestChanged oldestChanged && e.StateData is YoungerData youngerData)
                {
                    _oldestChangedReceived = true;
                    if (oldestChanged.Oldest.Equals(_selfUniqueAddress))
                    {
                        Log.Info("Younger observed OldestChanged: [{0} -> myself]", youngerData.Oldest.Head()?.Address);

                        if (youngerData.Oldest.All(m => _removed.ContainsKey(m)))
                        {
                            return TryGotoOldest();
                        }
                        else
                        {
                            Peer(youngerData.Oldest.Head().Address).Tell(HandOverToMe.Instance);
                            return GoTo(ClusterSingletonState.BecomingOldest).Using(new BecomingOldestData(youngerData.Oldest));
                        }
                    }
                    else
                    {
                        Log.Info("Younger observed OldestChanged: [{0} -> {1}]", youngerData.Oldest.Head()?.Address, oldestChanged.Oldest?.Address);
                        GetNextOldestChanged();
                        if (oldestChanged.Oldest != null && !youngerData.Oldest.Contains(oldestChanged.Oldest))
                            youngerData.Oldest.Insert(0, oldestChanged.Oldest);
                        return Stay().Using(new YoungerData(youngerData.Oldest));
                    }
                }
                else if (e.FsmEvent is MemberDowned memberDowned && memberDowned.Member.UniqueAddress.Equals(_cluster.SelfUniqueAddress))
                {
                    Log.Info("Self downed, stopping ClusterSingletonManager");
                    return Stop();
                }
                else if (e.FsmEvent is MemberRemoved memberRemoved)
                {
                    if (memberRemoved.Member.UniqueAddress.Equals(_cluster.SelfUniqueAddress))
                    {
                        Log.Info("Self removed, stopping ClusterSingletonManager");
                        return Stop();
                    }
                    ScheduleDelayedMemberRemoved(memberRemoved.Member);
                    return Stay();
                }
                else if (e.FsmEvent is DelayedMemberRemoved removed && e.StateData is YoungerData data)
                {
                    if (!_selfExited)
                        Log.Info("Member removed [{0}]", removed.Member.Address);
                    AddRemoved(removed.Member.UniqueAddress);
                    // transition when OldestChanged
                    return Stay().Using(new YoungerData(data.Oldest.FindAll(u => !u.Equals(removed.Member.UniqueAddress))));
                }
                else if (e.FsmEvent is HandOverToMe)
                {
                    var selfStatus = _cluster.SelfMember.Status;
                    if (selfStatus == MemberStatus.Leaving || selfStatus == MemberStatus.Exiting)
                    {
                        Log.Info("Ignoring HandOverToMe in Younger from [{0}] because self is [{1}].",
                            Sender.Path.Address, selfStatus);
                    }
                    else
                    {
                        // this node was probably quickly restarted with same hostname:port,
                        // confirm that the old singleton instance has been stopped
                        Sender.Tell(HandOverDone.Instance);
                    }
                    return Stay();
                }
                return null;
            });

            When(ClusterSingletonState.BecomingOldest, e =>
            {
                if (e.FsmEvent is HandOverInProgress)
                {
                    // confirmation that the hand-over process has started
                    Log.Info("Hand-over in progress at [{0}]", Sender.Path.Address);
                    CancelTimer(HandOverRetryTimer);
                    return Stay();
                }
                else if (e.FsmEvent is HandOverDone && e.StateData is BecomingOldestData b)
                {
                    var oldest = b.PreviousOldest.Head();
                    if (oldest != null)
                    {
                        if (Sender.Path.Address.Equals(oldest.Address))
                        {
                            return TryGotoOldest();
                        }

                        Log.Info("Ignoring HandOverDone in BecomingOldest from [{0}]. Expected previous oldest [{1}]",
                            Sender.Path.Address, oldest.Address);
                        return Stay();
                    }

                    Log.Info("Ignoring HandOverDone in BecomingOldest from [{0}].", Sender.Path.Address);
                    return Stay();
                }
                else if (e.FsmEvent is MemberDowned memberDowned && memberDowned.Member.UniqueAddress.Equals(_cluster.SelfUniqueAddress))
                {
                    Log.Info("Self downed, stopping ClusterSingletonManager");
                    return Stop();
                }
                else if (e.FsmEvent is MemberRemoved memberRemoved)
                {
                    if (memberRemoved.Member.UniqueAddress.Equals(_cluster.SelfUniqueAddress))
                    {
                        Log.Info("Self removed, stopping ClusterSingletonManager");
                        return Stop();
                    }
                    else
                    {
                        ScheduleDelayedMemberRemoved(memberRemoved.Member);
                        return Stay();
                    }
                }
                else if (e.FsmEvent is DelayedMemberRemoved delayed && e.StateData is BecomingOldestData becoming)
                {
                    if (!_selfExited)
                        Log.Info("Member removed [{0}], previous oldest [{1}]", delayed.Member.Address, becoming.PreviousOldest);
                    AddRemoved(delayed.Member.UniqueAddress);
                    if (_cluster.IsTerminated)
                    {
                        // Don't act on DelayedMemberRemoved (starting singleton) if this node is shutting its self down,
                        // just wait for self MemberRemoved
                        return Stay();
                    }
                    else if (becoming.PreviousOldest.Contains(delayed.Member.UniqueAddress) && becoming.PreviousOldest.All(a => _removed.ContainsKey(a)))
                    {
                        return TryGotoOldest();
                    }
                    else
                    {
                        return Stay().Using(new BecomingOldestData(becoming.PreviousOldest.FindAll(u => !u.Equals(delayed.Member.UniqueAddress))));
                    }
                }
                else if (e.FsmEvent is TakeOverFromMe && e.StateData is BecomingOldestData becomingOldestData)
                {
                    var senderAddress = Sender.Path.Address;
                    // it would have been better to include the UniqueAddress in the TakeOverFromMe message,
                    // but can't change due to backwards compatibility
                    var senderUniqueAddress = _cluster.State.Members
                        .Where(m => m.Address.Equals(senderAddress))
                        .Select(m => m.UniqueAddress)
                        .FirstOrDefault();

                    switch (senderUniqueAddress)
                    {
                        case null:
                            // from unknown node, ignore
                            Log.Info("Ignoring TakeOver request from unknown node in BecomingOldest from [{0}]", senderAddress);
                            return Stay();
                        case UniqueAddress _:
                            {
                                switch (becomingOldestData.PreviousOldest.Head())
                                {
                                    case UniqueAddress oldest:
                                        if (oldest.Equals(senderUniqueAddress))
                                            Sender.Tell(HandOverToMe.Instance);
                                        else
                                            Log.Info("Ignoring TakeOver request in BecomingOldest from [{0}]. Expected previous oldest [{1}]",
                                                Sender.Path.Address, oldest.Address);
                                        return Stay();
                                    case null:
                                        Sender.Tell(HandOverToMe.Instance);
                                        becomingOldestData.PreviousOldest.Insert(0, senderUniqueAddress);
                                        return Stay().Using(new BecomingOldestData(becomingOldestData.PreviousOldest));
                                }
                            }
                    }
                }
                else if (e.FsmEvent is HandOverRetry handOverRetry && e.StateData is BecomingOldestData becomingOldest)
                {
                    if (handOverRetry.Count <= _maxHandOverRetries)
                    {
                        var oldest = becomingOldest.PreviousOldest.Head();
                        Log.Info("Retry [{0}], sending HandOverToMe to [{1}]", handOverRetry.Count, oldest?.Address);
                        if (oldest != null) Peer(oldest.Address).Tell(HandOverToMe.Instance);
                        SetTimer(HandOverRetryTimer, new HandOverRetry(handOverRetry.Count + 1), _settings.HandOverRetryInterval);
                        return Stay();
                    }
                    else if (becomingOldest.PreviousOldest != null && becomingOldest.PreviousOldest.All(m => _removed.ContainsKey(m)))
                    {
                        // can't send HandOverToMe, previousOldest unknown for new node (or restart)
                        // previous oldest might be down or removed, so no TakeOverFromMe message is received
                        Log.Info("Timeout in BecomingOldest. Previous oldest unknown, removed and no TakeOver request.");
                        return TryGotoOldest();
                    }
                    else if (_cluster.IsTerminated)
                    {
                        return Stop();
                    }
                    else
                    {
                        throw new ClusterSingletonManagerIsStuckException($"Becoming singleton oldest was stuck because previous oldest [{becomingOldest.PreviousOldest.Head()}] is unresponsive");
                    }
                }

                return null;
            });

            When(ClusterSingletonState.AcquiringLease, e =>
            {
                if (e.FsmEvent is AcquireLeaseResult alr)
                {
                    Log.Info("Acquire lease result {0}", alr.HoldingLease);
                    if (alr.HoldingLease)
                    {
                        return GoToOldest();
                    }
                    else
                    {
                        SetTimer(LeaseRetryTimer, LeaseRetry.Instance, leaseRetryInterval, repeat: false);
                        return Stay().Using(new AcquiringLeaseData(false, null));
                    }
                }

                else if (e.FsmEvent is Terminated t && e.StateData is AcquiringLeaseData ald && t.ActorRef.Equals(ald.Singleton))
                {
                    Log.Info(
                        "Singleton actor terminated. Trying to acquire lease again before re-creating.");
                    // tryAcquireLease sets the state to None for singleton actor
                    return TryAcquireLease();
                }

                else if (e.FsmEvent is AcquireLeaseFailure alf)
                {
                    Log.Error(alf.Failure, "Failed to get lease (will be retried)");
                    SetTimer(LeaseRetryTimer, LeaseRetry.Instance, leaseRetryInterval, repeat: false);
                    return Stay().Using(new AcquiringLeaseData(false, null));
                }

                else if (e.FsmEvent is LeaseRetry)
                {
                    // If lease was lost (so previous state was oldest) then we don't try and get the lease
                    // until the old singleton instance has been terminated so we know there isn't an
                    // instance in this case
                    return TryAcquireLease();
                }

                if (e.FsmEvent is OldestChangedBuffer.OldestChanged oldestChanged && e.StateData is AcquiringLeaseData ald2)
                {
                    return HandleOldestChanged(ald2.Singleton, oldestChanged.Oldest);
                }

                if (e.FsmEvent is HandOverToMe && e.StateData is AcquiringLeaseData ald3)
                {
                    return GoToHandingOver(ald3.Singleton, Sender);
                }

                if (e.FsmEvent is TakeOverFromMe)
                {
                    // already oldest, so confirm and continue like that
                    Sender.Tell(HandOverToMe.Instance);
                    return Stay();
                }

                else if (e.FsmEvent is SelfExiting)
                {
                    SelfMemberExited();
                    // complete memberExitingProgress when handOverDone
                    Sender.Tell(Done.Instance); // reply to ask
                    return Stay();
                }

                else if (e.FsmEvent is MemberDowned md && md.Member.UniqueAddress.Equals(_cluster.SelfUniqueAddress))
                {
                    Log.Info("Self downed, stopping ClusterSingletonManager");
                    return Stop();
                }

                return null;
            });

            When(ClusterSingletonState.Oldest, e =>
            {
                if (e.FsmEvent is OldestChangedBuffer.OldestChanged oldestChanged && e.StateData is OldestData oldestData)
                {
                    return HandleOldestChanged(oldestData.Singleton, oldestChanged.Oldest);
                }
                else if (e.FsmEvent is HandOverToMe && e.StateData is OldestData oldest)
                {
                    return GoToHandingOver(oldest.Singleton, Sender);
                }
                else if (e.FsmEvent is TakeOverFromMe)
                {
                    // already oldest, so confirm and continue like that
                    Sender.Tell(HandOverToMe.Instance);
                    return Stay();
                }
                else if (e.FsmEvent is Terminated terminated && e.StateData is OldestData o && terminated.ActorRef.Equals(o.Singleton))
                {
                    Log.Info("Singleton actor [{0}] was terminated", o.Singleton.Path);
                    return Stay().Using(new OldestData(null));
                }
                else if (e.FsmEvent is SelfExiting)
                {
                    SelfMemberExited();
                    // complete _memberExitingProgress when HandOverDone
                    Sender.Tell(Done.Instance); // reply to ask
                    return Stay();
                }
                else if (e.FsmEvent is MemberDowned memberDowned && e.StateData is OldestData od && memberDowned.Member.UniqueAddress.Equals(_cluster.SelfUniqueAddress))
                {
                    if (od.Singleton == null)
                    {
                        Log.Info("Self downed, stopping ClusterSingletonManager");
                        return Stop();
                    }
                    else
                    {
                        Log.Info("Self downed, stopping");
                        return GoToStopping(od.Singleton);
                    }
                }
                else if (e.FsmEvent is LeaseLost ll && e.StateData is OldestData od2)
                {
                    Log.Warning(ll.Reason, "Lease has been lost. Terminating singleton and trying to re-acquire lease");
                    if (od2.Singleton != null)
                    {
                        od2.Singleton.Tell(_terminationMessage);
                        return GoTo(ClusterSingletonState.AcquiringLease).Using(new AcquiringLeaseData(false, od2.Singleton));
                    }
                    else
                    {
                        return TryAcquireLease();
                    }
                }

                return null;
            });

            When(ClusterSingletonState.WasOldest, e =>
            {
                if (e.FsmEvent is TakeOverRetry takeOverRetry && e.StateData is WasOldestData wasOldestData)
                {
                    if ((_cluster.IsTerminated || _selfExited)
                        && (wasOldestData.NewOldest == null || takeOverRetry.Count > _maxTakeOverRetries))
                    {
                        return wasOldestData.Singleton != null ? GoToStopping(wasOldestData.Singleton) : Stop();
                    }
                    else if (takeOverRetry.Count <= _maxTakeOverRetries)
                    {
                        if (_maxTakeOverRetries - takeOverRetry.Count <= 3)
                            Log.Info("Retry [{0}], sending TakeOverFromMe to [{1}]", takeOverRetry.Count, wasOldestData.NewOldest?.Address);
                        else
                            Log.Debug("Retry [{0}], sending TakeOverFromMe to [{1}]", takeOverRetry.Count, wasOldestData.NewOldest?.Address);

                        if (wasOldestData.NewOldest != null)
                            Peer(wasOldestData.NewOldest.Address).Tell(TakeOverFromMe.Instance);

                        SetTimer(TakeOverRetryTimer, new TakeOverRetry(takeOverRetry.Count + 1), _settings.HandOverRetryInterval, false);
                        return Stay();
                    }
                    else
                    {
                        throw new ClusterSingletonManagerIsStuckException($"Expected hand-over to [{wasOldestData.NewOldest}] never occurred");
                    }
                }
                else if (e.FsmEvent is HandOverToMe && e.StateData is WasOldestData w)
                {
                    return GoToHandingOver(w.Singleton, Sender);
                }
                else if (e.FsmEvent is MemberRemoved removed)
                {
                    if (removed.Member.UniqueAddress.Equals(_cluster.SelfUniqueAddress) && !_selfExited)
                    {
                        Log.Info("Self removed, stopping ClusterSingletonManager");
                        return Stop();
                    }
                    else if (e.StateData is WasOldestData data
                            && data.NewOldest != null
                            && !_selfExited
                            && removed.Member.UniqueAddress.Equals(data.NewOldest))
                    {
                        AddRemoved(removed.Member.UniqueAddress);
                        return GoToHandingOver(data.Singleton, null);
                    }
                }
                else if (e.FsmEvent is Terminated t
                    && e.StateData is WasOldestData oldestData
                    && t.ActorRef.Equals(oldestData.Singleton))
                {
                    Log.Info("Singleton actor [{0}] was terminated", oldestData.Singleton.Path);
                    return Stay().Using(new WasOldestData(null, oldestData.NewOldest));
                }
                else if (e.FsmEvent is SelfExiting)
                {
                    SelfMemberExited();
                    // complete _memberExitingProgress when HandOverDone
                    Sender.Tell(Done.Instance); // reply to ask
                    return Stay();
                }
                else if (e.FsmEvent is MemberDowned memberDowned && e.StateData is WasOldestData od && memberDowned.Member.UniqueAddress.Equals(_cluster.SelfUniqueAddress))
                {
                    if (od.Singleton == null)
                    {
                        Log.Info("Self downed, stopping ClusterSingletonManager");
                        return Stop();
                    }
                    else
                    {
                        Log.Info("Self downed, stopping");
                        return GoToStopping(od.Singleton);
                    }
                }

                return null;
            });

            When(ClusterSingletonState.HandingOver, e =>
            {
                if (e.FsmEvent is Terminated terminated
                    && e.StateData is HandingOverData handingOverData
                    && terminated.ActorRef.Equals(handingOverData.Singleton))
                {
                    return HandleHandOverDone(handingOverData.HandOverTo);
                }
                else if (e.FsmEvent is HandOverToMe
                    && e.StateData is HandingOverData d
                    && d.HandOverTo.Equals(Sender))
                {
                    // retry
                    Sender.Tell(HandOverInProgress.Instance);
                    return Stay();
                }
                else if (e.FsmEvent is SelfExiting)
                {
                    SelfMemberExited();
                    // complete _memberExitingProgress when HandOverDone
                    Sender.Tell(Done.Instance);
                    return Stay();
                }

                return null;
            });

            When(ClusterSingletonState.Stopping, e =>
            {
                if (e.FsmEvent is Terminated terminated
                    && e.StateData is StoppingData stoppingData
                    && terminated.ActorRef.Equals(stoppingData.Singleton))
                {
                    Log.Info("Singleton actor [{0}] was terminated", stoppingData.Singleton.Path);
                    return Stop();
                }

                return null;
            });

            When(ClusterSingletonState.End, e =>
            {
                if (e.FsmEvent is MemberRemoved removed
                    && removed.Member.UniqueAddress.Equals(_cluster.SelfUniqueAddress))
                {
                    Log.Info("Self removed, stopping ClusterSingletonManager");
                    return Stop();
                }
                if (e.FsmEvent is OldestChangedBuffer.OldestChanged || e.FsmEvent is HandOverToMe)
                {
                    // not interested anymore - waiting for removal
                    return Stay();
                }

                return null;
            });

            WhenUnhandled(e =>
            {
                if (e.FsmEvent is SelfExiting)
                {
                    SelfMemberExited();
                    // complete _memberExitingProgress when HandOverDone
                    _memberExitingProgress.TrySetResult(Done.Instance);
                    Sender.Tell(Done.Instance); // reply to ask
                    return Stay();
                }
                if (e.FsmEvent is MemberRemoved removed)
                {
                    if (removed.Member.UniqueAddress.Equals(_cluster.SelfUniqueAddress) && !_selfExited)
                    {
                        Log.Info("Self removed, stopping ClusterSingletonManager");
                        return Stop();
                    }
                    else
                    {
                        if (!_selfExited)
                            Log.Info("Member removed [{0}]", removed.Member.Address);

                        AddRemoved(removed.Member.UniqueAddress);
                        return Stay();
                    }
                }
                if (e.FsmEvent is DelayedMemberRemoved delayedMemberRemoved)
                {
                    if (!_selfExited)
                        Log.Info("Member removed [{0}]", delayedMemberRemoved.Member.Address);

                    AddRemoved(delayedMemberRemoved.Member.UniqueAddress);
                    return Stay();
                }
                if (e.FsmEvent is TakeOverFromMe)
                {
                    Log.Debug("Ignoring TakeOver request in [{0}] from [{1}].", StateName, Sender.Path.Address);
                    return Stay();
                }
                if (e.FsmEvent is Cleanup)
                {
                    CleanupOverdueNotMemberAnyMore();
                    return Stay();
                }
                if (e.FsmEvent is MemberDowned memberDowned)
                {
                    if (memberDowned.Member.UniqueAddress.Equals(_cluster.SelfUniqueAddress))
                        Log.Info("Self downed, waiting for removal");
                    return Stay();
                }

                if (e.FsmEvent is ReleaseLeaseFailure rlf)
                {
                    Log.Error(
                        rlf.Failure,
                        "Failed to release lease. Singleton may not be able to run on another node until lease timeout occurs");
                    return Stay();
                }

                if (e.FsmEvent is ReleaseLeaseResult rlr)
                {
                    if (rlr.Released)
                    {
                        Log.Info("Lease released");
                    }
                    else
                    {
                        // TODO we could retry
                        Log.Error(
                            "Failed to release lease. Singleton may not be able to run on another node until lease timeout occurs");
                    }
                    return Stay();
                }
                return null;
            });

            OnTransition((from, to) =>
            {
                Log.Info("ClusterSingletonManager state change [{0} -> {1}] {2}", from, to, StateData.ToString());

                if (to == ClusterSingletonState.BecomingOldest) SetTimer(HandOverRetryTimer, new HandOverRetry(1), _settings.HandOverRetryInterval);
                if (from == ClusterSingletonState.BecomingOldest) CancelTimer(HandOverRetryTimer);
                if (from == ClusterSingletonState.WasOldest) CancelTimer(TakeOverRetryTimer);

                if (from == ClusterSingletonState.AcquiringLease && to != ClusterSingletonState.Oldest)
                {
                    if (StateData is AcquiringLeaseData ald && ald.LeaseRequestInProgress)
                    {
                        Log.Info("Releasing lease as leaving AcquiringLease going to [{0}]", to);
                        if (lease != null)
                        {
                            lease.Release().ContinueWith(r =>
                            {
                                if (r.IsCanceled || r.IsFaulted)
                                    return (object)new ReleaseLeaseFailure(r.Exception);
                                return new ReleaseLeaseResult(r.Result);
                            }).PipeTo(Self);
                        }
                    }
                }

                if (from == ClusterSingletonState.Oldest && lease != null)
                {
                    Log.Info("Releasing lease as leaving Oldest");
                    lease.Release().ContinueWith(r => new ReleaseLeaseResult(r.Result)).PipeTo(Self);
                }

                if (to == ClusterSingletonState.Younger || to == ClusterSingletonState.Oldest) GetNextOldestChanged();
                if (to == ClusterSingletonState.Younger || to == ClusterSingletonState.End)
                {
                    if (_removed.ContainsKey(_cluster.SelfUniqueAddress))
                    {
                        Log.Info("Self removed, stopping ClusterSingletonManager");
                        Context.Stop(Self);
                    }
                }
            });

            StartWith(ClusterSingletonState.Start, Uninitialized.Instance);
        }

        private void SelfMemberExited()
        {
            _selfExited = true;
            Log.Info("Exited [{0}]", _cluster.SelfAddress);
        }

        private void ScheduleDelayedMemberRemoved(Member member)
        {
            if (_removalMargin > TimeSpan.Zero)
            {
                Log.Debug("Schedule DelayedMemberRemoved for {0}", member.Address);
                Context.System.Scheduler.ScheduleTellOnce(_removalMargin, Self, new DelayedMemberRemoved(member), Self);
            }
            else Self.Tell(new DelayedMemberRemoved(member));
        }
    }
}
