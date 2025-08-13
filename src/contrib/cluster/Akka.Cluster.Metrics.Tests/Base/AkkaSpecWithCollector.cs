//-----------------------------------------------------------------------
// <copyright file="AkkaSpecWithCollector.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Metrics.Collectors;
using Akka.Cluster.Metrics.Serialization;
using Akka.TestKit;
using FluentAssertions.Extensions;
using Xunit.Abstractions;

namespace Akka.Cluster.Metrics.Tests.Base
{
    /// <summary>
    /// Base class for specs that use <see cref="IMetricsCollector"/> property
    /// </summary>
    public abstract class AkkaSpecWithCollector : AkkaSpec
    {
        /// <summary>
        /// Collector used in specs
        /// </summary>
        protected IMetricsCollector Collector { get; }

        protected AkkaSpecWithCollector(string config, ITestOutputHelper output = null)
            : base(config, output)
        {
            Collector = new DefaultCollector(((ExtendedActorSystem)Sys).Provider.RootPath.Address);
        }
        
        // We need this because metrics can be missing from samples
        protected Queue<NodeMetrics> CreateTestData(int count, TimeSpan timeout, string[] requiredMetrics)
        {
            using var cts = new CancellationTokenSource(timeout);
            var queue = new Queue<NodeMetrics>();
            
            foreach (var _ in Enumerable.Range(0, count))
            {
                queue.Enqueue(CreateTestData(requiredMetrics, cts.Token));
            }

            return queue;
        }

        protected NodeMetrics CreateTestData(TimeSpan timeout, string[] requiredMetrics)
        {
            using var cts = new CancellationTokenSource(timeout);
            return CreateTestData(requiredMetrics, cts.Token);
        }
        
        protected NodeMetrics CreateTestData(string[] requiredMetrics, CancellationToken token)
        {
            NodeMetrics metrics;
            do
            {
                token.ThrowIfCancellationRequested();
                metrics = Collector.Sample();
            } while (!HasRequiredMetrics(metrics.Metrics, requiredMetrics));
            return metrics;
        }
        
        protected async Task<NodeMetrics> CreateTestDataAsync(TimeSpan timeout, string[] requiredMetrics)
        {
            using var cts = new CancellationTokenSource(timeout);
            NodeMetrics metrics;
            
            do
            {
                cts.Token.ThrowIfCancellationRequested();
                metrics = Collector.Sample();
                
                if (HasRequiredMetrics(metrics.Metrics, requiredMetrics))
                {
                    return metrics;
                }
                
                // Small delay between attempts to avoid tight loop
                await Task.Delay(100, cts.Token);
                
            } while (!cts.Token.IsCancellationRequested);
            
            throw new OperationCanceledException($"Could not collect required metrics {string.Join(", ", requiredMetrics)} within {timeout}");
        }

        private static bool HasRequiredMetrics(ImmutableHashSet<NodeMetrics.Types.Metric> metrics, string[] requiredMetrics)
        {
            foreach (var requiredMetric in requiredMetrics)
            {
                if (metrics.All(m => m.Name != requiredMetric))
                    return false;
            }

            return true;
        }
    }
}
