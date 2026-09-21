using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Hosting.Logging;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using static Akka.Hosting.Tests.TestHelpers;

namespace Akka.Hosting.Tests.Logging;

public class LoggerConfigEnd2EndSpecs : Akka.TestKit.Xunit.TestKit
{
    private class CustomLoggingProvider : ILoggerProvider
    {
        private readonly TestLogger _logger;

        public bool Created { get; private set; }
        
        public CustomLoggingProvider(TestLogger logger)
        {
            _logger = logger;
        }

        public void Dispose()
        {
        }

        public ILogger CreateLogger(string categoryName)
        {
            if (categoryName.Contains(nameof(ActorSystem)))
            {
                Created = true;
                _logger.LogInformation("ActorSystem logger created.");
            }
            return _logger;
        }
    }

    private readonly ITestOutputHelper _output;
    private readonly TestLogger _logger;

    public LoggerConfigEnd2EndSpecs(ITestOutputHelper output)
    {
        _output = output;
        _logger = new TestLogger(output);
    }

    [Fact]
    public async Task Should_configure_LoggerFactoryLogger()
    {
        var loggingProvider = new CustomLoggingProvider(_logger);
        
        // arrange
        using var host = await StartHost(collection =>
        {
            collection.AddLogging(builder => { builder.AddProvider(loggingProvider); });

            collection.AddAkka("MySys", (builder, provider) =>
            {
                builder.ConfigureLoggers(configBuilder => { configBuilder.AddLogger<LoggerFactoryLogger>(); });
                builder.AddTestOutputLogger(_output);
            });
        });
        
        // Make sure that the logger has already been created
        await AwaitConditionAsync(() => Task.FromResult(loggingProvider.Created));
        var actorSystem = host.Services.GetRequiredService<ActorSystem>();

        // act
        _logger.StartRecording();
        actorSystem.Log.Info("foo");

        // assert
        await AwaitAssertAsync(() =>
            _logger.Infos.Where(c => c.Contains("foo")).Should().HaveCount(1));
    }

    [Fact]
    public async Task Should_ActorSystem_without_LoggerFactoryLogger()
    {
        // arrange
        using var host = await StartHost(collection =>
        {
            collection.AddAkka("MySys", (builder, provider) => { builder.AddTestOutputLogger(_output); });
        });

        Action getActorSystem = () =>
        {
            var actorSystem = host.Services.GetRequiredService<ActorSystem>();
        };


        // act
        getActorSystem.Should().NotThrow();
    }
}