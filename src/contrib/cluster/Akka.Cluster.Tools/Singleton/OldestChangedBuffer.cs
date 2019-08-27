//-----------------------------------------------------------------------
// <copyright file="OldestChangedBuffer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Util.Internal;

namespace Akka.Cluster.Tools.Singleton
{
    /// <summary>
    /// Notifications of member events that track oldest member is tunneled
    /// via this actor (child of ClusterSingletonManager) to be able to deliver
    /// one change at a time. Avoiding simultaneous changes simplifies
    /// the process in ClusterSingletonManager. ClusterSingletonManager requests
    /// next event with <see cref="GetNext"/> when it is ready for it. Only one outstanding
    /// <see cref="GetNext"/> request is allowed. Incoming events are buffered and delivered
    /// upon <see cref="GetNext"/> request.
    /// </summary>
    internal sealed class OldestChangedBuffer : UntypedActor
    {
        #region Internal messages

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class GetNext
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static GetNext Instance { get; } = new GetNext();
            private GetNext() { }
            public override string ToString() => "GetNext";
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class InitialOldestState
        {
            /// <summary>
            /// TBD
            /// </summary>
            public UniqueAddress Oldest { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public bool SafeToBeOldest { get; }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="oldest">TBD</param>
            /// <param name="safeToBeOldest">TBD</param>
            public InitialOldestState(UniqueAddress oldest, bool safeToBeOldest)
            {
                Oldest = oldest;
                SafeToBeOldest = safeToBeOldest;
            }

            public override string ToString() => $"InitialOldestState(oldest:{Oldest}, safeToBeOldest:{SafeToBeOldest})";
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class OldestChanged
        {
            /// <summary>
            /// TBD
            /// </summary>
            public UniqueAddress Oldest { get; }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="oldest">TBD</param>
            public OldestChanged(UniqueAddress oldest)
            {
                Oldest = oldest;
            }

            public override string ToString() => $"OldestChanged(oldest:{Oldest})";
        }

        #endregion

        private readonly CoordinatedShutdown _coordShutdown = CoordinatedShutdown.Get(Context.System);

        private readonly string _role;
        private ImmutableSortedSet<Member> _membersByAge = ImmutableSortedSet<Member>.Empty.WithComparer(MemberAgeOrdering.Descending);
        private ImmutableQueue<object> _changes = ImmutableQueue<object>.Empty;

        private readonly Cluster _cluster = Cluster.Get(Context.System);
        private readonly string _selfDc;


        /// <summary>
        /// Creates a new instance of the <see cref="OldestChangedBuffer"/>.
        /// </summary>
        /// <param name="role">The role for which we're watching for membership changes.</param>
        public OldestChangedBuffer(string role)
        {
            _role = role;
            _selfDc = ClusterSettings.DcRolePrefix + _cluster.Settings.SelfDataCenter;
        }
        
        /// <inheritdoc cref="ActorBase.PreStart"/>
        protected override void PreStart()
        {
            _cluster.Subscribe(Self, typeof(ClusterEvent.IMemberEvent));

            // It's a delicate difference between CoordinatedShutdown.PhaseClusterExiting and MemberExited.
            // MemberExited event is published immediately (leader may have performed that transition on other node),
            // and that will trigger run of CoordinatedShutdown, while PhaseClusterExiting will happen later.
            // Using PhaseClusterExiting in the singleton because the graceful shutdown of sharding region
            // should preferably complete before stopping the singleton sharding coordinator on same node.
            var self = Self;
            _coordShutdown.AddTask(CoordinatedShutdown.PhaseClusterExiting, "singleton-exiting-1", (cancel) =>
            {
                if (_cluster.IsTerminated || _cluster.SelfMember.Status == MemberStatus.Down)
                    return Task.FromResult(Done.Instance);
                else
                    return self.Ask(SelfExiting.Instance, cancellationToken: cancel);
            });
        }

        /// <inheritdoc cref="ActorBase.PostStop"/>
        protected override void PostStop()
        {
            _cluster.Unsubscribe(Self);
        }

        private bool MatchingRole(Member member) => member.HasRole(_selfDc) && (string.IsNullOrEmpty(_role) || member.HasRole(_role));

        private void TrackChanges(Action block)
        {
            var before = _membersByAge.FirstOrDefault();
            block();
            var after = _membersByAge.FirstOrDefault();

            // todo: fix neq comparison
            if (!Equals(before, after))
                _changes = _changes.Enqueue(new OldestChanged(after?.UniqueAddress));
        }

        private void HandleInitial(ClusterEvent.CurrentClusterState state)
        {
            _membersByAge = state.Members
                .Where(m => (m.Status == MemberStatus.Up) && MatchingRole(m))
                .ToImmutableSortedSet(MemberAgeOrdering.Descending);
            // If there is some removal in progress of an older node it's not safe to immediately become oldest,
            // removal of younger nodes doesn't matter. Note that it can also be started via restart after
            // ClusterSingletonManagerIsStuck.

            int selfUpNumber = state.Members.Where(m => m.UniqueAddress == _cluster.SelfUniqueAddress).Select(m => (int?)m.UpNumber).FirstOrDefault() ?? int.MaxValue;

            var safeToBeOldest = !state.Members.Any(m => (m.UpNumber < selfUpNumber && MatchingRole(m)) && (m.Status == MemberStatus.Down || m.Status == MemberStatus.Exiting || m.Status == MemberStatus.Leaving));
            var initial = new InitialOldestState(_membersByAge.FirstOrDefault()?.UniqueAddress, safeToBeOldest);
            _changes = _changes.Enqueue(initial);
        }

        private void Add(Member member)
        {
            if (MatchingRole(member))
                TrackChanges(() =>
                {
                    // replace, it's possible that the upNumber is changed
                    _membersByAge = _membersByAge.Remove(member);
                    _membersByAge = _membersByAge.Add(member);
                });
        }

        private void Remove(Member member)
        {
            if (MatchingRole(member))
                TrackChanges(() => _membersByAge = _membersByAge.Remove(member));
        }

        private void SendFirstChange()
        {
            // don't send cluster change events if this node is shutting its self down, just wait for SelfExiting
            if (!_cluster.IsTerminated)
            {
                _changes = _changes.Dequeue(out var change);
                Context.Parent.Tell(change);
            }
        }

        /// <inheritdoc cref="UntypedActor.OnReceive"/>
        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case ClusterEvent.CurrentClusterState _:
                    HandleInitial((ClusterEvent.CurrentClusterState)message); break;
                case ClusterEvent.MemberUp _:
                    Add(((ClusterEvent.MemberUp)message).Member); break;
                case ClusterEvent.MemberRemoved _:
                    Remove(((ClusterEvent.MemberRemoved)message).Member); break;
                case ClusterEvent.MemberExited m when m.Member.UniqueAddress != _cluster.SelfUniqueAddress: 
                    Remove(m.Member); break;
                case SelfExiting _:
                    Remove(_cluster.ReadView.Self);
                    Sender.Tell(Done.Instance);
                    break;
                case GetNext _ when _changes.IsEmpty:
                    Context.BecomeStacked(OnDeliverNext);
                    break;
                case GetNext _:
                    SendFirstChange();
                    break;
                default:
                    Unhandled(message);
                    break;
            }
        }

