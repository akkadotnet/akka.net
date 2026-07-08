//-----------------------------------------------------------------------
// <copyright file="FrequencySketchSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using Akka.Remote.Artery.Compression;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Unit tests for the MVP heavy-hitter detector (design.md Decision 6, Q4-approved): the bounded
    /// count-based <see cref="BoundedFrequencySketch{T}"/> behind the <see cref="IFrequencySketch{T}"/>
    /// seam, and the bounded top-N <see cref="TopHeavyHitters{T}"/>. Fidelity affects only WHICH values
    /// get compressed, never correctness -- these tests pin the simple, deterministic behavior the port
    /// ships before Pekko's <c>FastFrequencySketch</c> slots in behind the same seam.
    /// </summary>
    public class FrequencySketchSpec
    {
        // ===================== BoundedFrequencySketch =====================

        [Fact(DisplayName = "BoundedFrequencySketch should accumulate counts and report estimates")]
        public void Sketch_accumulates_counts()
        {
            var sketch = new BoundedFrequencySketch<string>(capacity: 8);

            sketch.Add("x", 1).Should().Be(1);
            sketch.Add("x", 2).Should().Be(3);
            sketch.EstimatedCount("x").Should().Be(3);
            sketch.EstimatedCount("never-seen").Should().Be(0);
        }

        [Fact(DisplayName = "BoundedFrequencySketch should ignore non-positive increments")]
        public void Sketch_ignores_non_positive()
        {
            var sketch = new BoundedFrequencySketch<string>(capacity: 8);
            sketch.Add("x", 5);

            sketch.Add("x", 0).Should().Be(5);
            sketch.Add("x", -3).Should().Be(5);
            sketch.EstimatedCount("x").Should().Be(5);
        }

        [Fact(DisplayName = "BoundedFrequencySketch should evict the lowest-count value when full")]
        public void Sketch_evicts_lowest_when_full()
        {
            var sketch = new BoundedFrequencySketch<string>(capacity: 2);
            sketch.Add("a", 5);
            sketch.Add("b", 3);   // lowest
            sketch.Add("c", 10);  // full -> evicts "b"

            sketch.Count.Should().Be(2);
            sketch.EstimatedCount("a").Should().Be(5);
            sketch.EstimatedCount("c").Should().Be(10);
            sketch.EstimatedCount("b").Should().Be(0, "the lowest-count value is evicted to make room");
        }

        [Fact(DisplayName = "BoundedFrequencySketch Reset should forget all counts")]
        public void Sketch_reset()
        {
            var sketch = new BoundedFrequencySketch<string>();
            sketch.Add("a", 4);
            sketch.Reset();

            sketch.Count.Should().Be(0);
            sketch.EstimatedCount("a").Should().Be(0);
        }

        [Fact(DisplayName = "BoundedFrequencySketch should reject a non-positive capacity")]
        public void Sketch_rejects_bad_capacity()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedFrequencySketch<string>(0));
        }

        // ===================== TopHeavyHitters =====================

        [Fact(DisplayName = "TopHeavyHitters default max should be 256")]
        public void HeavyHitters_default_max()
        {
            new TopHeavyHitters<string>().Max.Should().Be(256);
            TopHeavyHitters<string>.DefaultMax.Should().Be(256);
        }

        [Fact(DisplayName = "TopHeavyHitters should admit up to max entries")]
        public void HeavyHitters_admits_up_to_max()
        {
            var hitters = new TopHeavyHitters<string>(max: 3);

            hitters.Update("a", 1).Should().BeTrue();
            hitters.Update("b", 2).Should().BeTrue();
            hitters.Update("c", 3).Should().BeTrue();

            hitters.Count.Should().Be(3);
            hitters.Contains("a").Should().BeTrue();
            hitters.Contains("b").Should().BeTrue();
            hitters.Contains("c").Should().BeTrue();
        }

        [Fact(DisplayName = "TopHeavyHitters should evict the weakest member when a stronger newcomer arrives")]
        public void HeavyHitters_evicts_weakest()
        {
            var hitters = new TopHeavyHitters<string>(max: 3);
            hitters.Update("a", 1); // weakest
            hitters.Update("b", 2);
            hitters.Update("c", 3);

            hitters.Update("d", 4).Should().BeTrue("count 4 beats the weakest (1)");

            hitters.Count.Should().Be(3);
            hitters.Contains("a").Should().BeFalse("the weakest member is evicted");
            hitters.Contains("d").Should().BeTrue();
            hitters.Contains("b").Should().BeTrue();
            hitters.Contains("c").Should().BeTrue();
        }

        [Fact(DisplayName = "TopHeavyHitters should reject a newcomer that does not beat the weakest member")]
        public void HeavyHitters_rejects_weak_newcomer()
        {
            var hitters = new TopHeavyHitters<string>(max: 2);
            hitters.Update("a", 5);
            hitters.Update("b", 4);

            hitters.Update("weak", 1).Should().BeFalse("count 1 does not beat the weakest (4)");
            hitters.Contains("weak").Should().BeFalse();
            hitters.Count.Should().Be(2);
        }

        [Fact(DisplayName = "TopHeavyHitters should refresh the count of an already-tracked value")]
        public void HeavyHitters_refreshes_existing()
        {
            var hitters = new TopHeavyHitters<string>(max: 2);
            hitters.Update("a", 1);
            hitters.Update("b", 2);

            // Refresh "a" to a strong count; a later weak-but-stronger-than-min newcomer must evict "b", not "a".
            hitters.Update("a", 100).Should().BeTrue();
            hitters.Update("c", 3).Should().BeTrue("3 beats the weakest (b=2)");

            hitters.Contains("a").Should().BeTrue("its refreshed count (100) keeps it in the set");
            hitters.Contains("b").Should().BeFalse();
            hitters.Contains("c").Should().BeTrue();
        }

        [Fact(DisplayName = "TopHeavyHitters with max 0 should admit nothing")]
        public void HeavyHitters_max_zero_admits_nothing()
        {
            var hitters = new TopHeavyHitters<string>(max: 0);

            hitters.Update("a", 100).Should().BeFalse();
            hitters.Count.Should().Be(0);
        }

        [Fact(DisplayName = "TopHeavyHitters Clear should empty the set")]
        public void HeavyHitters_clear()
        {
            var hitters = new TopHeavyHitters<string>(max: 4);
            hitters.Update("a", 1);
            hitters.Update("b", 2);

            hitters.Clear();
            hitters.Count.Should().Be(0);
            hitters.Contains("a").Should().BeFalse();
        }
    }
}
