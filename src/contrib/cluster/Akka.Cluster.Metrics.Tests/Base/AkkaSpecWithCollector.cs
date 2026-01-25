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
            NodeMetrics metrics = null;

            // Give the collector extra time to initialize on first sample
            // The DefaultCollector needs time for CPU timing initialization
            var attemptCount = 0;
            var exceptionCount = 0;
            const int maxExceptionAttempts = 3;
            string lastDiagnostics = "no samples taken";

            do
            {
                cts.Token.ThrowIfCancellationRequested();

                try
                {
                    metrics = Collector.Sample();

                    // Collect diagnostics for debugging CI failures
                    var metricNames = string.Join(", ", metrics.Metrics.Select(m => $"{m.Name}={m.Value}"));
                    lastDiagnostics = $"Attempt {attemptCount}: [{metricNames}]";

                    if (HasRequiredMetrics(metrics.Metrics, requiredMetrics))
                    {
                        return metrics;
                    }
                }
                catch (Exception ex)
                {
                    // Log but continue - collector might need more time to initialize
                    // This handles platform-specific issues like process access on Linux
                    exceptionCount++;
                    lastDiagnostics = $"Attempt {attemptCount}: Exception - {ex.Message}";

                    if (exceptionCount >= maxExceptionAttempts)
                    {
                        throw new InvalidOperationException($"Metrics collector failed after {maxExceptionAttempts} consecutive exceptions. Last error: {ex.Message}", ex);
                    }

                    // Longer delay after exceptions to allow system to recover
                    await Task.Delay(1000, cts.Token);
                    attemptCount++;
                    continue;
                }

                // Reset exception count on successful sample
                exceptionCount = 0;

                // Progressive backoff: longer delays for later attempts
                var delayMs = attemptCount switch
                {
                    < 5 => 200,   // First few attempts: 200ms
                    < 15 => 500,  // Middle attempts: 500ms
                    _ => 1000     // Later attempts: 1000ms
                };

                attemptCount++;
                await Task.Delay(delayMs, cts.Token);

            } while (!cts.Token.IsCancellationRequested);

            // Provide detailed diagnostics for timeout failures
            var availableMetrics = string.Join(", ", metrics?.Metrics?.Select(m => m.Name) ?? new[] { "none" });
            throw new OperationCanceledException(
                $"Could not collect required metrics [{string.Join(", ", requiredMetrics)}] within {timeout}. " +
                $"Available metrics: [{availableMetrics}]. Attempts made: {attemptCount}. " +
                $"Last sample: {lastDiagnostics}");
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
