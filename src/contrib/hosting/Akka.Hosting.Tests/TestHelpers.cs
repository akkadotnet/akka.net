using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit.Xunit.Internals;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Akka.Hosting.Tests;

public static class TestHelpers
{
    public static async Task<IHost> StartHost(Action<IServiceCollection> testSetup)
    {
        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<DiSanityCheckSpecs.IMySingletonInterface, DiSanityCheckSpecs.MySingletonImpl>();
                testSetup(services);
            }).Build();
        
        await host.StartAsync();
        return host;
    }

    public static AkkaConfigurationBuilder AddTestOutputLogger(this AkkaConfigurationBuilder builder,
        ITestOutputHelper output)
    {
        builder.WithActors((system, registry) =>
        {
            var extSystem = (ExtendedActorSystem)system;
            var logger = extSystem.SystemActorOf(Props.Create(() => new TestOutputLogger(output)), "log-test");
            logger.Tell(new InitializeLogger(system.EventStream));
        });
        
        return builder;
    }
}