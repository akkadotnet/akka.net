//-----------------------------------------------------------------------
// <copyright file="MauiAkkaHostingSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Maui.Hosting;
using Xunit;

namespace Akka.Hosting.Maui.Tests;

public sealed class MauiAkkaHostingSpecs
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private sealed class EchoActor : ReceiveActor
    {
        public EchoActor() => ReceiveAny(m => Sender.Tell(m));
    }

    private sealed class ProbeHostedService : IHostedService
    {
        public bool Started { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Started = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static void AddEcho(AkkaConfigurationBuilder builder) =>
        builder.WithActors((system, registry) => registry.Register<EchoActor>(system.ActorOf(Props.Create(() => new EchoActor()), "echo")));

    [Fact(DisplayName = "Should_NotStartHostedServices_When_HostIsMauiApp")]
    public async Task Should_NotStartHostedServices_When_HostIsMauiApp()
    {
        // Pins the reason AddAkka refuses to run in MAUI (https://github.com/dotnet/maui/issues/2244). If this
        // fails, MAUI started running IHostedService instances: AddAkka's guard and this package can be revisited.
        var builder = MauiApp.CreateBuilder();
        var probe = new ProbeHostedService();
        builder.Services.AddSingleton<IHostedService>(probe);

        await using var app = builder.Build();

        Assert.False(probe.Started);
    }

    [Fact(DisplayName = "Should_ThrowPlatformNotSupported_When_AddAkkaIsCalledInMauiApp")]
    public void Should_ThrowPlatformNotSupported_When_AddAkkaIsCalledInMauiApp()
    {
        // creating the builder loads Microsoft.Maui, which is what the guard looks for
        var builder = MauiApp.CreateBuilder();

        var ex = Assert.Throws<PlatformNotSupportedException>(() => builder.Services.AddAkka("guarded", AddEcho));
        Assert.Contains("AddAkkaMaui", ex.Message);
    }

    [Fact(DisplayName = "Should_StartActorSystemAndRunWithActors_When_MauiAppIsBuilt")]
    public async Task Should_StartActorSystemAndRunWithActors_When_MauiAppIsBuilt()
    {
        var builder = MauiApp.CreateBuilder();
        builder.Services.AddAkkaMaui("maui-specs", AddEcho);

        await using var app = builder.Build();

        // no await on GetAsync: Build() must have run the WithActors callback already
        var registry = app.Services.GetRequiredService<ActorRegistry>();
        Assert.True(registry.TryGet<EchoActor>(out var echo));

        var reply = await echo.Ask<string>("hello", Timeout, TestContext.Current.CancellationToken);
        Assert.Equal("hello", reply);

        var required = app.Services.GetRequiredService<IRequiredActor<EchoActor>>();
        Assert.Equal(echo, await required.GetAsync(TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Should_TerminateActorSystem_When_MauiAppIsDisposed")]
    public async Task Should_TerminateActorSystem_When_MauiAppIsDisposed()
    {
        var builder = MauiApp.CreateBuilder();
        builder.Services.AddAkkaMaui("maui-dispose", AddEcho);
        var app = builder.Build();
        var system = app.Services.GetRequiredService<ActorSystem>();

        await app.DisposeAsync();

        var terminated = await Task.WhenAny(system.WhenTerminated, Task.Delay(Timeout, TestContext.Current.CancellationToken));
        Assert.Same(system.WhenTerminated, terminated);
    }

    [Fact(DisplayName = "Should_ThrowFromBuild_When_ActorSystemFailsToStart")]
    public void Should_ThrowFromBuild_When_ActorSystemFailsToStart()
    {
        var builder = MauiApp.CreateBuilder();
        builder.Services.AddAkkaMaui("maui-failing", b =>
            b.WithActors((_, _) => throw new InvalidOperationException("startup failure")));

        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Equal("startup failure", ex.Message);
    }
}
