// -----------------------------------------------------------------------
//  <copyright file="TestConductorSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Remote.TestKit.Tests;

public class TestConductorSpec: Akka.TestKit.Xunit.TestKit
{
    public TestConductorSpec(ITestOutputHelper output): base("akka.actor.provider = remote", nameof(TestConductorSpec), output)
    {
    }

    [Fact(DisplayName = "TestConductor must initialize with not error")]
    public void InitializationTest()
    {
        var conductor = TestConductor.Get(Sys);
        conductor.Settings.BarrierTimeout.Should().Be(30.Seconds());
    }
}