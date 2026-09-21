using System;
using System.Threading.Tasks;
using Akka.Actor;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;
using static FluentAssertions.FluentActions;

namespace Akka.Hosting.Tests;

public class StartFailureSpec
{
    private readonly ITestOutputHelper _output;
    
    public StartFailureSpec(ITestOutputHelper output)
    {
        _output = output;
    }
    
    [Fact]
    public async Task ShouldThrowWhenActorSystemFailedToStart()
    {
        // arrange
        var host = new HostBuilder()
            .ConfigureLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddProvider(new XUnitLoggerProvider(_output, LogLevel.Debug));
            })
            .ConfigureServices(services =>
            {
                services.AddAkka("MySys", (builder, provider) =>
                {
                    builder.AddStartup((_, _) => throw new TestException("BOOM"));
                });
            })
            .Build();

        await Awaiting(async () => await host.StartAsync()).Should()
            .ThrowExactlyAsync<TestException>().WithMessage("BOOM");
    }

    private class TestException: Exception
    {
        public TestException(string? message) : base(message)
        {
        }
    }
}