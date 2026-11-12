// -----------------------------------------------------------------------
// <copyright file="PacedBenchmark.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;

namespace RemotePingPong
{
    /// <summary>Fixed scheduled offers; generator delay remains visible in scheduled-to-echo latency.</summary>
    internal static class PacedBenchmark
    {
        internal const int MaximumSamples = 1_000_000;
        internal sealed record Run(Guid RunId, long Origin);
        internal sealed record Release(Guid RunId, int Index) : INotInfluenceReceiveTimeout;
        internal readonly record struct Sample(long DispatchDelay, long RoundTrip);
        internal sealed record Releases(long Origin, long[] Actual, int IntervalMs);
        internal sealed record Distribution(double P50Us, double P95Us, double P99Us, double MaxUs);
        internal sealed record Report(Guid PhaseId, int Clients, int MessagesPerClient, int Samples, int IntervalMs,
            double NominalRttsPerSecond, double ActualReleaseRttsPerSecond, double ReleaseSpanMilliseconds, Distribution ReleaseLateness,
            Distribution DispatchDelay, Distribution SendToEcho, Distribution ScheduledToEcho);

        internal static long IntervalTicks(int intervalMs) => checked(Stopwatch.Frequency * intervalMs / 1000);

        internal static async Task<Releases> ReleaseAsync(IReadOnlyList<IActorRef> clients, Guid phaseId,
            long origin, long[] actual, int intervalMs, Task completed, Task<Exception> failed, CancellationToken cancellationToken)
        {
            if (clients.Count < 1 || origin <= 0 || actual.Length < 2 || intervalMs is < 1 or > 1000)
                throw new ArgumentException("Paced releases require clients, a monotonic origin, two offers and interval 1–1000 ms.");
            var step = IntervalTicks(intervalMs);
            for (var i = 0; i < actual.Length; i++)
            {
                var due = checked(origin + step * (i + 1L));
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (failed.IsCompleted)
                        throw await failed.ConfigureAwait(false);
                    if (completed.IsCompleted)
                    {
                        await completed.ConfigureAwait(false);
                        throw new InvalidOperationException("Clients completed before every planned offer was released.");
                    }
                    var now = Stopwatch.GetTimestamp();
                    if (now >= due)
                        break;
                    var delay = Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1, (due - now) * 1000.0 / Stopwatch.Frequency)), cancellationToken);
                    await Task.WhenAny(delay, completed, failed).ConfigureAwait(false);
                    if (delay.IsCanceled)
                        await delay.ConfigureAwait(false);
                }
                actual[i] = Stopwatch.GetTimestamp();
                var release = new Release(phaseId, i);
                foreach (var client in clients)
                    client.Tell(release);
            }
            return new Releases(origin, actual, intervalMs);
        }

        internal static Distribution Quantiles(long[] ticks)
        {
            if (ticks.Length == 0 || ticks.Any(value => value < 0))
                throw new ArgumentException("Latency distribution requires nonnegative samples.", nameof(ticks));
            Array.Sort(ticks);
            double At(double fraction) => ticks[(int)Math.Ceiling(fraction * ticks.Length) - 1] * 1_000_000.0 / Stopwatch.Frequency;
            return new Distribution(At(0.50), At(0.95), At(0.99), At(1));
        }

        internal static async Task<long[]> CompleteAsync(Task<long[]> completed, Task<Exception> failed, CancellationToken cancellationToken)
        {
            await Task.WhenAny(completed, failed).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (failed.IsCompleted)
                throw await failed.ConfigureAwait(false);
            return await completed.ConfigureAwait(false);
        }

        internal static Report Summarize(Guid phaseId, IReadOnlyList<ValidatedBenchmark.Outcome> outcomes, Releases releases)
        {
            var samples = outcomes.SelectMany(outcome => outcome.Samples).ToArray();
            var count = releases.Actual.Length;
            if (count < 2 || outcomes.Count < 1 || samples.Length != checked(outcomes.Count * count)
                || samples.Any(sample => sample.RoundTrip <= 0 || sample.DispatchDelay < 0 || sample.RoundTrip < sample.DispatchDelay)
                || releases.Actual[^1] <= releases.Actual[0])
                throw new InvalidOperationException("Incomplete or inconsistent paced samples/releases.");
            var step = IntervalTicks(releases.IntervalMs);
            var lateness = releases.Actual.Select((time, index) => checked(time - releases.Origin - step * (index + 1L))).ToArray();
            return new Report(phaseId, outcomes.Count, count, samples.Length, releases.IntervalMs,
                outcomes.Count * 1000.0 / releases.IntervalMs,
                outcomes.Count * (count - 1.0) * Stopwatch.Frequency / (releases.Actual[^1] - releases.Actual[0]),
                (releases.Actual[^1] - releases.Actual[0]) * 1000.0 / Stopwatch.Frequency,
                Quantiles(lateness), Quantiles(samples.Select(sample => sample.DispatchDelay).ToArray()),
                Quantiles(samples.Select(sample => sample.RoundTrip - sample.DispatchDelay).ToArray()),
                Quantiles(samples.Select(sample => sample.RoundTrip).ToArray()));
        }
    }
}
