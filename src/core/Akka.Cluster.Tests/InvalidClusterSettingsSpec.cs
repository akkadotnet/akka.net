// -----------------------------------------------------------------------
//  <copyright file="InvalidClusterSettingsSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static FluentAssertions.FluentActions;

namespace Akka.Cluster.Tests
{
    public class InvalidClusterSettingsSpec : AkkaSpec
    {
        public InvalidClusterSettingsSpec(ITestOutputHelper output) : base(output)
        {
        }

        [Fact(DisplayName = "Cluster started with invalid actor provider should raise a user friendly exception")]
        public void InvalidActorProviderTest()
        {
            Invoking(() => Cluster.Get(Sys)) // throws here
                .Should().ThrowExactly<ConfigurationException>()
                .WithMessage("*Did you forgot*");
        }
    }
}