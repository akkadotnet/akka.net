// -----------------------------------------------------------------------
//  <copyright file="ConductorBindSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.TestKit.Tests;

public class ConductorBindSpec : Akka.TestKit.Xunit.TestKit
{
    /// <summary>
    /// Ceiling for the "port already in use" failure. Well under the 30s query timeout the old
    /// code waited out, so the assertion fails if the failure ever goes back to being a timeout.
    /// </summary>
    private static readonly TimeSpan FailFastCeiling = TimeSpan.FromSeconds(10);

    public ConductorBindSpec(ITestOutputHelper output)
        : base("akka.actor.provider = remote", nameof(ConductorBindSpec), output)
    {
    }

    [Fact(DisplayName = "StartController must bind an OS assigned port and report it before waiting for players")]
    public async Task StartController_must_report_the_port_it_bound()
    {
        var conductor = TestConductor.Get(Sys);
        IPEndPoint reported = null;

        // One participant: the conductor is its own player, so this completes without other nodes.
        var bound = await conductor.StartControllerAsync(
            participants: 1,
            name: new RoleName("conductor"),
            controllerPort: new IPEndPoint(IPAddress.Loopback, 0),
            onBound: endpoint => reported = endpoint);

        bound.Port.Should().NotBe(0, "port 0 means the OS picks a port, so the bound port must be a real one");
        reported.Should().NotBeNull("the runner needs the port before the conductor starts waiting for players");
        reported.Port.Should().Be(bound.Port);

        // The reported port is what goes over the wire to the other nodes.
        ConductorPortSentinel.TryParse(ConductorPortSentinel.Format(reported.Port), out var published)
            .Should().BeTrue();
        published.Should().Be(bound.Port);
    }

    [Fact(DisplayName = "StartController must bind the exact port it was given and still report it")]
    public async Task StartController_must_honour_an_explicit_port()
    {
        // Someone running node processes by hand sets multinode.server-port themselves. That path
        // must still bind that exact port, and must report it like any other.
        var free = FreeTcpPort();

        var conductor = TestConductor.Get(Sys);
        IPEndPoint reported = null;

        var bound = await conductor.StartControllerAsync(
            participants: 1,
            name: new RoleName("conductor"),
            controllerPort: new IPEndPoint(IPAddress.Loopback, free),
            onBound: endpoint => reported = endpoint);

        bound.Port.Should().Be(free);
        reported.Port.Should().Be(free);
    }

    /// <summary>
    /// Picks a port that is free right now. Only safe here because the test binds it immediately
    /// afterwards and does not care which port it gets, only that the conductor honours it.
    /// </summary>
    private static int FreeTcpPort()
    {
        using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.LocalEndPoint).Port;
    }

    [Fact(DisplayName = "StartController must fail fast and name the port when the conductor port is taken")]
    public async Task StartController_must_fail_fast_when_the_port_is_taken()
    {
        // Hold the port for the whole test so the conductor's bind cannot succeed.
        using var occupied = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        occupied.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        occupied.Listen(1);
        var taken = (IPEndPoint)occupied.LocalEndPoint;

        var conductor = TestConductor.Get(Sys);
        var stopwatch = Stopwatch.StartNew();

        var exception = await Assert.ThrowsAsync<ConductorBindException>(
            () => conductor.StartControllerAsync(participants: 2, name: new RoleName("conductor"), controllerPort: taken));

        stopwatch.Stop();

        exception.Message.Should().Contain($"conductor port {taken.Port} already in use");
        stopwatch.Elapsed.Should().BeLessThan(FailFastCeiling,
            "a bind failure must be reported straight away, not waited out as a query timeout");
    }
}
