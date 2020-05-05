//-----------------------------------------------------------------------
// <copyright file="AllAtOnceEntityRecoveryStrategySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Sharding.Tests
{
    using EntityId = String;

    public class AllAtOnceEntityRecoveryStrategySpec : AkkaSpec
    {
        private readonly EntityRecoveryStrategy strategy = EntityRecoveryStrategy.AllStrategy;

        [Fact]
        public void AllAtOnceEntityRecoveryStrategy_must_recover_entities()
        {
            var entities = ImmutableHashSet.Create<EntityId>("1", "2", "3", "4", "5");

            var result = strategy.RecoverEntities(entities);
            result.Should().HaveCount(1);

            // the Task is completed immediately for allStrategy
            result.Head().Result.Should().BeSameAs(entities);
        }

        [Fact]
        public void AllAtOnceEntityRecoveryStrategy_must_no_recover_when_no_entities_to_recover()
        {
            var result = strategy.RecoverEntities(ImmutableHashSet<EntityId>.Empty);
            result.Should().BeEmpty();
        }
    }
}
