//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonManager.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Coordination;
using Akka.Dispatch;
using Akka.Event;
using Akka.Remote;
using Akka.Util.Internal;
using static Akka.Cluster.ClusterEvent;

namespace Akka.Cluster.Tools.Singleton
{
    /// <summary>
    /// Control messages used for the cluster singleton
    /// </summary>
    public interface IClusterSingletonMessage
    {
    }

    /// <summary>
    /// Sent from new oldest to previous oldest to initiate the
    /// hand-over process. <see cref="HandOverInProgress"/>  and <see cref="HandOverDone"/>
    /// are expected replies.
    /// </summary>
    [Serializable]
    internal sealed class HandOverToMe : IClusterSingletonMessage, IDeadLetterSuppression
    {
        public static HandOverToMe Instance { get; } = new();

        private HandOverToMe()
        {
        }
    }

    /// <summary>
    /// Confirmation by the previous oldest that the hand
    /// over process, shut down of the singleton actor, has
    /// started.
    /// </summary>
    [Serializable]
    internal sealed class HandOverInProgress : IClusterSingletonMessage
    {
        public static HandOverInProgress Instance { get; } = new();

        private HandOverInProgress()
        {
        }
    }

    /// <summary>
    /// Confirmation by the previous oldest that the singleton
    /// actor has been terminated and the hand-over process is
    /// completed.
    /// </summary>
    [Serializable]
    internal sealed class HandOverDone : IClusterSingletonMessage
    {
        public static HandOverDone Instance { get; } = new();

        private HandOverDone()
        {
        }
    }

    /// <summary>
    /// Sent from from previous oldest to new oldest to
    /// initiate the normal hand-over process.
    /// Especially useful when new node joins and becomes
    /// oldest immediately, without knowing who was previous
    /// oldest.
    /// </summary>
    [Serializable]
    internal sealed class TakeOverFromMe : IClusterSingletonMessage, IDeadLetterSuppression
    {
        public static TakeOverFromMe Instance { get; } = new();

        private TakeOverFromMe()
        {
        }
    }

    /// <summary>
    /// Scheduled task to cleanup overdue members that have been removed
    /// </summary>
    [Serializable]
    internal sealed class Cleanup
    {
        public static Cleanup Instance { get; } = new();

        private Cleanup()
        {
        }
    }

    /// <summary>
    /// Initialize the oldest changed buffer actor.
    /// </summary>
    [Serializable]
    internal sealed class StartOldestChangedBuffer
    {
        public static StartOldestChangedBuffer Instance { get; } = new();

        private StartOldestChangedBuffer()
        {
        }
    }

    /// <summary>
    /// Retry a failed cluster singleton handover.
    /// </summary>
    /// <param name="Count">The number of retries</param>
    [Serializable]
    internal sealed record HandOverRetry(int Count);

    /// <summary>
    /// Used to retry a failed takeover operation.
    /// </summary>
    /// <param name="Count">The number of retries</param>
    [Serializable]
    internal sealed record TakeOverRetry(int Count);


    [Serializable]
    internal sealed class LeaseRetry : INoSerializationVerificationNeeded
    {
        public static LeaseRetry Instance { get; } = new();

        private LeaseRetry()
        {
        }
    }

    /// <summary>
    /// The data type used by the <see cref="ClusterSingletonManager"/>
    /// </summary>
    public interface IClusterSingletonData
    {
    }

    /// <summary>
    /// The initial state of the cluster singleton manager at startup before it receives any data.
    /// </summary>
    [Serializable]
    internal sealed class Uninitialized : IClusterSingletonData
    {
        public static Uninitialized Instance { get; } = new();

        private Uninitialized()
        {
        }
    }

    /// <summary>
    /// The state used after we've initialized and are aware of all of the other
    /// older members currently present in the cluster.
    /// </summary>
    [Serializable]
    internal sealed class YoungerData : IClusterSingletonData
    {
        /// <summary>
        /// The age-ordered (ascending) set of addresses of older nodes than us in the cluster.
        /// </summary>
        public ImmutableList<UniqueAddress> Oldest { get; }

        public YoungerData(ImmutableList<UniqueAddress> oldest)
        {
            Oldest = oldest;
        }
    }

    /// <summary>
    /// State when we're transitioning to becoming the oldest singleton manager.
    /// </summary>
    [Serializable]
    internal sealed class BecomingOldestData : IClusterSingletonData
    {
        /// <summary>
        /// The previous oldest nodes - can be empty
        /// </summary>
        public ImmutableList<UniqueAddress> PreviousOldest { get; }

        public BecomingOldestData(ImmutableList<UniqueAddress> previousOldest)
        {
            PreviousOldest = previousOldest;
        }
    }

    /// <summary>
    /// State for after we've successfully transitioned to oldest, so we're hosting
    /// the singleton actor.
    /// </summary>
    [Serializable]
    internal sealed class OldestData : IClusterSingletonData
    {
        /// <summary>
        /// The reference to the current singleton running on this node.
        /// </summary>
        /// <remarks>
        /// Cam be explicitly set to <c>null</c> when we are leaving the cluster
        /// and the singleton has to be terminated.
        /// </remarks>
        public IActorRef? Singleton { get; }

