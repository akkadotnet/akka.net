// -----------------------------------------------------------------------
//  <copyright file="ClusterMetricsView.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster.Metrics.Events;
using Akka.Cluster.Metrics.Serialization;
using Akka.Dispatch;

namespace Akka.Cluster.Metrics.Tests.Helpers;

/// <summary>
///     Current cluster metrics, updated periodically via event bus.
/// </summary>
public class ClusterMetricsView : IDisposable
{
    /// <summary>
    ///     Actor that subscribes to the cluster eventBus to update current read view state.
    /// </summary>
    private readonly IActorRef _eventBusListener;

    private readonly ClusterMetrics _extension;
    private readonly ExtendedActorSystem _system;

    public ClusterMetricsView(ExtendedActorSystem system)
    {
        _system = system;
        _extension = Metrics.ClusterMetrics.Get(system);

        _eventBusListener = system.SystemActorOf(
            Props.Create(() => new EventBusListenerActor(_extension, AppendMetricsChange))
                .WithDispatcher(Dispatchers.DefaultDispatcherId)
                .WithDeploy(Deploy.Local),
            "metrics-event-bus-listener");
    }

    /// <summary>
    ///     Current cluster metrics, updated periodically via event bus.
    /// </summary>
    public IImmutableSet<NodeMetrics> ClusterMetrics { get; private set; } = ImmutableHashSet<NodeMetrics>.Empty;

    /// <summary>
    ///     Collected cluster metrics history.
    /// </summary>
    public IImmutableList<IImmutableSet<NodeMetrics>> MetricsHistory { get; private set; } =
        ImmutableList<IImmutableSet<NodeMetrics>>.Empty;

    /// <inheritdoc />
    public void Dispose()
    {
        _eventBusListener.Tell(PoisonPill.Instance);
    }

    /// <summary>
    ///     Handles
    /// </summary>
    /// <param name="change"></param>
    private void AppendMetricsChange(ClusterMetricsChanged change)
    {
        ClusterMetrics = change.NodeMetrics;
        MetricsHistory = MetricsHistory.Add(change.NodeMetrics);
    }

    /// <summary>
    ///     Intrenal event bus listener actor for <see cref="ClusterMetricsView" />
    /// </summary>
    private class EventBusListenerActor : ActorBase
    {
        private readonly ClusterMetrics _extension;
        private readonly Action<ClusterMetricsChanged> _onMetricsChanged;

        public EventBusListenerActor(ClusterMetrics extension, Action<ClusterMetricsChanged> onMetricsChanged)
        {
            _extension = extension;
            _onMetricsChanged = onMetricsChanged;
        }

        /// <inheritdoc />
        protected override void PreStart()
        {
            base.PreStart();

            _extension.Subscribe(Self);
        }

        /// <inheritdoc />
        protected override void PostStop()
        {
            base.PostStop();

            _extension.Unsubscribe(Self);
        }

        /// <inheritdoc />
        protected override bool Receive(object message)
        {
            if (message is ClusterMetricsChanged changed) _onMetricsChanged(changed);

            return true;
        }
    }
}