//-----------------------------------------------------------------------
// <copyright file="PersistentShard.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
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
        public void ConstantRateEntityRecoveryStrategySpec_must_recover_entities()
        {
            var entities = ImmutableHashSet.Create<EntityId>("1", "2", "3", "4", "5");

            // TODO: https://github.com/akka/akka/blob/master/akka-cluster-sharding/src/test/scala/akka/cluster/sharding/ConstantRateEntityRecoveryStrategySpec.scala
        }

        [Fact]
        public void ConstantRateEntityRecoveryStrategySpec_must_no_recover_when_no_entities_to_recover()
        {
            var result = strategy.RecoverEntities(ImmutableHashSet<EntityId>.Empty);
            result.Should().BeEmpty();
        }
    }
}