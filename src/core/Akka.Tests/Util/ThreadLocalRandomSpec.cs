//-----------------------------------------------------------------------
// <copyright file="ThreadLocalRandomSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
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
    public void Seed_base_should_come_from_a_nondeterministic_source()
    {
        // Environment.TickCount returns one stable value throughout a tight loop and therefore
        // gives simultaneously-started processes the same base. Multiple draws from the
        // cryptographic source must not collapse to that deterministic behavior. We avoid
        // asserting that every draw is unique: collisions are valid for a finite random domain.
        var seeds = Enumerable.Range(0, 32)
            .Select(_ => Akka.Util.ThreadLocalRandom.CreateSeed())
            .ToArray();

        Assert.True(seeds.Distinct().Skip(1).Any(),
            "the seed source returned the same value for all 32 draws");
    }
}
