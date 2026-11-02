// -----------------------------------------------------------------------
//  <copyright file="LoggerConfigBuilderSpecs.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using LogLevel = Akka.Event.LogLevel;

namespace Akka.Hosting.Tests.Logging;

public class LoggerConfigBuilderSpecs
{
    [Fact(DisplayName = "LoggerConfigBuilder should contain proper default configuration")]
    public async Task LoggerSetupDefaultValues()
    {
        var builder = new AkkaConfigurationBuilder(new ServiceCollection(), "test")
            .ConfigureLoggers(_ => { });

        builder.Configuration.HasValue.Should().BeFalse();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddAkka(nameof(LoggerConfigBuilderSpecs), b =>
                {
                    b.ConfigureLoggers(_ => { });
                });
            })
            .Build();

        await host.StartAsync();

        try
        {
            var config = host.Services.GetRequiredService<ActorSystem>().Settings.Config;
            config.GetString("akka.loglevel").Should().Be("INFO");
            config.GetBoolean("akka.log-config-on-start").Should().BeFalse();
            var loggers = config.GetStringList("akka.loggers");
            loggers.Count.Should().Be(1);
            loggers[0].Should().Contain("Akka.Event.DefaultLogger");
            config.GetString("akka.logger-formatter").Should().Contain("SemanticLogMessageFormatter");

            var debug = config.GetConfig("akka.actor.debug");
            debug.Should().NotBeNull();
            debug.GetBoolean("receive").Should().BeFalse();
            debug.GetBoolean("autoreceive").Should().BeFalse();
            debug.GetBoolean("lifecycle").Should().BeFalse();
            debug.GetBoolean("fsm").Should().BeFalse();
            debug.GetBoolean("event-stream").Should().BeFalse();
            debug.GetBoolean("unhandled").Should().BeFalse();
            debug.GetBoolean("router-misconfiguration").Should().BeFalse();
            debug.GetBoolean("log-timers").Should().BeFalse();
            
            config.GetInt("akka.log-dead-letters").Should().Be(10);
            config.GetBoolean("akka.log-dead-letters-during-shutdown").Should().BeFalse();
            config.GetTimeSpan("akka.log-dead-letters-suspend-duration").Should().Be(TimeSpan.FromMinutes(5));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }
    
    [Fact(DisplayName = "LoggerConfigBuilder should override config values")]
    public void LoggerSetupOverrideValues()
    {
        var builder = new AkkaConfigurationBuilder(new ServiceCollection(), "test")
            .ConfigureLoggers(setup =>
            {
                setup.LogLevel = LogLevel.WarningLevel;
                setup.LogConfigOnStart = true;
                setup.ClearLoggers();
                setup.DebugOptions = new DebugOptions
                {
                    Receive = true,
                    AutoReceive = true,
                    LifeCycle = true,
                    EventStream = true,
                    FiniteStateMachine = true,
                    Unhandled = true,
                    RouterMisconfiguration = true
                };
                setup.DeadLetterOptions = new DeadLetterOptions
                {
                    LogCount = 99,
                    LogDuringShutdown = false,
                    LogSuspendDuration = TimeSpan.Zero
                };
            });

        builder.Configuration.HasValue.Should().BeTrue();
        var config = builder.Configuration.Value;
        config.GetString("akka.loglevel").Should().Be("Warning");
        config.GetBoolean("akka.log-config-on-start").Should().BeTrue();
        var loggers = config.GetStringList("akka.loggers");
        loggers.Count.Should().Be(0);
        
        var debug = config.GetConfig("akka.actor.debug");
        debug.Should().NotBeNull();
        debug.GetBoolean("receive").Should().BeTrue();
        debug.GetBoolean("autoreceive").Should().BeTrue();
        debug.GetBoolean("lifecycle").Should().BeTrue();
        debug.GetBoolean("fsm").Should().BeTrue();
        debug.GetBoolean("event-stream").Should().BeTrue();
        debug.GetBoolean("unhandled").Should().BeTrue();
        debug.GetBoolean("router-misconfiguration").Should().BeTrue();
        
        config.GetInt("akka.log-dead-letters").Should().Be(99);
        config.GetBoolean("akka.log-dead-letters-during-shutdown").Should().BeFalse();
        config.GetString("akka.log-dead-letters-suspend-duration").Should().Be("infinite");
    }

    [Fact(DisplayName = "DeadLetterOptions should override log-dead-letters properly")]
    public void DeadLetterOptionsTest()
    {
        var cfg = (Config)new DeadLetterOptions
        {
            ShouldLog = TriStateValue.All
        }.ToString();
        cfg.GetBoolean("akka.log-dead-letters").Should().BeTrue();
        
        cfg = new DeadLetterOptions
        {
            ShouldLog = TriStateValue.None
        }.ToString();
        cfg.GetBoolean("akka.log-dead-letters").Should().BeFalse();
        
        cfg = new DeadLetterOptions
        {
            LogCount = 10
        }.ToString();
        cfg.GetInt("akka.log-dead-letters").Should().Be(10);

        cfg = new DeadLetterOptions().ToString();
        cfg.IsEmpty.Should().BeTrue();
    }

    [Fact(DisplayName = "WithLogFilter should populate the LogFilterBuilder property")]
    public void WithLogFilterPropertyTest()
    {
        var akkaBuilder = new AkkaConfigurationBuilder(new ServiceCollection(), "test");
        var loggerConfigBuilder = new LoggerConfigBuilder(akkaBuilder)
                .WithLogFilter(filterBuilder =>
                {
                    filterBuilder.ExcludeMessageContaining("Test");
                });
        loggerConfigBuilder.LogFilterBuilder.Should().NotBeNull();
        var filterSetup = loggerConfigBuilder.LogFilterBuilder!.Build();
        filterSetup.Filters.Length.Should().Be(1);
        filterSetup.Filters.Any(f => f is RegexLogMessageFilter).Should().BeTrue();
    }
    
    [Fact(DisplayName = "WithLogFilter should append existing LogFilterBuilder property")]
    public void WithLogFilterConcatTest()
    {
        var akkaBuilder = new AkkaConfigurationBuilder(new ServiceCollection(), "test");
        var loggerConfigBuilder = new LoggerConfigBuilder(akkaBuilder)
        {
            LogFilterBuilder = new LogFilterBuilder()
                .ExcludeSourceContaining("Test")
        };
        loggerConfigBuilder
            .WithLogFilter(filterBuilder =>
            {
                filterBuilder.ExcludeMessageContaining("Test");
            });
        
        loggerConfigBuilder.LogFilterBuilder.Should().NotBeNull();
        var filterSetup = loggerConfigBuilder.LogFilterBuilder.Build();
        filterSetup.Filters.Length.Should().Be(2);
        filterSetup.Filters.Any(f => f is RegexLogMessageFilter).Should().BeTrue();
        filterSetup.Filters.Any(f => f is RegexLogSourceFilter).Should().BeTrue();
    }
    
}