// -----------------------------------------------------------------------
//  <copyright file="InvalidSettingsSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using static FluentAssertions.FluentActions;

namespace Akka.Cluster.Sharding.Tests
{
    public class InvalidSettingsSpec: AkkaSpec
    {
        [Fact(DisplayName = "ClusterSharding started with invalid actor provider should raise a user friendly exception")]
        public void InvalidActorProviderTest()
        {
            Invoking(() => ClusterSharding.Get(Sys)) // throws here
                .Should().ThrowExactly<ConfigurationException>()
                .WithMessage("*Did you forgot*");
        }
    }
}