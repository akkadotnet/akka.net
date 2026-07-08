//-----------------------------------------------------------------------
// <copyright file="IFrequencySketch.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System.Collections.Generic;

namespace Akka.Remote.Artery.Compression
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// The frequency-estimation seam behind which a receiver counts how often it observes each
    /// inbound actor path / class manifest, so it can pick the "heavy hitters" worth advertising a
    /// compression table for. Ported (as a seam) from Apache Pekko's compression frequency estimators
    /// (Apache 2.0), which offers a <c>count-min-sketch</c> and the default aging
    /// <c>fast-frequency-sketch</c> (TinyLFU).
    ///
    /// <para>
    /// SCAFFOLD (feature/artery-ref-manifest-compression): only the simple bounded
    /// <see cref="BoundedFrequencySketch{T}"/> is shipped for the MVP (design.md Decision 6, Q4).
    /// Sketch fidelity affects only <b>which</b> values get compressed, never correctness, so Pekko's
    /// <c>FastFrequencySketch</c> is deferred and slots in behind this same interface later, selected
    /// by the <c>frequency-sketch-implementation</c> setting.
    /// </para>
    /// </summary>
    /// <typeparam name="T">The observed value type (an actor-path or manifest string in this port).</typeparam>
    internal interface IFrequencySketch<T> where T : class
    {
        /// <summary>
        /// Records <paramref name="count"/> additional observations of <paramref name="item"/> and
        /// returns its new estimated total count.
        /// </summary>
        long Add(T item, long count);

        /// <summary>The current estimated count for <paramref name="item"/>, or <c>0</c> if never observed.</summary>
        long EstimatedCount(T item);

        /// <summary>Forgets all counts (e.g. after a table is built and the window rolls over).</summary>
        void Reset();
    }

    /// <summary>
    /// INTERNAL API.
    ///
    /// The MVP frequency estimator (design.md Decision 6): an exact, bounded per-value counter. It
    /// keeps at most <see cref="Capacity"/> distinct values; when a new value arrives at capacity, the
    /// current lowest-count value is evicted to make room. This is deliberately simpler than Pekko's
    /// probabilistic sketches -- it trades a little memory and a little accuracy under churn for
    /// obvious, testable behavior. It is NOT thread-safe; the owner (per-origin inbound compression
    /// state, a later task) serializes access.
    /// </summary>
    /// <typeparam name="T">The observed value type.</typeparam>
    internal sealed class BoundedFrequencySketch<T> : IFrequencySketch<T> where T : class
    {
        /// <summary>Default distinct-value capacity -- generous headroom over the default 256-entry heavy-hitter set.</summary>
        public const int DefaultCapacity = 1024;

        private readonly Dictionary<T, long> _counts;

        public BoundedFrequencySketch(int capacity = DefaultCapacity)
        {
            if (capacity < 1)
                throw new System.ArgumentOutOfRangeException(nameof(capacity), capacity, "Frequency-sketch capacity must be at least 1.");

            Capacity = capacity;
            _counts = new Dictionary<T, long>(capacity);
        }

        /// <summary>Maximum number of distinct values tracked before the lowest-count value is evicted.</summary>
        public int Capacity { get; }

        /// <summary>Number of distinct values currently tracked.</summary>
        public int Count => _counts.Count;

        public long Add(T item, long count)
        {
            if (count <= 0)
                return EstimatedCount(item);

            if (_counts.TryGetValue(item, out var existing))
            {
                var updated = existing + count;
                _counts[item] = updated;
                return updated;
            }

            if (_counts.Count >= Capacity)
                EvictLowest();

            _counts[item] = count;
            return count;
        }

        public long EstimatedCount(T item) => _counts.TryGetValue(item, out var c) ? c : 0L;

        public void Reset() => _counts.Clear();

        private void EvictLowest()
        {
            T? lowestKey = null;
            var lowestCount = long.MaxValue;
            foreach (var kv in _counts)
            {
                if (kv.Value < lowestCount)
                {
                    lowestCount = kv.Value;
                    lowestKey = kv.Key;
                }
            }

            if (lowestKey is not null)
                _counts.Remove(lowestKey);
        }
    }
}
