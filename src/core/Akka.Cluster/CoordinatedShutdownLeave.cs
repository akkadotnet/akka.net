//-----------------------------------------------------------------------
// <copyright file="CoordinatedShutdownLeave.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------


using System.Linq;
using Akka.Actor;

namespace Akka.Cluster
{
    /// <summary>
    /// INTERNAL API
    ///
    /// Used for executing <see cref="CoordinatedShutdown"/> phases for graceful
    /// <see cref="Cluster.Leave"/> behaviors.
    /// </summary>
    internal sealed class CoordinatedShutdownLeave : ReceiveActor
    {
        /// <summary>
        /// A leave request for beginning the exit process from the <see cref="Cluster"/>
        /// </summary>
        public sealed class LeaveReq
        {
            private LeaveReq() { }

            /// <summary>
            /// Singleton instance.
            /// </summary>
            public static readonly LeaveReq Instance = new LeaveReq();
        }

        private readonly Cluster _cluster = Cluster.Get(Context.System);

        protected override void PostStop()
        {
            _cluster.Unsubscribe(Self);
        }

        public CoordinatedShutdownLeave()
        {
            Receive<LeaveReq>(req =>
            {
                // MemberRemoved is needed in case it was downed instead
                _cluster.Leave(_cluster.SelfAddress);
                _cluster.Subscribe(Self, typeof(ClusterEvent.MemberLeft), typeof(ClusterEvent.MemberRemoved), typeof(ClusterEvent.MemberDowned));
                var s = Sender;
                Become(() => WaitingLeaveCompleted(s));
            });
        }

        private void WaitingLeaveCompleted(IActorRef replyTo)
        {
            Receive<ClusterEvent.CurrentClusterState>(s =>
            {
                if (s.Members.IsEmpty)
                {
                    // not joined yet
                    replyTo.Tell(Done.Instance);
                    Context.Stop(Self);
                }
                else if (s.Members.Any(m => m.UniqueAddress.Equals(_cluster.SelfUniqueAddress)
                                       &&
                                       (m.Status == MemberStatus.Leaving || m.Status == MemberStatus.Exiting ||
                                        m.Status == MemberStatus.Down)))
                {
                    replyTo.Tell(Done.Instance);
                    Context.Stop(Self);
                }
            });

            Receive<ClusterEvent.IMemberEvent>(evt =>
            {
                if (evt.Member.UniqueAddress.Equals(_cluster.SelfUniqueAddress))
                {
                    replyTo.Tell(Done.Instance);
                    Context.Stop(Self);
                }
            });
        }
    }
}
