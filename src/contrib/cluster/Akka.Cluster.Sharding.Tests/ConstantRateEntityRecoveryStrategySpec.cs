//-----------------------------------------------------------------------
// <copyright file="ConstantRateEntityRecoveryStrategySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Sharding.Tests
{
    using EntityId = String;

    public class ConstantRateEntityRecoveryStrategySpec : AkkaSpec
    {
        private readonly EntityRecoveryStrategy strategy;

        public ConstantRateEntityRecoveryStrategySpec()
        {
            strategy = EntityRecoveryStrategy.ConstantStrategy(Sys, TimeSpan.FromSeconds(1), 2);
        }

        [Fact]
        public void ConstantRateEntityRecoveryStrategy_must_recover_entities()
        {
            var entities = ImmutableHashSet.Create<EntityId>("1", "2", "3", "4", "5");
            var startTime = DateTime.UtcNow;
            var resultWithTimes = strategy.RecoverEntities(entities)
                .Select(scheduledRecovery => scheduledRecovery.ContinueWith(t => new KeyValuePair<IImmutableSet<string>, TimeSpan>(t.Result, DateTime.UtcNow - startTime)))
                .ToArray();

            var result = Task.WhenAll(resultWithTimes).Result.OrderBy(pair => pair.Value).ToArray();
            result.Length.Should().Be(3);

            var scheduledEntities = result.Select(pair => pair.Key).ToArray();
            scheduledEntities[0].Count.Should().Be(2);
            scheduledEntities[1].Count.Should().Be(2);
            scheduledEntities[2].Count.Should().Be(1);
            scheduledEntities.SelectMany(s => s).ToImmutableHashSet().Should().Equal(entities);

            var timesMillis = result.Select(pair => pair.Value.TotalMilliseconds).ToArray();

            // scheduling will not happen too early
            timesMillis[0].Should().BeApproximately(1400, 500);
            timesMillis[1].Should().BeApproximately(2400, 500);
            timesMillis[2].Should().BeApproximately(3400, 500);
        }

        [Fact]
        public void ConstantRateEntityRecoveryStrategy_must_no_recover_when_no_entities_to_recover()
        {
            var result = strategy.RecoverEntities(ImmutableHashSet<EntityId>.Empty);
            result.Should().BeEmpty();
        }
    }
}