        public OldestData(IActorRef? singleton)
        {
            Singleton = singleton;
        }
    }

    /// <summary>
    /// State we're transitioning into once we know we've started the hand-over process.
    /// </summary>
    [Serializable]
    internal sealed class WasOldestData : IClusterSingletonData
    {
        /// <summary>
        /// The reference to the singleton.
        /// </summary>
        /// <remarks>
        /// Can be <c>null</c> in edge cases where the node became the oldest but was already shutting down.
        /// Shouldn't happen very often, but it's not impossible.
        /// </remarks>
        public IActorRef? Singleton { get; }

        /// <summary>
        /// The address of the new oldest node.
        /// </summary>
        /// <remarks>
        /// Can be <c>null</c> if we don't know who the new oldest is - for instance, during a full cluster
        /// shutdown (in which case, there won't be any hand-over.)
        /// </remarks>
        public UniqueAddress? NewOldest { get; }

        public WasOldestData(IActorRef? singleton, UniqueAddress? newOldest)
        {
            Singleton = singleton;
            NewOldest = newOldest;
        }
    }

    /// <summary>
    /// State when we're handing over control of the singleton to another node.
    /// </summary>
    [Serializable]
    internal sealed class HandingOverData : IClusterSingletonData
    {
        /// <summary>
        /// The current singleton reference
        /// </summary>
        public IActorRef Singleton { get; }

        /// <summary>
        /// The actor we're handing over to.
        /// </summary>
        /// <remarks>
        /// Can be <c>null</c> if they haven't contacted us yet and some other edge conditions.
        /// </remarks>
        public IActorRef? HandOverTo { get; }

        public HandingOverData(IActorRef singleton, IActorRef? handOverTo)
        {
            Singleton = singleton;
            HandOverTo = handOverTo;
        }
    }

    /// <summary>
    /// For when we are transitioning to a stopping state.
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
        public static EndData Instance { get; } = new();

