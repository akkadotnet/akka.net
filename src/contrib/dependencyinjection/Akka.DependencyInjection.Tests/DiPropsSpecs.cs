//-----------------------------------------------------------------------
// <copyright file="DiPropsSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Util.Internal;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Akka.DependencyInjection.Tests;

public class DiPropsSpecs : IAsyncLifetime
{
    private readonly IServiceProvider _serviceProvider;
    private readonly AkkaService _akkaService;
    private readonly ITestOutputHelper _output;
    private TestKit.Xunit2.TestKit _testKit;

    public DiPropsSpecs(ITestOutputHelper output)
    {
        _output = output;
        var services = new ServiceCollection()
            .AddSingleton<InjectedService>()
            .AddSingleton<AkkaService>()
            .AddHostedService<AkkaService>();

        _serviceProvider = services.BuildServiceProvider();
        _akkaService = _serviceProvider.GetRequiredService<AkkaService>();
    }
    
    
    [Fact(DisplayName = "DI should work with custom mailbox")]
    public async Task ShouldWorkWithCustomMailbox()
    {
        var system = _serviceProvider.GetRequiredService<AkkaService>().ActorSystem;
        var resolver = DependencyResolver.For(system);
        const string thing = "foobar";
        var props = resolver.Props<InjectedEchoActor>(thing).WithMailbox(AkkaService.CustomMailboxName);
        var actor = system.ActorOf(props, "testDIActorWithCustomMailbox");

        var probe = _testKit.CreateTestProbe(system);
        actor.Tell(1, probe);
        actor.Tell("test", probe);
        var result1 = await probe.ExpectMsgAsync<Message>();
        result1.Value.Should().Be("I was injected" + thing);
        var result2 = await probe.ExpectMsgAsync<Message>();
        result2.Value.Should().Be("I was injected" + thing);

        // Verify that the custom mailbox was used
        var actorRef = (RepointableActorRef)actor;
        actorRef.MailboxType.GetType().Should().Be(typeof(CustomMailbox));
    }

    public async Task InitializeAsync()
    {
        await _akkaService.StartAsync(default);
        _testKit = new TestKit.Xunit2.TestKit(_akkaService.ActorSystem, _output);
    }

    public async Task DisposeAsync()
    {
        await _akkaService.StopAsync();
    }
}