        /// <summary>
        /// The buffer was empty when GetNext was received, deliver next event immediately.
        /// </summary>
        /// <param name="message">The message to handle.</param>
        private void OnDeliverNext(object message)
        {
            switch (message)
            {
                case ClusterEvent.CurrentClusterState _:
                    HandleInitial((ClusterEvent.CurrentClusterState)message);
                    SendFirstChange();
                    Context.UnbecomeStacked();
                    break;
                case ClusterEvent.MemberUp _:
                    Add(((ClusterEvent.MemberUp)message).Member);
                    DeliverChanges();
                    break;
                case ClusterEvent.MemberRemoved _:
                    Remove(((ClusterEvent.MemberRemoved)message).Member);
                    DeliverChanges();
                    break;
                case ClusterEvent.MemberExited m when m.Member.UniqueAddress != _cluster.SelfUniqueAddress:
                    Remove(m.Member);
                    DeliverChanges();
                    break;
                case SelfExiting _:
                    Remove(_cluster.ReadView.Self);
                    DeliverChanges();
                    Sender.Tell(Done.Instance); // reply to ask
                    break;
                default:
                    Unhandled(message);
                    break;
            }
        }

        private void DeliverChanges()
        {
            if (!_changes.IsEmpty)
            {
                SendFirstChange();
                Context.UnbecomeStacked();
            }
        }

        /// <inheritdoc cref="ActorBase.Unhandled"/>
        protected override void Unhandled(object message)
        {
            if (!(message is ClusterEvent.IMemberEvent)) base.Unhandled(message);
        }
    }
}