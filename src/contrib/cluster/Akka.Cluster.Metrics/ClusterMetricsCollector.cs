//-----------------------------------------------------------------------
// <copyright file="ClusterMetricsCollector.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Akka.Actor;
using Akka.Annotations;
using Akka.Cluster.Metrics.Events;
using Akka.Cluster.Metrics.Serialization;
using Akka.Event;
using Akka.Util;
using Akka.Util.Internal;
using Address = Akka.Actor.Address;

namespace Akka.Cluster.Metrics
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Actor responsible for periodic data sampling in the node and publication to the cluster.
    /// </summary>
    [InternalApi]
    public class ClusterMetricsCollector : ActorBase
    {
        private readonly Cluster _cluster;

        private readonly ILoggingAdapter _log = Context.GetLogger();

        /// <summary>
        /// Metrics tick internal message
        /// </summary>
        sealed class MetricsTick
        { 
            private MetricsTick() { }
            public static readonly MetricsTick Instance = new MetricsTick();
        }

        /// <summary>
        /// Gossip tick internal message
        /// </summary>
        sealed class GossipTick
        {
            private GossipTick() { }
            public static readonly GossipTick Instance = new GossipTick();
        }
        
        /// <summary>
        /// The node ring gossipped that contains only members that are Up.
        /// </summary>
        private ImmutableSortedSet<Akka.Actor.Address> _nodes = ImmutableSortedSet<Akka.Actor.Address>.Empty;
        /// <summary>
        /// The latest metric values with their statistical data.
        /// </summary>
        private MetricsGossip _latestGossip = MetricsGossip.Empty;
        /// <summary>
        /// The metrics collector that samples data on the node.
        /// </summary>
        private readonly IMetricsCollector _collector;

        private readonly ICancelable _gossipTask;
        private readonly ICancelable _sampleTask;
        
        public ClusterMetricsCollector()
        {
            _cluster = Cluster.Get(Context.System);
            _collector = new MetricsCollectorBuilder().Build(Context.System);
            
            var metrics = ClusterMetrics.Get(Context.System);
            
            // Start periodic gossip to random nodes in cluster
            var gossipInitialDelay = metrics.Settings.PeriodicTasksInitialDelay.Max(metrics.Settings.CollectorGossipInterval);
            _gossipTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
                gossipInitialDelay, metrics.Settings.CollectorGossipInterval, Self, GossipTick.Instance, Self);
            
            // Start periodic metrics collection
            var sampleInitialDelay = metrics.Settings.PeriodicTasksInitialDelay.Max(metrics.Settings.CollectorSampleInterval);
            _sampleTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
                sampleInitialDelay, metrics.Settings.CollectorSampleInterval, Self, MetricsTick.Instance, Self);
        }

        /// <inheritdoc />
        protected override void PreStart()
        {
            base.PreStart();
            
            _cluster.Subscribe(Self, typeof(ClusterEvent.IMemberEvent), typeof(ClusterEvent.ReachabilityEvent));
            _log.Info("Metrics collection has started successfully");
        }

        /// <inheritdoc />
        protected override void PostStop()
        {
            base.PostStop();
            
            _cluster.Unsubscribe(Self);
            _gossipTask.Cancel();
            _sampleTask.Cancel();
            _collector.Dispose();
        }

        /// <inheritdoc />
        protected override bool Receive(object message)
        {
            switch (message)
            {
                case GossipTick _: Gossip(); return true;
                case MetricsTick _: Sample(); return true;
                case MetricsGossipEnvelope msg: ReceiveGossip(msg); return true;
                case ClusterEvent.CurrentClusterState state: ReceiveState(state); return true;
                case ClusterEvent.MemberUp m: AddMember(m.Member); return true;
                case ClusterEvent.MemberWeaklyUp m: AddMember(m.Member); return true;
                case ClusterEvent.MemberRemoved m: RemoveMember(m.Member); return true;
                case ClusterEvent.MemberExited m: RemoveMember(m.Member); return true;
                case ClusterEvent.UnreachableMember m: RemoveMember(m.Member); return true;
                case ClusterEvent.ReachableMember m: 
                    if (m.Member.Status == MemberStatus.Up || m.Member.Status == MemberStatus.WeaklyUp)
                        AddMember(m.Member);
                    return true;
                case object msg when msg is ClusterEvent.IMemberEvent:
                    return true; // not interested in other types of MemberEvent
            }

            return false;
        }

        /// <summary>
        /// Adds a member to the node ring.
        /// </summary>
        private void AddMember(Member member) => _nodes = _nodes.Add(member.Address);

        /// <summary>
        /// Removes a member from the member node ring.
        /// </summary>
        private void RemoveMember(Member member)
        {
            _nodes = _nodes.Remove(member.Address);
            _latestGossip = _latestGossip.Remove(member.Address);
            Publish();
        }

        /// <summary>
        /// Updates the initial node ring for those nodes that are <see cref="MemberStatus"/> `Up`.
        /// </summary>
        private void ReceiveState(ClusterEvent.CurrentClusterState state)
        {
            _nodes = state.Members.Except(state.Unreachable)
                .Where(m => m.Status == MemberStatus.Up || m.Status == MemberStatus.WeaklyUp)
                .Select(m => m.Address)
                .ToImmutableSortedSet();
        }

        /// <summary>
        /// Samples the latest metrics for the node, updates metrics statistics in
        /// <see cref="MetricsGossip"/>, and publishes the change to the event bus.
        ///
        /// <seealso cref="IMetricsCollector"/>
        /// </summary>
        private void Sample()
        {
            _latestGossip += _collector.Sample();
            Publish();
        }

        /// <summary>
        /// Receives changes from peer nodes, merges remote with local gossip nodes, then publishes
        /// changes to the event stream for load balancing router consumption, and gossip back.
        /// </summary>
        private void ReceiveGossip(MetricsGossipEnvelope envelope)
        {
            // remote node might not have same view of member nodes, this side should only care
            // about nodes that are known here, otherwise removed nodes can come back
            var otherGossip = envelope.Gossip.Filter(_nodes.Select(n => n).ToImmutableHashSet());
            _latestGossip = _latestGossip.Merge(otherGossip);
            
            // changes will be published in the period collect task
            if (!envelope.Reply)
                ReplyGossipTo(envelope.FromAddress);
        }

        /// <summary>
        /// Gossip to peer nodes.
        /// </summary>
        private void Gossip() => SelectRandomNode(_nodes.Remove(_cluster.SelfAddress).ToImmutableArray()).OnSuccess(GossipTo);
        
        private void GossipTo(Akka.Actor.Address address)
        {
            SendGossip(address, new MetricsGossipEnvelope(_cluster.SelfAddress, _latestGossip, reply: false));
        }

        private void ReplyGossipTo(Akka.Actor.Address address)
        {
            SendGossip(address, new MetricsGossipEnvelope(_cluster.SelfAddress, _latestGossip, reply: true));
        }

        private void SendGossip(Akka.Actor.Address address, MetricsGossipEnvelope envelope)
        {
            Context.ActorSelection(Self.Path.ToStringWithAddress(address)).Tell(envelope);
        }

        private Option<Address> SelectRandomNode(ImmutableArray<Address> addresses)
        {
            return addresses.IsEmpty ? Option<Address>.None : addresses[ThreadLocalRandom.Current.Next(addresses.Length)];
        }

        /// <summary>
        /// Publishes to the event stream.
        /// </summary>
        private void Publish()
        {
            Context.System.EventStream.Publish(new ClusterMetricsChanged(_latestGossip.Nodes.ToImmutableHashSet()));
        }
    }
}
