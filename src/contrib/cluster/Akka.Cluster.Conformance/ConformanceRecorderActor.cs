//-----------------------------------------------------------------------
// <copyright file="ConformanceRecorderActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Linq;
using Akka.Actor;

namespace Akka.Cluster.Conformance
{
    /// <summary>
    /// Runs on the reference seed node. Subscribes to both the low-level protocol recorder
    /// (<see cref="ClusterProtocolEvent"/> on the system event stream) and the high-level cluster
    /// membership event stream, and merges everything it receives — in arrival order — into a single
    /// <see cref="ConformanceTrace"/>. Because one actor is the sole writer, arrival order is a valid
    /// total order for the trace.
    /// </summary>
    internal sealed class ConformanceRecorderActor : UntypedActor
    {
        private readonly ConformanceTrace _trace;
        private readonly Cluster _cluster;

        public static Props Props(ConformanceTrace trace) =>
            Akka.Actor.Props.Create(() => new ConformanceRecorderActor(trace));

        public ConformanceRecorderActor(ConformanceTrace trace)
        {
            _trace = trace;
            _cluster = Cluster.Get(Context.System);
        }

        protected override void PreStart()
        {
            // Low-level wire protocol captured by the (modified) core recorder.
            Context.System.EventStream.Subscribe(Self, typeof(ClusterProtocolEvent));

            // High-level membership transitions, replayed from current state then live.
            _cluster.Subscribe(
                Self,
                ClusterEvent.SubscriptionInitialStateMode.InitialStateAsEvents,
                typeof(ClusterEvent.IMemberEvent),
                typeof(ClusterEvent.IReachabilityEvent),
                typeof(ClusterEvent.LeaderChanged));
        }

        protected override void PostStop()
        {
            Context.System.EventStream.Unsubscribe(Self, typeof(ClusterProtocolEvent));
            _cluster.Unsubscribe(Self);
        }

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case ClusterProtocolEvent pe:
                    _trace.Append(
                        ConformanceSource.Protocol,
                        pe.Direction == ClusterProtocolDirection.Inbound
                            ? ConformanceDirection.Inbound
                            : ConformanceDirection.Outbound,
                        pe.Kind,
                        pe.Peer,
                        pe.Detail);
                    break;

                case ClusterEvent.MemberRemoved removed:
                    _trace.Append(ConformanceSource.Membership, ConformanceDirection.None,
                        nameof(ClusterEvent.MemberRemoved), removed.Member.Address,
                        $"previousStatus={removed.PreviousStatus}");
                    break;

                case ClusterEvent.IMemberEvent me:
                    _trace.Append(ConformanceSource.Membership, ConformanceDirection.None,
                        me.GetType().Name, me.Member.Address,
                        $"status={me.Member.Status} roles=[{string.Join(",", me.Member.Roles)}]");
                    break;

                case ClusterEvent.ReachabilityEvent re:
                    _trace.Append(ConformanceSource.Membership, ConformanceDirection.None,
                        re.GetType().Name, re.Member.Address,
                        $"status={re.Member.Status}");
                    break;

                case ClusterEvent.LeaderChanged lc:
                    _trace.Append(ConformanceSource.Membership, ConformanceDirection.None,
                        nameof(ClusterEvent.LeaderChanged), lc.Leader,
                        lc.Leader is null ? "leader=none" : "leader=" + lc.Leader);
                    break;

                case ClusterEvent.CurrentClusterState:
                    // initial snapshot delivered alongside InitialStateAsEvents; not part of the trace
                    break;

                default:
                    Unhandled(message);
                    break;
            }
        }
    }
}
