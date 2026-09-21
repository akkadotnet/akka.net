// -----------------------------------------------------------------------
//  <copyright file="ExtensionsSpecs.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using static FluentAssertions.FluentActions;

namespace Akka.Hosting.Tests;

public class ExtensionsSpecs
{
    private readonly ITestOutputHelper _helper;

    public ExtensionsSpecs(ITestOutputHelper helper)
    {
        _helper = helper;
    }
    
    public async Task<IHost> StartHost(Action<AkkaConfigurationBuilder, IServiceProvider> testSetup)
    {
        var host = new HostBuilder()
            .ConfigureLogging(builder =>
            {
                builder.AddProvider(new XUnitLoggerProvider(_helper, LogLevel.Information));
            })
            .ConfigureServices(service =>
            {
                service.AddAkka("TestActorSystem", testSetup);
            }).Build();
        
        await host.StartAsync();
        return host;
    }

    [Fact(DisplayName = "WithExtensions should not override extensions declared in HOCON")]
    public async Task ShouldNotOverrideHocon()
    {
        using var host = await StartHost((builder, _) =>
        {
            builder.AddHocon("akka.extensions = [\"Akka.Hosting.Tests.ExtensionsSpecs+FakeExtensionOneProvider, Akka.Hosting.Tests\"]", HoconAddMode.Append);
            builder.WithExtensions(typeof(FakeExtensionTwoProvider));
        });

        var system = host.Services.GetRequiredService<ActorSystem>();
        system.TryGetExtension<FakeExtensionOne>(out _).Should().BeTrue();
        system.TryGetExtension<FakeExtensionTwo>(out _).Should().BeTrue();
    }
    
    [Fact(DisplayName = "WithExtensions should be able to be called multiple times")]
    public async Task CanBeCalledMultipleTimes()
    {
        using var host = await StartHost((builder, _) =>
        {
            builder.WithExtensions(typeof(FakeExtensionOneProvider));
            builder.WithExtensions(typeof(FakeExtensionTwoProvider));
        });

        var system = host.Services.GetRequiredService<ActorSystem>();
        system.TryGetExtension<FakeExtensionOne>(out _).Should().BeTrue();
        system.TryGetExtension<FakeExtensionTwo>(out _).Should().BeTrue();
    }

    [Fact(DisplayName = "WithExtensions with invalid type should throw")]
    public void InvalidTypeShouldThrow()
    {
        Invoking(() =>
        {
            var builder = new AkkaConfigurationBuilder(new ServiceCollection(), "mySystem");
            builder.WithExtensions(typeof(string));
        }).Should().Throw<ConfigurationException>();
    }

    [Fact(DisplayName = "WithExtension should not override extensions declared in HOCON")]
    public async Task WithExtensionShouldNotOverrideHocon()
    {
        using var host = await StartHost((builder, _) =>
        {
            builder.AddHocon("akka.extensions = [\"Akka.Hosting.Tests.ExtensionsSpecs+FakeExtensionOneProvider, Akka.Hosting.Tests\"]", HoconAddMode.Append);
            builder.WithExtension<FakeExtensionTwoProvider>();
        });

        var system = host.Services.GetRequiredService<ActorSystem>();
        system.TryGetExtension<FakeExtensionOne>(out _).Should().BeTrue();
        system.TryGetExtension<FakeExtensionTwo>(out _).Should().BeTrue();
    }
    
    [Fact(DisplayName = "WithExtension should be able to be called multiple times")]
    public async Task WithExtensionCanBeCalledMultipleTimes()
    {
        using var host = await StartHost((builder, _) =>
        {
            builder.WithExtension<FakeExtensionOneProvider>();
            builder.WithExtension<FakeExtensionTwoProvider>();
        });

        var system = host.Services.GetRequiredService<ActorSystem>();
        system.TryGetExtension<FakeExtensionOne>(out _).Should().BeTrue();
        system.TryGetExtension<FakeExtensionTwo>(out _).Should().BeTrue();
    }

    public class FakeExtensionOne: IExtension
    {
    }

    public class FakeExtensionOneProvider : ExtensionIdProvider<FakeExtensionOne>
    {
        public override FakeExtensionOne CreateExtension(ExtendedActorSystem system)
        {
            return new FakeExtensionOne();
        }
    }

    public class FakeExtensionTwo: IExtension
    {
    }

    public class FakeExtensionTwoProvider : ExtensionIdProvider<FakeExtensionTwo>
    {
        public override FakeExtensionTwo CreateExtension(ExtendedActorSystem system)
        {
            return new FakeExtensionTwo();
        }
    }
}

