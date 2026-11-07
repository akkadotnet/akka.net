// -----------------------------------------------------------------------
//  <copyright file="LoggerFactoryLoggerSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Hosting.Logging;
using FluentAssertions;
using FluentAssertions.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Akka.Hosting.Tests.Logging;

public class LoggerFactoryLoggerSpec: IAsyncLifetime
{
    private readonly TestLogger _logger;
    private IHost? _host;
    private IActorRef? _echo;

    public LoggerFactoryLoggerSpec(ITestOutputHelper helper)
    {
        _logger = new TestLogger(helper);
    }

    public async ValueTask InitializeAsync()
    {
        _host = await SetupHost(_logger);
        var registry = _host.Services.GetRequiredService<ActorRegistry>();
        _echo = registry.Get<EchoActor>();
    }

    public ValueTask DisposeAsync()
    {
        _host?.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact(DisplayName = "LoggerFactoryLogger should log events")]
    public async Task LoggerShouldLogEvents()
    {
        await WaitUntilSilent(10.Seconds());
        
        _logger.StopWhenReceives(">>>> error");
        _logger.StartRecording();
        var reply = await _echo.Ask<string>(new Message(Event.LogLevel.DebugLevel, ">>>> debug"));
        reply.Should().Be(">>>> debug");
        
        reply = await _echo.Ask<string>(new Message(Event.LogLevel.InfoLevel, ">>>> info"));
        reply.Should().Be(">>>> info");
        
        reply = await _echo.Ask<string>(new Message(Event.LogLevel.WarningLevel, ">>>> warning"));
        reply.Should().Be(">>>> warning");
        
        reply = await _echo.Ask<string>(new Message(Event.LogLevel.ErrorLevel, ">>>> error"));
        reply.Should().Be(">>>> error");
        await WaitUntilLoggerStopsRecording(10.Seconds());

        _logger.TotalLogs.Should().BeGreaterThan(0);
        _logger.Debugs.Count(m => m.Contains(">>>> debug")).Should().Be(1);
        _logger.Infos.Count(m => m.Contains(">>>> info")).Should().Be(1);
        _logger.Warnings.Count(m => m.Contains(">>>> warning")).Should().Be(1);
        _logger.Errors.Count(m => m.Contains(">>>> error")).Should().Be(1);
    }

    [Fact(DisplayName = "LoggerFactoryLogger should log all events")]
    public async Task LoggerShouldLogAllEvents()
    {
        var rnd = new Random();
        var allLevels = new[]
        {
            Event.LogLevel.DebugLevel,
            Event.LogLevel.InfoLevel,
            Event.LogLevel.WarningLevel,
            Event.LogLevel.ErrorLevel,
        };
        
        await WaitUntilSilent(10.Seconds());
        
        _logger.StopWhenReceives(">>>> STOP");
        _logger.StartRecording();
        string reply;
        foreach (var i in Enumerable.Range(0, 500))
        {
            reply = await _echo.Ask<string>(new Message(allLevels[rnd.Next(0, 4)], $">>>> MESSAGE {i}"));
            reply.Should().Be($">>>> MESSAGE {i}");
        }
        
        reply = await _echo.Ask<string>(new Message(Event.LogLevel.InfoLevel, ">>>> STOP"));
        reply.Should().Be(">>>> STOP");
        await WaitUntilLoggerStopsRecording(10.Seconds());

        _logger.TotalLogs.Should().Be(501);
    }

    private async Task WaitUntilLoggerStopsRecording(TimeSpan timeout)
    {
        var cts = new CancellationTokenSource(timeout);
        try
        {
            while (_logger.Recording)
            {
                await Task.Delay(100, cts.Token);
                if (cts.IsCancellationRequested)
                    throw new TimeoutException($"Waiting too long for logger to stop recording. Timeout: {timeout}");
            }
        }
        finally
        {
            cts.Dispose();
        }
    }
    
    private async Task WaitUntilSilent(TimeSpan timeout)
    {
        var cts = new CancellationTokenSource(timeout);
        try
        {
            int previousCount;
            int count;
            do
            {
                previousCount = _logger.ReceivedLogs;
                await Task.Delay(200, cts.Token);
                if(cts.IsCancellationRequested)
                    throw new TimeoutException($"Waiting too long for ActorSystem logging system to be silent. Timeout: {timeout}");
                
                count = _logger.ReceivedLogs;
            } while (previousCount != count);
        }
        finally
        {
            cts.Dispose();
        }

    }
    
    private static async Task<IHost> SetupHost(TestLogger logger)
    {
        var host = new HostBuilder()
            .ConfigureServices(collection =>
            {
                collection.AddAkka("TestSys", configurationBuilder =>
                {
                    configurationBuilder
                        .ConfigureLoggers(setup =>
                        {
                            setup.LogLevel = Event.LogLevel.DebugLevel;
                            setup.ClearLoggers();
                            setup.AddLoggerFactory(new TestLoggerFactory(logger));
                        })
                        .WithActors((system, registry) =>
                        {
                            var echo = system.ActorOf(Props.Create(() => new EchoActor()), "echo");
                            registry.TryRegister<EchoActor>(echo); // register for DI
                        });
                });
            }).Build();
        await host.StartAsync();
        return host;
    }

    private class EchoActor: ReceiveActor
    {
        public EchoActor()
        {
            var log = Context.GetLogger();
            Receive<Message>(o =>
            {
                switch (o.LogLevel)
                {
                    case Event.LogLevel.DebugLevel:
                        log.Debug(o.Payload);
                        break;
                    case Event.LogLevel.InfoLevel:
                        log.Info(o.Payload);
                        break;
                    case Event.LogLevel.WarningLevel:
                        log.Warning(o.Payload);
                        break;
                    case Event.LogLevel.ErrorLevel:
                        log.Error(o.Payload);
                        break;
                }

                Sender.Tell(o.Payload);
            });
        }
    }
    
    private class Message
    {
        public Message(Event.LogLevel logLevel, string payload)
        {
            LogLevel = logLevel;
            Payload = payload;
        }

        public Event.LogLevel LogLevel { get; }
        public string Payload { get; }
    }

    private class TestLoggerFactory: ILoggerFactory
    {
        private readonly TestLogger _logger;

        public TestLoggerFactory(TestLogger logger)
        {
            _logger = logger;
        }

        public void Dispose()
        {
            // no-op
        }

        public ILogger CreateLogger(string categoryName) => _logger;

        public void AddProvider(ILoggerProvider provider)
        {
            // no-op
        }
    }
}