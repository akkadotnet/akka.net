//-----------------------------------------------------------------------
// <copyright file="OldestChangedBuffer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
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
        /// Request to deliver one more event.
        /// </summary>
        [Serializable]
        public sealed class GetNext
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static GetNext Instance { get; } = new GetNext();
            private GetNext() { }
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class InitialOldestState
        {
            /// <summary>
            /// The first event, corresponding to CurrentClusterState.
            /// </summary>
            public List<UniqueAddress> Oldest { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public bool SafeToBeOldest { get; }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="oldest">TBD</param>
            /// <param name="safeToBeOldest">TBD</param>
            public InitialOldestState(List<UniqueAddress> oldest, bool safeToBeOldest)
            {
                Oldest = oldest;
                SafeToBeOldest = safeToBeOldest;
            }
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
        }

        #endregion

        private readonly CoordinatedShutdown _coordShutdown = CoordinatedShutdown.Get(Context.System);

        /// <summary>
        /// Creates a new instance of the <see cref="OldestChangedBuffer"/>.
        /// </summary>
        /// <param name="role">The role for which we're watching for membership changes.</param>
        public OldestChangedBuffer(string role)
        {
            _role = role;
            SetupCoordinatedShutdown();
        }

        /// <summary>
        /// It's a delicate difference between <see cref="CoordinatedShutdown.PhaseClusterExiting"/> and <see cref="ClusterEvent.MemberExited"/>.
        ///
        /// MemberExited event is published immediately (leader may have performed that transition on other node),
        /// and that will trigger run of <see cref="CoordinatedShutdown"/>, while PhaseClusterExiting will happen later.
        /// Using PhaseClusterExiting in the singleton because the graceful shutdown of sharding region
        /// should preferably complete before stopping the singleton sharding coordinator on same node.
        /// </summary>
        private void SetupCoordinatedShutdown()
        {
            var self = Self;
            _coordShutdown.AddTask(CoordinatedShutdown.PhaseClusterExiting, "singleton-exiting-1", () =>
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

        private readonly string _role;
        private ImmutableSortedSet<Member> _membersByAge = ImmutableSortedSet<Member>.Empty.WithComparer(MemberAgeOrdering.Descending);
        private ImmutableQueue<object> _changes = ImmutableQueue<object>.Empty;

        private readonly Cluster _cluster = Cluster.Get(Context.System);

        private void TrackChanges(Action block)
        {
            var before = _membersByAge.FirstOrDefault();
            block();
            var after = _membersByAge.FirstOrDefault();

            // todo: fix neq comparison
            if (!Equals(before, after))
                _changes = _changes.Enqueue(new OldestChanged(after?.UniqueAddress));
        }

        private bool MatchingRole(Member member)
        {
            return string.IsNullOrEmpty(_role) || member.HasRole(_role);
        }

        private void HandleInitial(ClusterEvent.CurrentClusterState state)
        {
            // all members except Joining and WeaklyUp
            _membersByAge = state.Members
                .Where(m => m.UpNumber != int.MaxValue && MatchingRole(m))
                .ToImmutableSortedSet(MemberAgeOrdering.Descending);

            // If there is some removal in progress of an older node it's not safe to immediately become oldest,
            // removal of younger nodes doesn't matter. Note that it can also be started via restart after
            // ClusterSingletonManagerIsStuck.
            var selfUpNumber = state.Members
                .Where(m => m.UniqueAddress == _cluster.SelfUniqueAddress)
                .Select(m => (int?)m.UpNumber)
                .FirstOrDefault() ?? int.MaxValue;

            var oldest = _membersByAge.TakeWhile(m => m.UpNumber <= selfUpNumber).ToList();
            var safeToBeOldest = !oldest.Any(m => m.Status == MemberStatus.Down || m.Status == MemberStatus.Exiting || m.Status == MemberStatus.Leaving);
            var initial = new InitialOldestState(oldest.Select(m => m.UniqueAddress).ToList(), safeToBeOldest);
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
                object change;
                _changes = _changes.Dequeue(out change);
                Context.Parent.Tell(change);
            }
        }

        /// <inheritdoc cref="ActorBase.PreStart"/>
        protected override void PreStart()
        {
            _cluster.Subscribe(Self, typeof(ClusterEvent.IMemberEvent));
        }

        /// <inheritdoc cref="ActorBase.PostStop"/>
        protected override void PostStop()
        {
            _cluster.Unsubscribe(Self);
        }

        /// <inheritdoc cref="UntypedActor.OnReceive"/>
        protected override void OnReceive(object message)
        {
            if (message is ClusterEvent.CurrentClusterState state) HandleInitial(state);
            else if (message is ClusterEvent.MemberUp up) Add(up.Member);
            else if (message is ClusterEvent.MemberRemoved removed) Remove(removed.Member);
            else if (message is ClusterEvent.MemberExited exited && exited.Member.UniqueAddress != _cluster.SelfUniqueAddress)
                Remove(exited.Member);
            else if (message is SelfExiting)
            {
                Remove(_cluster.ReadView.Self);
                Sender.Tell(Done.Instance); // reply to ask
            }
            else if (message is GetNext && _changes.IsEmpty) Context.BecomeStacked(OnDeliverNext);
            else if (message is GetNext) SendFirstChange();
            else
            {
                Unhandled(message);
            }
        }

        /// <summary>
        /// The buffer was empty when GetNext was received, deliver next event immediately.
        /// </summary>
        /// <param name="message">The message to handle.</param>
        private void OnDeliverNext(object message)
        {
            if (message is ClusterEvent.CurrentClusterState state)
            {
                HandleInitial(state);
                SendFirstChange();
                Context.UnbecomeStacked();
            }
            else if (message is ClusterEvent.MemberUp up)
            {
                Add(up.Member);
                DeliverChanges();
            }
            else if (message is ClusterEvent.MemberRemoved removed)
            {
                Remove(removed.Member);
                DeliverChanges();
            }
            else if (message is ClusterEvent.MemberExited exited && exited.Member.UniqueAddress != _cluster.SelfUniqueAddress)
            {
                Remove(exited.Member);
                DeliverChanges();
            }
            else if (message is SelfExiting)
            {
                Remove(_cluster.ReadView.Self);
                DeliverChanges();
                Sender.Tell(Done.Instance); // reply to ask
            }
            else
            {
                Unhandled(message);
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
            if (message is ClusterEvent.IMemberEvent)
            {
                // ok, silence
            }
            else
            {
                base.Unhandled(message);
            }
        }
    }
}
