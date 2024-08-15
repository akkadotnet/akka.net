// -----------------------------------------------------------------------
//  <copyright file="BugFixSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace Akka.DependencyInjection.Tests;

public class BugFixSpec : AkkaSpec, IAsyncLifetime
{
    private readonly AkkaService _akkaService;
    private readonly IServiceProvider _serviceProvider;

    public BugFixSpec(ITestOutputHelper output) : base(output)
    {
        var services = new ServiceCollection()
            .AddSingleton<AkkaService>()
            .AddHostedService<AkkaService>();

        _serviceProvider = services.BuildServiceProvider();
        _akkaService = _serviceProvider.GetRequiredService<AkkaService>();
    }


    public async Task InitializeAsync()
    {
        await _akkaService.StartAsync(default);
        InitializeLogger(_akkaService.ActorSystem);
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact(DisplayName = "DI should log an error if DI provider does not contain required parameter")]
    public void ShouldLogAnErrorIfParameterInjectionFailed()
    {
        var system = _serviceProvider.GetRequiredService<AkkaService>().ActorSystem;
        var probe = CreateTestProbe(system);
        system.EventStream.Subscribe(probe, typeof(Error));

        var props = DependencyResolver.For(system).Props<TestDiActor>();
        var actor = system.ActorOf(props.WithDeploy(Deploy.Local), "testDIActor");

        probe.ExpectMsg<Error>().Cause.Should().BeOfType<ActorInitializationException>();
    }

    protected override void AfterAll()
    {
        var sys = _serviceProvider.GetRequiredService<AkkaService>().ActorSystem;
        Shutdown(sys);
        base.AfterAll();
    }

    internal class TestDiActor : ReceiveActor
    {
        public TestDiActor(NotInServices doesNotExistInDi)
        {
        }
    }

    internal class NotInServices
    {
    }

    internal class AkkaService : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;

        public AkkaService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public ActorSystem ActorSystem { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var setup = DependencyResolverSetup.Create(_serviceProvider)
                .And(BootstrapSetup.Create().WithConfig(TestKitBase.DefaultConfig));

            ActorSystem = ActorSystem.Create("TestSystem", setup);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await ActorSystem.Terminate();
        }
    }
}