// -----------------------------------------------------------------------
//  <copyright file="LogMessageFormatterSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
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

namespace Akka.Hosting.Tests.Logging;

public class LogMessageFormatterSpec
{
    private ITestOutputHelper _helper;

    public LogMessageFormatterSpec(ITestOutputHelper helper)
    {
        _helper = helper;
    }

    [Fact(DisplayName = "ILogMessageFormatter should transform log messages")]
    public async Task TransformMessagesTest()
    {
        using var host = await SetupHost<TestLogMessageFormatter>();

        try
        {
            var sys = host.Services.GetRequiredService<ActorSystem>();
            var testKit = new Akka.TestKit.Xunit.TestKit(sys);

            var probe = testKit.CreateTestProbe();
            sys.EventStream.Subscribe(probe, typeof(Error));
            sys.Log.Error("This is a test {0}", 1);

            var msg = probe.ExpectMsg<Error>();
            msg.Message.Should().BeAssignableTo<LogMessage>();
            msg.ToString().Should().Contain("++TestLogMessageFormatter++");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact(DisplayName = "Invalid LogMessageFormatter property should throw")]
    public async Task InvalidLogMessageFormatterThrowsTest()
    {
        await Awaiting(async () => await SetupHost<InvalidLogMessageFormatter>())
            .Should().ThrowAsync<ConfigurationException>().WithMessage("*must have an empty constructor*");
    }

    [Fact(DisplayName = "SemanticLogMessageFormatter should be accepted (GitHub issue #703)")]
    public async Task SemanticLogMessageFormatterShouldBeAcceptedTest()
    {
        // SemanticLogMessageFormatter has a private constructor - verify it doesn't throw
#pragma warning disable CS0618
        using var host = await SetupHost<SemanticLogMessageFormatter>();
#pragma warning restore CS0618

        try
        {
            var sys = host.Services.GetRequiredService<ActorSystem>();
            sys.Settings.LogFormatter.Should().BeOfType<SemanticLogMessageFormatter>();
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact(DisplayName = "DefaultLogMessageFormatter should be accepted")]
    public async Task DefaultLogMessageFormatterShouldBeAcceptedTest()
    {
        // DefaultLogMessageFormatter has a private constructor - verify it doesn't throw
#pragma warning disable CS0618
        using var host = await SetupHost<DefaultLogMessageFormatter>();
#pragma warning restore CS0618

        try
        {
            var sys = host.Services.GetRequiredService<ActorSystem>();
            sys.Settings.LogFormatter.Should().BeOfType<DefaultLogMessageFormatter>();
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private async Task<IHost> SetupHost<TFormatter>() where TFormatter : ILogMessageFormatter
    {
        var host = new HostBuilder()
            .ConfigureLogging(builder =>
            {
                builder.AddProvider(new XUnitLoggerProvider(_helper, LogLevel.Information));
            })
            .ConfigureServices(collection =>
            {
                collection.AddAkka("TestSys", configurationBuilder =>
                {
                    configurationBuilder
                        .ConfigureLoggers(setup =>
                        {
                            setup.LogLevel = Event.LogLevel.DebugLevel;
                            setup.AddLoggerFactory();
#pragma warning disable CS0618
                            setup.WithDefaultLogMessageFormatter<TFormatter>();
#pragma warning restore CS0618
                        });
                });
            }).Build();
        await host.StartAsync();
        return host;
    }
}

public class TestLogMessageFormatter : ILogMessageFormatter
{
    public string Format(string format, params object[] args)
    {
        return string.Format($"++TestLogMessageFormatter++{format}", args);
    }

    public string Format(string format, IEnumerable<object> args)
        => Format(format, args.ToArray());
}

public class InvalidLogMessageFormatter : ILogMessageFormatter
{
    public InvalidLogMessageFormatter(string doesNotMatter)
    {
    }
    
    public string Format(string format, params object[] args)
    {
        throw new NotImplementedException();
    }

    public string Format(string format, IEnumerable<object> args)
    {
        throw new NotImplementedException();
    }
}