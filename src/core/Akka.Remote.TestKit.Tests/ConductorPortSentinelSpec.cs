// -----------------------------------------------------------------------
//  <copyright file="ConductorPortSentinelSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.TestKit.Tests;

public class ConductorPortSentinelSpec
{
    [Theory(DisplayName = "Sentinel must survive a format then parse round trip")]
    [InlineData(1)]
    [InlineData(4711)]
    [InlineData(33365)]
    [InlineData(65535)]
    public void Sentinel_must_round_trip(int port)
    {
        var line = ConductorPortSentinel.Format(port);

        ConductorPortSentinel.TryParse(line, out var parsed).Should().BeTrue();
        parsed.Should().Be(port);
    }

    [Fact(DisplayName = "Sentinel must reject ports outside the TCP range")]
    public void Sentinel_must_reject_out_of_range_ports()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ConductorPortSentinel.Format(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ConductorPortSentinel.Format(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ConductorPortSentinel.Format(65536));
    }

    [Fact(DisplayName = "Sentinel must tolerate the trailing whitespace a console line can carry")]
    public void Sentinel_must_tolerate_surrounding_whitespace()
    {
        ConductorPortSentinel.TryParse("  " + ConductorPortSentinel.Format(4711) + " \t", out var parsed)
            .Should().BeTrue();
        parsed.Should().Be(4711);
    }

    [Theory(DisplayName = "Sentinel must not read a port out of a line that is not a sentinel")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Running specs for Akka.Cluster.Tests.MultiNode.dll")]
    [InlineData("[MULTINODE-CONDUCTOR-PORT]")]
    [InlineData("[MULTINODE-CONDUCTOR-PORT]not-a-number")]
    [InlineData("[MULTINODE-CONDUCTOR-PORT]4711abc")]
    [InlineData("[MULTINODE-CONDUCTOR-PORT]-4711")]
    [InlineData("[MULTINODE-CONDUCTOR-PORT] 4711")]
    [InlineData("[MULTINODE-CONDUCTOR-PORT]0")]
    [InlineData("[MULTINODE-CONDUCTOR-PORT]65536")]
    [InlineData("[MULTINODE-CONDUCTOR-PORT]99999999999999999999")]
    [InlineData("prefixed [MULTINODE-CONDUCTOR-PORT]4711")]
    [InlineData("[multinode-conductor-port]4711")]
    public void Sentinel_must_reject_malformed_lines(string line)
    {
        ConductorPortSentinel.TryParse(line, out var parsed).Should().BeFalse();
        parsed.Should().Be(0);
    }
}
