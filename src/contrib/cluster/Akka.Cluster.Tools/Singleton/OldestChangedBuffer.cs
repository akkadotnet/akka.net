//-----------------------------------------------------------------------
// <copyright file="OldestChangedBuffer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;

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

        [Serializable]
        public sealed class GetNext
        {
            public static readonly GetNext Instance = new GetNext();
            private GetNext() { }
        }

        [Serializable]
        public sealed class InitialOldestState
        {
            public readonly Address Oldest;
            public readonly bool SafeToBeOldest;

            public InitialOldestState(Address oldest, bool safeToBeOldest)
            {
                Oldest = oldest;
                SafeToBeOldest = safeToBeOldest;
            }
        }

        [Serializable]
        public sealed class OldestChanged
        {
            public readonly Address Oldest;

            public OldestChanged(Address oldest)
            {
                Oldest = oldest;
            }
        }

        #endregion

        public OldestChangedBuffer(string role)
        {
            _role = role;
        }

        private string _role;
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
                _changes = _changes.Enqueue(new OldestChanged(MemberAddressOrDefault(after)));
        }

        private bool MatchingRole(Member member)
        {
            return string.IsNullOrEmpty(_role) || member.HasRole(_role);
        }

        private Address MemberAddressOrDefault(Member member)
        {
            return (member == null) ? null : member.Address;
        }

        private void HandleInitial(ClusterEvent.CurrentClusterState state)
        {
            _membersByAge = state.Members
                .Where(m => (m.Status == MemberStatus.Up || m.Status == MemberStatus.Leaving) && MatchingRole(m))
                .ToImmutableSortedSet(MemberAgeOrdering.Descending);

            var safeToBeOldest = !state.Members.Any(m => m.Status == MemberStatus.Down || m.Status == MemberStatus.Exiting);
            var initial = new InitialOldestState(MemberAddressOrDefault(_membersByAge.FirstOrDefault()), safeToBeOldest);
            _changes = _changes.Enqueue(initial);
        }

        private void Add(Member member)
        {
            if (MatchingRole(member))
                TrackChanges(() => _membersByAge = _membersByAge.Add(member));
        }

        private void Remove(Member member)
        {
            if (MatchingRole(member))
                TrackChanges(() => _membersByAge = _membersByAge.Remove(member));
        }

        private void SendFirstChange()
        {
            object change;
            _changes = _changes.Dequeue(out change);
            Context.Parent.Tell(change);
        }

        protected override void PreStart()
        {
            _cluster.Subscribe(Self, new[] { typeof(ClusterEvent.IMemberEvent) });
        }

        protected override void PostStop()
        {
            _cluster.Unsubscribe(Self);
        }

        protected override void OnReceive(object message)
        {
            if (message is ClusterEvent.CurrentClusterState) HandleInitial((ClusterEvent.CurrentClusterState)message);
            else if (message is ClusterEvent.MemberUp) Add(((ClusterEvent.MemberUp)message).Member);
            else if (message is ClusterEvent.MemberExited || message is ClusterEvent.MemberRemoved) Remove(((ClusterEvent.IMemberEvent)(message)).Member);
            else if (message is GetNext && _changes.IsEmpty) Context.BecomeStacked(OnDeliverNext);
            else if (message is GetNext) SendFirstChange();
        }

        private void OnDeliverNext(object message)
        {
            if (message is ClusterEvent.CurrentClusterState)
            {
                HandleInitial((ClusterEvent.CurrentClusterState)message);
                SendFirstChange();
                Context.UnbecomeStacked();
            }
            else if (message is ClusterEvent.MemberUp)
            {
                var memberUp = (ClusterEvent.MemberUp)message;
                Add(memberUp.Member);
                if (!_changes.IsEmpty)
                {
                    SendFirstChange();
                    Context.UnbecomeStacked();
                }
            }
            else if (message is ClusterEvent.MemberExited || message is ClusterEvent.MemberRemoved)
            {
                var memberEvent = (ClusterEvent.IMemberEvent)message;
                Remove(memberEvent.Member);
                if (!_changes.IsEmpty)
                {
                    SendFirstChange();
                    Context.UnbecomeStacked();
                }
            }
        }
    }
}