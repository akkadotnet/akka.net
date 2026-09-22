//-----------------------------------------------------------------------
// <copyright file="DilatedTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Akka.Configuration;
using Xunit;
using Xunit.Sdk;
using FluentAssertions;
using static FluentAssertions.FluentActions;

namespace Akka.Hosting.TestKit.Tests.TestKitBaseTests;

public class DilatedTests : TestKit
{
    private const int TimeFactor = 4;
    private const int Timeout = 1000;
    private const int ExpectedTimeout = Timeout * TimeFactor;
    // Upper-bound slack for GC pauses and scheduling. This project runs its test collections in
    // parallel (see its xunit.runner.json), so a wall-clock measurement here sees more contention
    // than the equivalent spec in Akka.TestKit.Tests, which runs sequentially with a 1 s margin.
    private const int Margin = 2000;
    private const int DiffDelta = 100;

    protected override Config Config { get; } = $"akka.test.timefactor={TimeFactor}";

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
    }

    [Fact]
    public void Dilates_correctly_using_timeFactor()
    {
        Assert.Equal(Dilated(TimeSpan.FromMilliseconds(Timeout)), TimeSpan.FromMilliseconds(ExpectedTimeout));
    }

    [Fact]
    public async Task AwaitConditionAsync_should_dilate_timeout()
    {
        var stopwatch = Stopwatch.StartNew();
        await Awaiting(() => AwaitConditionAsync(() => Task.FromResult(false), TimeSpan.FromMilliseconds(Timeout)))
            .Should().ThrowAsync<FailException>();
        stopwatch.Stop();
        AssertDilated(stopwatch.ElapsedMilliseconds, $"Expected the timeout to be {ExpectedTimeout} but in fact it was {stopwatch.ElapsedMilliseconds}.");
    }

    [Fact]
    public async Task ReceiveNAsync_should_dilate_timeout()
    {
        var stopwatch = Stopwatch.StartNew();
        await Awaiting(async () => await ReceiveNAsync(42, TimeSpan.FromMilliseconds(Timeout)).ToListAsync())
            .Should().ThrowAsync<TrueException>();
        stopwatch.Stop();
        AssertDilated(stopwatch.ElapsedMilliseconds, $"Expected the timeout to be {ExpectedTimeout} but in fact it was {stopwatch.ElapsedMilliseconds}.");
    }

    [Fact]
    public async Task ExpectMsgAllOfAsync_should_dilate_timeout()
    {
        var stopwatch = Stopwatch.StartNew();
        await Awaiting(async () => await ExpectMsgAllOfAsync(TimeSpan.FromMilliseconds(Timeout), new[]{"1", "2"}).ToListAsync())
            .Should().ThrowAsync<TrueException>();
        stopwatch.Stop();
        AssertDilated(stopwatch.ElapsedMilliseconds, $"Expected the timeout to be {ExpectedTimeout} but in fact it was {stopwatch.ElapsedMilliseconds}.");
    }

    [Fact]
    public async Task FishForMessageAsync_should_dilate_timeout()
    {
        var stopwatch = Stopwatch.StartNew();
        await Awaiting(async () => await FishForMessageAsync(_=>false, TimeSpan.FromMilliseconds(Timeout)))
            .Should().ThrowAsync<TrueException>();
        stopwatch.Stop();
        AssertDilated(stopwatch.ElapsedMilliseconds, $"Expected the timeout to be {ExpectedTimeout} but in fact it was {stopwatch.ElapsedMilliseconds}.");
    }

    private static void AssertDilated(double diff, string? message = null)
    {
        Assert.True(diff >= ExpectedTimeout - DiffDelta, message);
        Assert.True(diff < ExpectedTimeout + Margin, message); // margin for GC
    }
}