        private EndData()
        {
        }
    }

    /// <summary>
    /// When we are moving into the "acquiring lease" state
    /// </summary>
    [Serializable]
    internal sealed class AcquiringLeaseData : IClusterSingletonData
    {
        /// <summary>
        /// Is there already a lease request in-progress?
        /// </summary>
        public bool LeaseRequestInProgress { get; }

        /// <summary>
        /// A reference to the current singleton, if it exists.
        /// </summary>
        public IActorRef? Singleton { get; }

        public AcquiringLeaseData(bool leaseRequestInProgress, IActorRef? singleton)
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
        public Exception? Failure { get; }

        public AcquireLeaseFailure(Exception? failure)
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
    /// We delay <see cref="MemberRemoved"/> notifications in order to tolerate
    /// downed nodes removed by the SBR, as the singleton may still be running there
    /// until the node shuts itself down.
    /// </summary>
    [Serializable]
    internal sealed class DelayedMemberRemoved
    {
        /// <summary>
        /// The removed member.
        /// </summary>
        public Member Member { get; }

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
        private SelfExiting()
        {
        }

        public static SelfExiting Instance { get; } = new();
    }

    /// <summary>
    /// The current FSM state of the cluster singleton manager.
    /// </summary>
    [Serializable]
    public enum ClusterSingletonState
    {
        Start,
        AcquiringLease,

        /// <summary>
        /// Oldest is the state where we run the singleton.
        /// </summary>
        Oldest,
        Younger,

        /// <summary>
        /// In the BecomingOldest state we start the hand-off process
        /// with the WasOldest node, which is exiting the cluster.
        /// </summary>
        BecomingOldest,

        /// <summary>
        /// We were the oldest node, but now we're exiting the cluster.
        /// </summary>
        WasOldest,

        /// <summary>
        /// We are handing over our singleton to the new oldest node.
        /// </summary>
        HandingOver,

        /// <summary>
        /// Not used
        /// </summary>
        TakeOver,

        /// <summary>
        /// We are shutting down.
        /// </summary>
        Stopping,

        /// <summary>
        /// We have shut down and are terminating.
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
        public ClusterSingletonManagerIsStuckException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterSingletonManagerIsStuckException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
        public ClusterSingletonManagerIsStuckException(SerializationInfo info, StreamingContext context) : base(info,
            context)
        {
        }
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
        [Obsolete("Deprecated and will be removed in v1.6, please use ClusterSingleton.DefaultConfig() instead. Since 1.5.32.")]
        public static Config DefaultConfig()
        {
            return ConfigurationFactory.FromResource<ClusterSingletonManager>(
                "Akka.Cluster.Tools.Singleton.reference.conf");
        }

        /// <summary>
        /// Creates props for the current cluster singleton manager using <see cref="PoisonPill"/>
        /// as the default termination message.
        /// </summary>
        /// <param name="singletonProps"><see cref="Actor.Props"/> of the singleton actor instance.</param>
        /// <param name="settings">Cluster singleton manager settings.</param>
        /// <returns>Props for the <see cref="ClusterSingletonManager"/>.</returns>
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
        /// <returns>Props for the <see cref="ClusterSingletonManager"/>.</returns>
        public static Props Props(Props singletonProps, object terminationMessage,
            ClusterSingletonManagerSettings settings)
        {
            return Actor.Props.Create(() => new ClusterSingletonManager(singletonProps, terminationMessage, settings))
                .WithDispatcher(Dispatchers.InternalDispatcherId)
                .WithDeploy(Deploy.Local);
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

        // started when self member is Up
        private IActorRef? _oldestChangedBuffer;

        // keep track of previously removed members
        private ImmutableDictionary<UniqueAddress, Deadline> _removed =
            ImmutableDictionary<UniqueAddress, Deadline>.Empty;

        private readonly TimeSpan _removalMargin;
        private readonly int _maxHandOverRetries;
        private readonly int _maxTakeOverRetries;
        private readonly Cluster _cluster = Cluster.Get(Context.System);
        private readonly UniqueAddress _selfUniqueAddress;

        private readonly CoordinatedShutdown _coordShutdown = CoordinatedShutdown.Get(Context.System);
        private readonly TaskCompletionSource<Done> _memberExitingProgress = new();

        private readonly string _singletonLeaseName;
        private readonly Lease? _lease;
        private readonly TimeSpan _leaseRetryInterval = TimeSpan.FromSeconds(5); // won't be used

        public ClusterSingletonManager(Props singletonProps, object terminationMessage,
            ClusterSingletonManagerSettings settings)
        {
            var role = settings.Role;
            if (!string.IsNullOrEmpty(role) && !_cluster.SelfRoles.Contains(role))
                throw new ArgumentException(
                    $"This cluster member [{_cluster.SelfAddress}] doesn't have the role [{role}]");

            _singletonProps = singletonProps;
            _terminationMessage = terminationMessage;
            _settings = settings;
            _singletonLeaseName = $"{Context.System.Name}-singleton-{Self.Path}";

            if (settings.LeaseSettings != null)
            {
                _lease = LeaseProvider.Get(Context.System)
                    .GetLease(_singletonLeaseName, settings.LeaseSettings.LeaseImplementation,
                        _cluster.SelfAddress.HostPort());
                _leaseRetryInterval = settings.LeaseSettings.LeaseRetryInterval;
            }

            // Added in v1.5.27 to signal to users who were considering AppVersion
            // in their singleton placement decisions that we don't do that any more
#pragma warning disable CS0618 // Type or member is obsolete
            if (settings.ConsiderAppVersion)
#pragma warning restore CS0618 // Type or member is obsolete
            {
                Log.Warning(
                    "As of Akka.NET v1.5.27, The 'ConsiderAppVersion' setting is no longer supported and will " +
                    "be removed in a future version because this setting is inherently unsafe and can result in split brains. " +
                    "Singleton instances will always be created on the oldest member.");
            }

            _removalMargin = (settings.RemovalMargin <= TimeSpan.Zero)
                ? _cluster.DowningProvider.DownRemovalMargin
                : settings.RemovalMargin;

            var n = (int)(_removalMargin.TotalMilliseconds / _settings.HandOverRetryInterval.TotalMilliseconds);

            var minRetries =
                Context.System.Settings.Config.GetInt("akka.cluster.singleton.min-number-of-hand-over-retries", 0);
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
                    return self.Ask(SelfExiting.Instance, timeout).ContinueWith(_ => Done.Instance);
                }
            });
        }

        private ILoggingAdapter Log { get; } = Context.GetLogger();

        /// <inheritdoc cref="ActorBase.PreStart"/>
        protected override void PreStart()
        {
            if (_cluster.IsTerminated)
                throw new ActorInitializationException("Cluster node must not be terminated");

            // subscribe to cluster changes, re-subscribe when restart
            _cluster.Subscribe(Self, ClusterEvent.InitialStateAsEvents, typeof(ClusterEvent.MemberRemoved),
                typeof(ClusterEvent.MemberDowned));

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
            if (_removed.TryGetValue(node, out _))
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
            _removed = _removed.Where(kv => !kv.Value.IsOverdue).ToImmutableDictionary();
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

            if (_lease == null)
                throw new ArgumentNullException(nameof(_lease),
                    "Lease must be initialized before trying to acquire it");

            _lease.Acquire(reason => { self.Tell(new LeaseLost(reason)); }).ContinueWith(r =>
            {
                if (r.IsFaulted || r.IsCanceled)
                    return (object)new AcquireLeaseFailure(r.Exception);
                return new AcquireLeaseResult(r.Result);
            }).PipeTo(self);

            return GoTo(ClusterSingletonState.AcquiringLease).Using(new AcquiringLeaseData(true, null));
        }

        // Try and go to oldest, taking the lease if needed
        private State<ClusterSingletonState, IClusterSingletonData> TryGotoOldest()
        {
            // check if lease
            if (_lease == null)
                return GoToOldest();

            Log.Info("Trying to acquire lease before starting singleton");
            return TryAcquireLease();
        }

        private State<ClusterSingletonState, IClusterSingletonData> GoToOldest()
        {
            var singleton = Context.Watch(Context.ActorOf(_singletonProps, _settings.SingletonName));
            Log.Info("Singleton manager started singleton actor [{0}] ", singleton.Path);
            return
                GoTo(ClusterSingletonState.Oldest).Using(new OldestData(singleton));
        }

        private State<ClusterSingletonState, IClusterSingletonData> HandleOldestChanged(IActorRef? singleton,
            UniqueAddress? oldest)
        {
            _oldestChangedReceived = true;
            Log.Info("{0} observed OldestChanged: [{1} -> {2}]", StateName, _cluster.SelfAddress, oldest?.Address);
            switch (oldest)
            {
                case not null when oldest.Equals(_cluster.SelfUniqueAddress):
                    // already oldest
                    return Stay();
                case not null when !_selfExited && _removed.ContainsKey(oldest):
                    // The member removal was not completed and the old removed node is considered
                    // oldest again. Safest is to terminate the singleton instance and goto Younger.
                    // This node will become oldest again when the other is removed again.
                    return GoToHandingOver(singleton, null);
                case not null:
                    // send TakeOver request in case the new oldest doesn't know previous oldest
                    Peer(oldest.Address).Tell(TakeOverFromMe.Instance);
                    SetTimer(TakeOverRetryTimer, new TakeOverRetry(1), _settings.HandOverRetryInterval, repeat: false);
                    return GoTo(ClusterSingletonState.WasOldest)
                        .Using(new WasOldestData(singleton, oldest));
                case null:
                    // new oldest will initiate the hand-over
                    SetTimer(TakeOverRetryTimer, new TakeOverRetry(1), _settings.HandOverRetryInterval, repeat: false);
                    return GoTo(ClusterSingletonState.WasOldest)
                        .Using(new WasOldestData(singleton, newOldest: null));
            }
        }

        private State<ClusterSingletonState, IClusterSingletonData> HandleHandOverDone(IActorRef? handOverTo)
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

            if (handOverTo == null)
            {
                return GoTo(ClusterSingletonState.Younger).Using(new YoungerData(ImmutableList<UniqueAddress>.Empty));
            }

            return GoTo(ClusterSingletonState.End).Using(EndData.Instance);
        }

        private State<ClusterSingletonState, IClusterSingletonData> GoToHandingOver(IActorRef? singleton,
            IActorRef? handOverTo)
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
                    case StartOldestChangedBuffer:
                    {
                        _oldestChangedBuffer = Context.ActorOf(
                            Actor.Props.Create(() =>
                                    new OldestChangedBuffer(_settings.Role, false))
                                .WithDispatcher(Context.Props.Dispatcher));
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
                            ? GoTo(ClusterSingletonState.BecomingOldest).Using(
                                new BecomingOldestData(
                                    initialOldestState.Oldest.FindAll(u => !u.Equals(_selfUniqueAddress))))
                            : GoTo(ClusterSingletonState.Younger)
                                .Using(new YoungerData(
                                    initialOldestState.Oldest.FindAll(u => !u.Equals(_selfUniqueAddress))));
                    }
                    case HandOverToMe:
                        // nothing to hand over in start
                        return Stay();
                    default:
                        return null;
                }
            });

            When(ClusterSingletonState.Younger, e =>
            {
                switch (e.FsmEvent)
                {
                    case OldestChangedBuffer.OldestChanged oldestChanged when e.StateData is YoungerData youngerData:
                    {
                        _oldestChangedReceived = true;
                        if (oldestChanged.NewOldest != null && oldestChanged.NewOldest.Equals(_selfUniqueAddress))
                        {
                            Log.Info("Younger observed OldestChanged: [{0} -> myself]",
                                oldestChanged.PreviousOldest?.Address ?? youngerData.Oldest.Head()?.Address);
                            if (youngerData.Oldest.All(m => _removed.ContainsKey(m)))
                            {
                                return TryGotoOldest();
                            }

                            // explicitly re-order the list to make sure that the oldest, as indicated to us by the OldestChangedBuffer,
                            //  is the first element - resolves bug https://github.com/akkadotnet/akka.net/issues/6973
                            var newOldestState = oldestChanged.PreviousOldest switch
                            {
                                not null => ImmutableList<UniqueAddress>.Empty.Add(oldestChanged.PreviousOldest)
                                    .AddRange(youngerData.Oldest.Where(c => c != oldestChanged.PreviousOldest)),
                                _ => youngerData.Oldest
                            };

                            Peer(newOldestState.Head().Address).Tell(HandOverToMe.Instance);
                            return GoTo(ClusterSingletonState.BecomingOldest)
                                .Using(new BecomingOldestData(newOldestState));
                        }

                        Log.Info("Younger observed OldestChanged: [{0} -> {1}]", youngerData.Oldest.Head()?.Address,
                            oldestChanged.NewOldest?.Address);
                        GetNextOldestChanged();

                        var newOldest = oldestChanged.NewOldest switch
                        {
                            not null when !youngerData.Oldest.Contains(oldestChanged.NewOldest) => ImmutableList<
                                UniqueAddress>.Empty.Add(oldestChanged.NewOldest).AddRange(youngerData.Oldest),
                            _ => youngerData.Oldest
                        };

                        return Stay().Using(new YoungerData(newOldest));
                    }
                    case MemberDowned memberDowned
                        when memberDowned.Member.UniqueAddress.Equals(_cluster.SelfUniqueAddress):
                        Log.Info("Self downed, stopping ClusterSingletonManager");
                        return Stop();
                    case MemberRemoved memberRemoved
                        when memberRemoved.Member.UniqueAddress.Equals(_cluster.SelfUniqueAddress):
                        Log.Info("Self removed, stopping ClusterSingletonManager");
                        return Stop();
                    case MemberRemoved memberRemoved:
                        ScheduleDelayedMemberRemoved(memberRemoved.Member);
                        return Stay();
                    case DelayedMemberRemoved removed when e.StateData is YoungerData data:
                    {
                        if (!_selfExited)
                            Log.Info("Member removed [{0}]", removed.Member.Address);
                        AddRemoved(removed.Member.UniqueAddress);
                        // transition when OldestChanged
                        return Stay()
                            .Using(new YoungerData(data.Oldest.FindAll(u => !u.Equals(removed.Member.UniqueAddress))));
                    }
                    case HandOverToMe:
                    {
                        var selfStatus = _cluster.SelfMember.Status;
                        if (selfStatus is MemberStatus.Leaving or MemberStatus.Exiting)
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
                    default:
                        return null;
                }
            });

            When(ClusterSingletonState.BecomingOldest, e =>
            {
                switch (e.FsmEvent)
                {
                    case HandOverInProgress:
                        // confirmation that the hand-over process has started
                        Log.Info("Hand-over in progress at [{0}]", Sender.Path.Address);
                        CancelTimer(HandOverRetryTimer);
                        return Stay();
                    case HandOverDone when e.StateData is BecomingOldestData b:
                    {
                        var oldest = b.PreviousOldest.Head();
                        if (oldest != null)
                        {
                            if (Sender.Path.Address.Equals(oldest.Address))
                            {
                                return TryGotoOldest();
                            }

                            Log.Info(
                                "Ignoring HandOverDone in BecomingOldest from [{0}]. Expected previous oldest [{1}]",
                                Sender.Path.Address, oldest.Address);
                            return Stay();
                        }

                        Log.Info("Ignoring HandOverDone in BecomingOldest from [{0}].", Sender.Path.Address);
                        return Stay();
                    }
                    case MemberDowned memberDowned
                        when memberDowned.Member.UniqueAddress.Equals(_cluster.SelfUniqueAddress):
                        Log.Info("Self downed, stopping ClusterSingletonManager");
                        return Stop();
                    case MemberRemoved memberRemoved
                        when memberRemoved.Member.UniqueAddress.Equals(_cluster.SelfUniqueAddress):
                        Log.Info("Self removed, stopping ClusterSingletonManager");
                        return Stop();
                    case MemberRemoved memberRemoved:
                        ScheduleDelayedMemberRemoved(memberRemoved.Member);
                        return Stay();
                    case DelayedMemberRemoved delayed when e.StateData is BecomingOldestData becoming:
                    {
                        if (!_selfExited)
                            Log.Info("Member removed [{0}], previous oldest [{1}]", delayed.Member.Address,
                                becoming.PreviousOldest);
                        AddRemoved(delayed.Member.UniqueAddress);
                        if (_cluster.IsTerminated)
                        {
                            // Don't act on DelayedMemberRemoved (starting singleton) if this node is shutting its self down,
                            // just wait for self MemberRemoved
                            return Stay();
                        }

                        if (becoming.PreviousOldest.Contains(delayed.Member.UniqueAddress) &&
                            becoming.PreviousOldest.All(a => _removed.ContainsKey(a)))
                        {
                            return TryGotoOldest();
                        }

                        return Stay()
                            .Using(new BecomingOldestData(
                                becoming.PreviousOldest.FindAll(u => !u.Equals(delayed.Member.UniqueAddress))));
                    }
                    case TakeOverFromMe when e.StateData is BecomingOldestData becomingOldestData:
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
                                Log.Info("Ignoring TakeOver request from unknown node in BecomingOldest from [{0}]",
                                    senderAddress);
                                return Stay();
                            case not null:
                            {
                                switch (becomingOldestData.PreviousOldest.Head())
                                {
                                    case { } oldest:
                                        if (oldest.Equals(senderUniqueAddress))
                                            Sender.Tell(HandOverToMe.Instance);
                                        else
                                            Log.Info(
                                                "Ignoring TakeOver request in BecomingOldest from [{0}]. Expected previous oldest [{1}]",
                                                Sender.Path.Address, oldest.Address);
                                        return Stay();
                                    case null:
                                        Sender.Tell(HandOverToMe.Instance);
                                        return Stay().Using(new BecomingOldestData(ImmutableList<UniqueAddress>.Empty
                                            .Add(senderUniqueAddress).AddRange(becomingOldestData.PreviousOldest)));
                                }
                            }
                        }
                    }
                    case HandOverRetry handOverRetry when e.StateData is BecomingOldestData becomingOldest:
                    {
                        if (handOverRetry.Count <= _maxHandOverRetries)
                        {
                            var oldest = becomingOldest.PreviousOldest.Head();
                            Log.Info("Retry [{0}], sending HandOverToMe to [{1}]", handOverRetry.Count,
                                oldest?.Address);
                            if (oldest != null) Peer(oldest.Address).Tell(HandOverToMe.Instance);
                            SetTimer(HandOverRetryTimer, new HandOverRetry(handOverRetry.Count + 1),
                                _settings.HandOverRetryInterval);
                            return Stay();
                        }

                        if (becomingOldest.PreviousOldest.Count > 0 &&
                            becomingOldest.PreviousOldest.All(m => _removed.ContainsKey(m)))
                        {
                            // can't send HandOverToMe, previousOldest unknown for new node (or restart)
                            // previous oldest might be down or removed, so no TakeOverFromMe message is received
                            Log.Info(
                                "Timeout in BecomingOldest. Previous oldest unknown, removed and no TakeOver request.");
                            return TryGotoOldest();
                        }

                        if (_cluster.IsTerminated)
                        {
                            return Stop();
                        }

                        throw new ClusterSingletonManagerIsStuckException(
                            $"Becoming singleton oldest was stuck because previous oldest [{becomingOldest.PreviousOldest.Head()}] is unresponsive");
                    }
                }

                return null;
            });

            When(ClusterSingletonState.AcquiringLease, e =>
            {
                switch (e.FsmEvent)
                {
                    case AcquireLeaseResult alr:
                    {
                        Log.Info("Acquire lease result {0}", alr.HoldingLease);
                        if (alr.HoldingLease)
                        {
                            return GoToOldest();
                        }

                        SetTimer(LeaseRetryTimer, LeaseRetry.Instance, _leaseRetryInterval, repeat: false);
                        return Stay().Using(new AcquiringLeaseData(false, null));
                    }
                    case Terminated t when e.StateData is AcquiringLeaseData ald && t.ActorRef.Equals(ald.Singleton):
                        Log.Info(
                            "Singleton actor terminated. Trying to acquire lease again before re-creating.");
                        // tryAcquireLease sets the state to None for singleton actor
                        return TryAcquireLease();
                    case AcquireLeaseFailure alf:
                        Log.Error(alf.Failure, "Failed to get lease (will be retried)");
                        SetTimer(LeaseRetryTimer, LeaseRetry.Instance, _leaseRetryInterval, repeat: false);
                        return Stay().Using(new AcquiringLeaseData(false, null));
                    case LeaseRetry:
                        // If lease was lost (so previous state was oldest) then we don't try and get the lease
                        // until the old singleton instance has been terminated so we know there isn't an
                        // instance in this case
                        return TryAcquireLease();
                    case OldestChangedBuffer.OldestChanged oldestChanged when e.StateData is AcquiringLeaseData ald2:
                        return HandleOldestChanged(ald2.Singleton, oldestChanged.NewOldest);
                    case HandOverToMe when e.StateData is AcquiringLeaseData ald3:
                        return GoToHandingOver(ald3.Singleton, Sender);
                    case TakeOverFromMe:
                        // already oldest, so confirm and continue like that
                        Sender.Tell(HandOverToMe.Instance);
                        return Stay();
                    case SelfExiting:
                        SelfMemberExited();
                        // complete memberExitingProgress when handOverDone
                        Sender.Tell(Done.Instance); // reply to ask
                        return Stay();
                    case MemberDowned md when md.Member.UniqueAddress.Equals(_cluster.SelfUniqueAddress):
                        Log.Info("Self downed, stopping ClusterSingletonManager");
                        return Stop();
                    default:
                        return null;
                }
            });

            When(ClusterSingletonState.Oldest, e =>
            {
                switch (e.FsmEvent)
                {
                    case OldestChangedBuffer.OldestChanged oldestChanged when e.StateData is OldestData oldestData:
                        return HandleOldestChanged(oldestData.Singleton, oldestChanged.NewOldest);

                    case HandOverToMe when e.StateData is OldestData oldest:
                        return GoToHandingOver(oldest.Singleton, Sender);

                    case TakeOverFromMe:
                        // already oldest, so confirm and continue like that
                        Sender.Tell(HandOverToMe.Instance);
                        return Stay();

                    case Terminated terminated
                        when e.StateData is OldestData o && terminated.ActorRef.Equals(o.Singleton):
                        Log.Info("Singleton actor [{0}] was terminated", o.Singleton.Path);
                        return Stay().Using(new OldestData(null));

                    case SelfExiting:
                        SelfMemberExited();
                        // complete _memberExitingProgress when HandOverDone
                        Sender.Tell(Done.Instance); // reply to ask
                        return Stay();

                    case MemberDowned memberDowned when e.StateData is OldestData od &&
                                                        memberDowned.Member.UniqueAddress.Equals(
                                                            _cluster.SelfUniqueAddress):
                        if (od.Singleton == null)
                        {
                            Log.Info("Self downed, stopping ClusterSingletonManager");
                            return Stop();
                        }

                        Log.Info("Self downed, stopping");
                        return GoToStopping(od.Singleton);

                    case LeaseLost ll when e.StateData is OldestData od2:
                        Log.Warning(ll.Reason,
                            "Lease has been lost. Terminating singleton and trying to re-acquire lease");
                        if (od2.Singleton != null)
                        {
                            od2.Singleton.Tell(_terminationMessage);
                            return GoTo(ClusterSingletonState.AcquiringLease)
                                .Using(new AcquiringLeaseData(false, od2.Singleton));
                        }

                        return TryAcquireLease();

                    case HandOverDone:
                        // no-op, the HandOverDone message can be sent multiple times if HandOverToMe
                        // was sent multiple times (retried)
                        // https://github.com/akka/akka/pull/29216/files#r440062592
                        return Stay();

                    default:
                        return null;
                }
            });

            When(ClusterSingletonState.WasOldest, e =>
            {
                switch (e.FsmEvent)
                {
                    case TakeOverRetry takeOverRetry when e.StateData is WasOldestData wasOldestData:
                    {
                        if ((_cluster.IsTerminated || _selfExited)
                            && (wasOldestData.NewOldest == null || takeOverRetry.Count > _maxTakeOverRetries))
                        {
                            return wasOldestData.Singleton != null ? GoToStopping(wasOldestData.Singleton) : Stop();
                        }

                        if (takeOverRetry.Count <= _maxTakeOverRetries)
                        {
                            if (_maxTakeOverRetries - takeOverRetry.Count <= 3)
                                Log.Info("Retry [{0}], sending TakeOverFromMe to [{1}]", takeOverRetry.Count,
                                    wasOldestData.NewOldest?.Address);
                            else
                                Log.Debug("Retry [{0}], sending TakeOverFromMe to [{1}]", takeOverRetry.Count,
                                    wasOldestData.NewOldest?.Address);

                            if (wasOldestData.NewOldest != null)
                                Peer(wasOldestData.NewOldest.Address).Tell(TakeOverFromMe.Instance);

                            SetTimer(TakeOverRetryTimer, new TakeOverRetry(takeOverRetry.Count + 1),
                                _settings.HandOverRetryInterval, false);
                            return Stay();
                        }

                        throw new ClusterSingletonManagerIsStuckException(
                            $"Expected hand-over to [{wasOldestData.NewOldest}] never occurred");
                    }
                    case HandOverToMe when e.StateData is WasOldestData w:
                        return GoToHandingOver(w.Singleton, Sender);
                    case MemberRemoved removed
                        when removed.Member.UniqueAddress.Equals(_cluster.SelfUniqueAddress) && !_selfExited:
                        Log.Info("Self removed, stopping ClusterSingletonManager");
                        return Stop();
                    case MemberRemoved removed when e.StateData is WasOldestData data
                                                    && data.NewOldest != null
                                                    && !_selfExited
                                                    && removed.Member.UniqueAddress.Equals(data.NewOldest):
                        AddRemoved(removed.Member.UniqueAddress);
                        return GoToHandingOver(data.Singleton, null);
                    case MemberRemoved:
                        return Stay();
                    case Terminated t
                        when e.StateData is WasOldestData oldestData
                             && t.ActorRef.Equals(oldestData.Singleton):
                        Log.Info("Singleton actor [{0}] was terminated", oldestData.Singleton.Path);
                        return Stay().Using(new WasOldestData(null, oldestData.NewOldest));
                    case SelfExiting:
                        SelfMemberExited();
                        // complete _memberExitingProgress when HandOverDone
                        Sender.Tell(Done.Instance); // reply to ask
                        return Stay();
                    case MemberDowned memberDowned when e.StateData is WasOldestData od &&
                                                        memberDowned.Member.UniqueAddress.Equals(
                                                            _cluster.SelfUniqueAddress):
                    {
                        if (od.Singleton == null)
                        {
                            Log.Info("Self downed, stopping ClusterSingletonManager");
                            return Stop();
                        }

                        Log.Info("Self downed, stopping");
                        return GoToStopping(od.Singleton);
                    }
                    default:
                        return null;
                }
            });

            When(ClusterSingletonState.HandingOver, e =>
            {
                switch (e.FsmEvent)
                {
                    case Terminated terminated
                        when e.StateData is HandingOverData handingOverData
                             && terminated.ActorRef.Equals(handingOverData.Singleton):
                        return HandleHandOverDone(handingOverData.HandOverTo);
                    case HandOverToMe
                        when e.StateData is HandingOverData d
                             && Sender.Equals(d.HandOverTo):
                        // retry
                        Sender.Tell(HandOverInProgress.Instance);
                        return Stay();
                    case SelfExiting:
                        SelfMemberExited();
                        // complete _memberExitingProgress when HandOverDone
                        Sender.Tell(Done.Instance);
                        return Stay();
                    default:
                        return null;
                }
            });

            When(ClusterSingletonState.Stopping, e =>
            {
                if (e is { FsmEvent: Terminated terminated, StateData: StoppingData stoppingData }
                    && terminated.ActorRef.Equals(stoppingData.Singleton))
                {
                    Log.Info("Singleton actor [{0}] was terminated", stoppingData.Singleton.Path);
                    return Stop();
                }

                return null;
            });

            When(ClusterSingletonState.End, e =>
            {
                switch (e.FsmEvent)
                {
                    case MemberRemoved removed
                        when removed.Member.UniqueAddress.Equals(_cluster.SelfUniqueAddress):
                        Log.Info("Self removed, stopping ClusterSingletonManager");
                        return Stop();
                    case OldestChangedBuffer.OldestChanged or HandOverToMe:
                        // not interested anymore - waiting for removal
                        return Stay();
                    default:
                        return null;
                }
            });

            WhenUnhandled(e =>
            {
                switch (e.FsmEvent)
                {
                    case SelfExiting:
                        SelfMemberExited();
                        // complete _memberExitingProgress when HandOverDone
                        _memberExitingProgress.TrySetResult(Done.Instance);
                        Sender.Tell(Done.Instance); // reply to ask
                        return Stay();
                    case MemberRemoved removed
                        when removed.Member.UniqueAddress.Equals(_cluster.SelfUniqueAddress) && !_selfExited:
                        Log.Info("Self removed, stopping ClusterSingletonManager");
                        return Stop();
                    case MemberRemoved removed:
                    {
                        if (!_selfExited)
                            Log.Info("Member removed [{0}]", removed.Member.Address);

                        AddRemoved(removed.Member.UniqueAddress);
                        return Stay();
                    }
                    case DelayedMemberRemoved delayedMemberRemoved:
                    {
                        if (!_selfExited)
                            Log.Info("Member removed [{0}]", delayedMemberRemoved.Member.Address);

                        AddRemoved(delayedMemberRemoved.Member.UniqueAddress);
                        return Stay();
                    }
                    case TakeOverFromMe:
                        Log.Debug("Ignoring TakeOver request in [{0}] from [{1}].", StateName, Sender.Path.Address);
                        return Stay();
                    case Cleanup:
                        CleanupOverdueNotMemberAnyMore();
                        return Stay();
                    case MemberDowned memberDowned:
                    {
                        if (memberDowned.Member.UniqueAddress.Equals(_cluster.SelfUniqueAddress))
                            Log.Info("Self downed, waiting for removal");
                        return Stay();
                    }
                    case ReleaseLeaseFailure rlf:
                        Log.Error(
                            rlf.Failure,
                            "Failed to release lease. Singleton may not be able to run on another node until lease timeout occurs");
                        return Stay();
                    case ReleaseLeaseResult rlr:
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
                    default:
                        return null;
                }
            });

            OnTransition((from, to) =>
            {
                Log.Info("ClusterSingletonManager state change [{0} -> {1}] {2}", from, to, StateData.ToString());

                if (to == ClusterSingletonState.BecomingOldest)
                    SetTimer(HandOverRetryTimer, new HandOverRetry(1), _settings.HandOverRetryInterval);
                if (from == ClusterSingletonState.BecomingOldest) CancelTimer(HandOverRetryTimer);
                if (from == ClusterSingletonState.WasOldest) CancelTimer(TakeOverRetryTimer);

                if (from == ClusterSingletonState.AcquiringLease && to != ClusterSingletonState.Oldest)
                {
                    if (StateData is AcquiringLeaseData ald && ald.LeaseRequestInProgress)
                    {
                        Log.Info("Releasing lease as leaving AcquiringLease going to [{0}]", to);
                        if (_lease != null)
                        {
                            _lease.Release().ContinueWith(r =>
                            {
                                if (r.IsCanceled || r.IsFaulted)
                                    return (object)new ReleaseLeaseFailure(r.Exception ?? (Exception)new LeaseException("Failed to release lease"));
                                return new ReleaseLeaseResult(r.Result);
                            }).PipeTo(Self);
                        }
                    }
                }

                if (from == ClusterSingletonState.Oldest && _lease != null)
                {
                    Log.Info("Releasing lease as leaving Oldest");
                    _lease.Release().ContinueWith(r => new ReleaseLeaseResult(r.Result)).PipeTo(Self);
                }

                if (to is ClusterSingletonState.Younger or ClusterSingletonState.Oldest) GetNextOldestChanged();
                if (to is ClusterSingletonState.Younger or ClusterSingletonState.End)
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
                SetTimer("delayed-member-removed-" + member.UniqueAddress, new DelayedMemberRemoved(member),
                    _removalMargin, repeat: false);
            }
            else Self.Tell(new DelayedMemberRemoved(member));
        }
    }
}
