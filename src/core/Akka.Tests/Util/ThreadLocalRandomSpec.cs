//-----------------------------------------------------------------------
// <copyright file="ThreadLocalRandomSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Xunit;

namespace Akka.Tests.Util;

/// <summary>
/// Sanity checks for <see cref="Akka.Util.ThreadLocalRandom"/>.
/// </summary>
public class ThreadLocalRandomSpec
{
    [Fact]
    public void Current_should_return_a_usable_Random()
    {
        var random = Akka.Util.ThreadLocalRandom.Current;
        Assert.NotNull(random);

        // should not throw and should produce values in the expected range
        var value = random.Next(0, 100);
        Assert.InRange(value, 0, 99);
    }

    [Fact]
    public void Different_threads_should_draw_different_random_streams()
    {
        const int drawCount = 32;

        var first = new long[drawCount];
        var second = new long[drawCount];

        // Two dedicated (non-pooled) threads, released at the same time via the barrier, so each
        // one seeds its own ThreadLocal<Random> instance independently - mirroring the scenario
        // that motivated this fix (multiple processes/threads starting at ~the same moment).
        using var barrier = new Barrier(2);

        void Draw(long[] target)
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            var random = Akka.Util.ThreadLocalRandom.Current;
            for (var i = 0; i < drawCount; i++)
                target[i] = random.NextInt64();
        }

        var t1 = new Thread(() => Draw(first));
        var t2 = new Thread(() => Draw(second));

        t1.Start();
        t2.Start();

        Assert.True(t1.Join(TimeSpan.FromSeconds(10)));
        Assert.True(t2.Join(TimeSpan.FromSeconds(10)));

        // Deterministic-failure-free formulation: compare the whole drawn sequence rather than a
        // single value, so the odds of a false failure are astronomically small.
        Assert.NotEqual(first, second);
    }
}
