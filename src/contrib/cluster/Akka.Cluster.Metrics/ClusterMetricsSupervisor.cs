//-----------------------------------------------------------------------
// <copyright file="ClusterMetricsSupervisor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Annotations;
using Akka.Event;
using Akka.Util.Internal;

namespace Akka.Cluster.Metrics
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Actor providing customizable metrics collection supervision.
    /// </summary>
    [InternalApi]
    public class ClusterMetricsSupervisor : ActorBase
    {
        private readonly ClusterMetrics _metrics;
        private int _collectorInstance = 0;
        private ILoggingAdapter _log;
        
        private string CollectorName => $"collector-{_collectorInstance}";
        
        
        public ClusterMetricsSupervisor()
        {
            _metrics = ClusterMetrics.Get(Context.System);
            _log = Context.GetLogger();
        }

        /// <inheritdoc />
        protected override SupervisorStrategy SupervisorStrategy() => _metrics.Strategy;

        /// <inheritdoc />
        protected override void PreStart()
        {
            base.PreStart();

            if (_metrics.Settings.CollectorEnabled)
            {
                Self.Tell(ClusterMetricsSupervisorMetadata.CollectionStartMessage.Instance);
            }
            else
            {
                _log.Warning($"Metrics collection is disabled in configuration. " +
                           $"Use subtypes of {typeof(ClusterMetricsSupervisorMetadata.ICollectionControlMessage).FullName} to manage collection at runtime.");
            }
        }

        /// <inheritdoc />
        protected override bool Receive(object message)
        {
            switch (message)
            {
                case ClusterMetricsSupervisorMetadata.CollectionStartMessage start:
                    Context.GetChildren().ForEach(child => Context.Stop(child));
                    _collectorInstance++;
                    Context.ActorOf(Props.Create<ClusterMetricsCollector>(), CollectorName);
                    _log.Debug("Collection started");
                    return true;
                case ClusterMetricsSupervisorMetadata.CollectionStopMessage stop:
                    Context.GetChildren().ForEach(child => Context.Stop(child));
                    _log.Debug("Collection stopped");
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// INTERNAL API.
    /// 
    /// ClusterMetricsSupervisorMetadata
    /// </summary>
    [InternalApi]
    public static class ClusterMetricsSupervisorMetadata
    {
        /// <summary>
        /// Runtime collection management commands.
        /// </summary>
        public interface ICollectionControlMessage { }

        /// <summary>
        /// Command for <see cref="ClusterMetricsSupervisor"/> to start metrics collection.
        /// </summary>
        public sealed class CollectionStartMessage : ICollectionControlMessage
        {
            private CollectionStartMessage(){ }
            /// <summary>
            /// Singleton instance for 
            /// </summary>
            public static readonly CollectionStartMessage Instance = new CollectionStartMessage();
        }

        /// <summary>
        /// Command for <see cref="ClusterMetricsSupervisor"/> to stop metrics collection.
        /// </summary>
        public sealed class CollectionStopMessage : ICollectionControlMessage
        {
            private CollectionStopMessage(){ }
            /// <summary>
            /// Singleton instance for 
            /// </summary>
            public static readonly CollectionStopMessage Instance = new CollectionStopMessage();
        }
    }
}
