// -----------------------------------------------------------------------
//  <copyright file="SerilogLoggerEnd2EndSpecs.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using Akka.Event;
using Akka.Logger.Serilog;
using FluentAssertions;
using Serilog;
using Serilog.Core;
using Xunit;

namespace Akka.Hosting.Tests.Logging;

public class SerilogLoggerEnd2EndSpecs : TestKit.TestKit
{
    /// <inheritdoc />
    /// <summary>
    /// Basic concurrent sink implementation for testing the final output from Serilog
    /// </summary>
    public sealed class TestSink : ILogEventSink
    {
        public ConcurrentQueue<Serilog.Events.LogEvent> Writes { get; private set; } = new();

        private readonly ITestOutputHelper? _output;
        private int _count;

        public TestSink() : this(null)
        {
        }

        public TestSink(ITestOutputHelper? output)
        {
            _output = output;
        }


        /// <summary>
        /// Resets the contents of the queue
        /// </summary>
        public void Clear()
        {
            Writes.Clear();
        }

        public void Emit(Serilog.Events.LogEvent logEvent)
        {
            _count++;
            _output?.WriteLine($"[{nameof(TestSink)}][{_count}]: {logEvent.RenderMessage()}");
            Writes.Enqueue(logEvent);
        }
    }

    private readonly TestSink _sink = new();

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        Serilog.Log.Logger = new LoggerConfiguration()
            .WriteTo.Sink(_sink)
            .MinimumLevel.Information()
            .CreateLogger();

        builder.ConfigureLoggers(setup =>
        {
            setup.ClearLoggers();
            setup.AddLogger<SerilogLogger>();
            setup.LogLevel = Event.LogLevel.DebugLevel;
#pragma warning disable CS0618
            setup.WithDefaultLogMessageFormatter<SerilogLogMessageFormatter>();
#pragma warning restore CS0618
        });
    }

    [Theory]
    [InlineData(Event.LogLevel.DebugLevel, "test case {0}", new object[] { 1 })]
    [InlineData(Event.LogLevel.DebugLevel, "test case {myNum}", new object[] { 1 })]
    [InlineData(Event.LogLevel.InfoLevel, "test case {myNum} {myStr}", new object[] { 1, "foo" })]
    public void ShouldHandleSerilogFormats(LogLevel level, string formatStr, object[] args)
    {
        Sys.EventStream.Subscribe(TestActor, typeof(LogEvent));

        var logWrite = () =>
        {
            Sys.Log.Log(level, formatStr, args);

            var logEvent = ExpectMsg<LogEvent>();
            logEvent.LogLevel().Should().Be(level);
            logEvent.ToString().Should().NotBeEmpty();
        };

        logWrite.Should().NotThrow<FormatException>();
    }

    [Fact(Skip = "Does not work right now")]
    public void ShouldHaveEnrichedContext()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(LogEvent));

        var contextedLogger = Sys.Log.ForContext("TestContext", "Testy");
        _sink.Clear();
        AwaitCondition(() => _sink.Writes.IsEmpty);

        contextedLogger.Info("test case {0}", 1);
        AwaitCondition(() => _sink.Writes.Count == 1);

        _sink.Writes.TryDequeue(out var logEvent).Should().BeTrue();
        logEvent!.Properties.ContainsKey("TestContext").Should().BeTrue();
    }
}