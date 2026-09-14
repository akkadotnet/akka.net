// -----------------------------------------------------------------------
//  <copyright file="CustomLoggerEnd2EndSpecs.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Event;
using FluentAssertions;
using Xunit;

namespace Akka.Hosting.Tests.Logging;

/// <summary>
/// End-to-end checks that a fully custom logger (one that replaces every built-in logger) plus a
/// custom <see cref="ILogMessageFormatter"/> can be wired up through <c>ConfigureLoggers</c> and
/// that message templates using both positional (<c>{0}</c>) and named (<c>{myNum}</c>)
/// placeholders survive the round trip without blowing up.
/// </summary>
public class CustomLoggerEnd2EndSpecs : TestKit.TestKit
{
    /// <summary>
    /// Published on the <see cref="EventStream"/> for every <see cref="LogEvent"/> the custom
    /// logger actually received, so a test can assert on the logger's own output.
    /// </summary>
    public sealed class CapturedLogEntry
    {
        public CapturedLogEntry(LogLevel level, string message)
        {
            Level = level;
            Message = message;
        }

        public LogLevel Level { get; }

        public string Message { get; }
    }

    /// <summary>
    /// Minimal Akka.NET logger actor - stands in for a third party logger such as Serilog.
    /// </summary>
    public sealed class CapturingLogger : ReceiveActor, IRequiresMessageQueue<ILoggerMessageQueueSemantics>
    {
        public CapturingLogger()
        {
            Receive<InitializeLogger>(_ => Sender.Tell(new LoggerInitialized()));
            Receive<LogEvent>(e =>
                Context.System.EventStream.Publish(new CapturedLogEntry(e.LogLevel(), e.ToString())));
        }
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        builder.ConfigureLoggers(setup =>
        {
            setup.ClearLoggers();
            setup.AddLogger<CapturingLogger>();
            setup.LogLevel = Event.LogLevel.DebugLevel;
#pragma warning disable CS0618
            setup.WithDefaultLogMessageFormatter<SemanticLogMessageFormatter>();
#pragma warning restore CS0618
        });
    }

    [Theory]
    [InlineData(Event.LogLevel.DebugLevel, "test case {0}", new object[] { 1 })]
    [InlineData(Event.LogLevel.DebugLevel, "test case {myNum}", new object[] { 1 })]
    [InlineData(Event.LogLevel.InfoLevel, "test case {myNum} {myStr}", new object[] { 1, "foo" })]
    public async Task ShouldHandleSemanticLogFormats(LogLevel level, string formatStr, object[] args)
    {
        Sys.EventStream.Subscribe(TestActor, typeof(LogEvent));
        Sys.EventStream.Subscribe(TestActor, typeof(CapturedLogEntry));

        Sys.Log.Log(level, formatStr, args);

        // The log level is Debug, so unrelated system events (actor lifecycle, logger start-up)
        // can land on the EventStream before ours does. Fish for the event this test emitted
        // instead of asserting on whichever LogEvent arrives first.
        // Formatting the event must not throw, regardless of the placeholder style used.
        var logEvent = await FishForMessageAsync<LogEvent>(e => e.ToString().Contains("test case"));
        logEvent.LogLevel().Should().Be(level);

        // ...and the custom logger must have received and formatted the same event
        var captured = await FishForMessageAsync<CapturedLogEntry>(e => e.Message.Contains("test case"));
        captured.Level.Should().Be(level);
    }
}
