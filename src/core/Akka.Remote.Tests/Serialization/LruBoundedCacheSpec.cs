//-----------------------------------------------------------------------
// <copyright file="LruBoundedCacheSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Globalization;
using System.Linq;
using Akka.Remote.Serialization;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Serialization
{
    public class LruBoundedCacheSpec
    {
        private class TestCache : LruBoundedCache<string, string> {
            public TestCache(int capacity, int evictAgeThreshold, string hashSeed = "") : base(capacity, evictAgeThreshold)
            {
                _hashSeed = hashSeed;
            }

            private readonly string _hashSeed;
            private int _cntr = 0;

            protected override int Hash(string k)
            {
                return FastHash.OfStringFast(_hashSeed + k + _hashSeed);
            }

            protected override string Compute(string k)
            {
                var id = _cntr;
                _cntr += 1;
                return k + ":" + id;
            }

            protected override bool IsCacheable(string v)
            {
                return !v.StartsWith("#");
            }

            public int InternalProbeDistanceOf(int idealSlot, int actualSlot)
            {
                return ProbeDistanceOf(idealSlot, actualSlot);
            }

            public void ExpectComputed(string key, string value)
            {
                Get(key).Should().Be(null);
                GetOrCompute(key).Should().Be(value);
                Get(key).Should().Be(value);
            }

            public void ExpectCached(string key, string value)
            {
                Get(key).Should().Be(value);
                GetOrCompute(key).Should().Be(value);
                Get(key).Should().Be(value);
            }

            public void ExpectComputedOnly(string key, string value)
            {
                Get(key).Should().Be(null);
                GetOrCompute(key).Should().Be(value);
                Get(key).Should().Be(null);
            }
        }

        private sealed class BrokenHashFunctionTestCache : TestCache
        {
            public BrokenHashFunctionTestCache(int capacity, int evictAgeThreshold, string hashSeed = "") : base(capacity, evictAgeThreshold, hashSeed)
            {
            }

            protected override int Hash(string k)
            {
                return 0;
            }
        }

        [Fact]
        public void LruBoundedCache_must_work_in_the_happy_case()
        {
            var cache = new TestCache(4,4);

            cache.ExpectComputed("A", "A:0");
            cache.ExpectComputed("B", "B:1");
            cache.ExpectComputed("C", "C:2");
            cache.ExpectComputed("D", "D:3");

            cache.ExpectCached("A", "A:0");
            cache.ExpectCached("B", "B:1");
            cache.ExpectCached("C", "C:2");
            cache.ExpectCached("D", "D:3");
        }

        [Fact]
        public void LruBoundedCache_must_evict_oldest_when_full()
        {
            foreach (var i in Enumerable.Range(1, 10))
            {
                var seed = ThreadLocalRandom.Current.Next(1024);
                var cache = new TestCache(4,4, seed.ToString());

                cache.ExpectComputed("A", "A:0");
                cache.ExpectComputed("B", "B:1");
                cache.ExpectComputed("C", "C:2");
                cache.ExpectComputed("D", "D:3");
                cache.ExpectComputed("E", "E:4");

                cache.ExpectCached("B", "B:1");
                cache.ExpectCached("C", "C:2");
                cache.ExpectCached("D", "D:3");
                cache.ExpectCached("E", "E:4");

                cache.ExpectComputed("A", "A:5");
                cache.ExpectComputed("B", "B:6");
                cache.ExpectComputed("C", "C:7");
                cache.ExpectComputed("D", "D:8");
                cache.ExpectComputed("E", "E:9");

                cache.ExpectCached("B", "B:6");
                cache.ExpectCached("C", "C:7");
                cache.ExpectCached("D", "D:8");
                cache.ExpectCached("E", "E:9");
            }
        }

        [Fact]
        public void LruBoundedCache_must_work_with_low_quality_hash_function()
        {
            var cache = new BrokenHashFunctionTestCache(4,4);

            cache.ExpectComputed("A", "A:0");
            cache.ExpectComputed("B", "B:1");
            cache.ExpectComputed("C", "C:2");
            cache.ExpectComputed("D", "D:3");
            cache.ExpectComputed("E", "E:4");

            cache.ExpectCached("B", "B:1");
            cache.ExpectCached("C", "C:2");
            cache.ExpectCached("D", "D:3");
            cache.ExpectCached("E", "E:4");

            cache.ExpectComputed("A", "A:5");
            cache.ExpectComputed("B", "B:6");
            cache.ExpectComputed("C", "C:7");
            cache.ExpectComputed("D", "D:8");
            cache.ExpectComputed("E", "E:9");

            cache.ExpectCached("B", "B:6");
            cache.ExpectCached("C", "C:7");
            cache.ExpectCached("D", "D:8");
            cache.ExpectCached("E", "E:9");
        }

        [Fact]
        public void LruBoundedCache_must_calculate_probe_distance_correctly()
        {
            var cache = new TestCache(4,4);

            cache.InternalProbeDistanceOf(0, 0).Should().Be(0);
            cache.InternalProbeDistanceOf(0, 1).Should().Be(1);
            cache.InternalProbeDistanceOf(0, 2).Should().Be(2);
            cache.InternalProbeDistanceOf(0, 3).Should().Be(3);

            cache.InternalProbeDistanceOf(1, 1).Should().Be(0);
            cache.InternalProbeDistanceOf(1, 2).Should().Be(1);
            cache.InternalProbeDistanceOf(1, 3).Should().Be(2);
            cache.InternalProbeDistanceOf(1, 0).Should().Be(3);

            cache.InternalProbeDistanceOf(2, 2).Should().Be(0);
            cache.InternalProbeDistanceOf(2, 3).Should().Be(1);
            cache.InternalProbeDistanceOf(2, 0).Should().Be(2);
            cache.InternalProbeDistanceOf(2, 1).Should().Be(3);

            cache.InternalProbeDistanceOf(3, 3).Should().Be(0);
            cache.InternalProbeDistanceOf(3, 0).Should().Be(1);
            cache.InternalProbeDistanceOf(3, 1).Should().Be(2);
            cache.InternalProbeDistanceOf(3, 2).Should().Be(3);
        }

        [Fact]
        public void LruBoundedCache_must_work_with_lower_age_threshold()
        {
            foreach (var i in Enumerable.Range(1, 10))
            {
                var seed = ThreadLocalRandom.Current.Next(1024);
                var cache = new TestCache(4, 2, seed.ToString());

                cache.ExpectComputed("A", "A:0");
                cache.ExpectComputed("B", "B:1");
                cache.ExpectComputed("C", "C:2");
                cache.ExpectComputed("D", "D:3");
                cache.ExpectComputed("E", "E:4");

                cache.ExpectCached("D", "D:3");
                cache.ExpectCached("E", "E:4");

                cache.ExpectComputed("F", "F:5");
                cache.ExpectComputed("G", "G:6");
                cache.ExpectComputed("H", "H:7");
                cache.ExpectComputed("I", "I:8");
                cache.ExpectComputed("J", "J:9");

                cache.ExpectCached("I", "I:8");
                cache.ExpectCached("J", "J:9");
            }
        }

        [Fact]
        public void LruBoundedCache_must_not_cache_noncacheable_values()
        {
            var cache = new TestCache(4, 4);

            cache.ExpectComputedOnly("#A", "#A:0");
            cache.ExpectComputedOnly("#A", "#A:1");
            cache.ExpectComputedOnly("#A", "#A:2");
            cache.ExpectComputedOnly("#A", "#A:3");

            cache.ExpectComputed("A", "A:4");
            cache.ExpectComputed("B", "B:5");
            cache.ExpectComputed("C", "C:6");
            cache.ExpectComputed("D", "D:7");
            cache.ExpectComputed("E", "E:8");

            cache.ExpectComputedOnly("#A", "#A:9");
            cache.ExpectComputedOnly("#A", "#A:10");
            cache.ExpectComputedOnly("#A", "#A:11");
            cache.ExpectComputedOnly("#A", "#A:12");

            // Cacheable values are not affected
            cache.ExpectCached("B", "B:5");
            cache.ExpectCached("C", "C:6");
            cache.ExpectCached("D", "D:7");
            cache.ExpectCached("E", "E:8");
        }

        [Fact]
        public void LruBoundedCache_must_maintain_good_average_probe_distance()
        {
            foreach (var u in Enumerable.Range(1, 10))
            {
                var seed = ThreadLocalRandom.Current.Next(1024);

                // Cache emulating 60% fill rate
                var cache = new TestCache(1024, 600, seed.ToString());

                for (var i = 0; i < 10000; i++)
                    cache.GetOrCompute(ThreadLocalRandom.Current.NextDouble().ToString(CultureInfo.InvariantCulture));

                var stats = cache.Stats;

                // Have not seen lower than 890
                stats.Entries.Should().BeGreaterThan(750);

                // Have not seen higher than 1.8
                stats.AverageProbeDistance.Should().BeLessThan(2.5);

                // Have not seen higher than 15
                stats.MaxProbeDistance.Should().BeLessThan(25);
            }
        }
    }
}
