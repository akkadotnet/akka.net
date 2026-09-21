// -----------------------------------------------------------------------
//  <copyright file="Issue701SemanticLoggingRegressionSpecs.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Event;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Akka.Hosting.Tests.Logging;

/// <summary>
/// Bug reproduction tests for semantic logging issue reported in Discord.
///
/// Issue: When using ILoggingAdapter with named placeholders like {UserId}, the formatted
/// message output shows the raw template instead of substituted values.
///
/// Example:
///   _logger.Info("User {UserId} logged in", 12345);
///
/// Expected output: "User 12345 logged in"
/// Actual output:   "User {UserId} logged in"
///
/// Root cause: LoggerFactoryLogger.FormatMessage() uses string.Format() which only
/// supports positional placeholders ({0}, {1}), not named placeholders ({UserId}).
/// </summary>
public class Issue701SemanticLoggingRegressionSpecs : TestKit.TestKit
{
    private readonly BugReproTestSink _sink;

    public Issue701SemanticLoggingRegressionSpecs(ITestOutputHelper output) : base(output: output)
    {
        _sink = new BugReproTestSink(output);
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        builder.ConfigureLoggers(setup =>
        {
            setup.LogLevel = Event.LogLevel.InfoLevel;
            setup.ClearLoggers();
            setup.AddLoggerFactory(new BugReproTestLoggerFactory(_sink));
        });
    }

    private void AssertMetadata(BugReproLogEntry entry, string? format = null)
    {
        entry.State.Should().ContainKey("ActorPath");
        entry.State.Should().ContainKey("Timestamp");
        entry.State.Should().ContainKey("Thread");
        entry.State.Should().ContainKey("LogSource");
        entry.State.Should().ContainKey("{OriginalFormat}");
        
        entry.State["ActorPath"].Should().BeOfType<string>();
        entry.State["Timestamp"].Should().BeOfType<DateTimeOffset>();
        entry.State["Thread"].Should().BeOfType<int>();
        entry.State["LogSource"].Should().BeOfType<string>();
        entry.State["{OriginalFormat}"].Should().BeOfType<string>();

        if (format is not null)
            entry.State["{OriginalFormat}"].Should().Be(format);
    }
    
    /// <summary>
    /// BUG REPRO: Named template placeholders should have values substituted in the formatted message.
    /// This test demonstrates the bug where {UserId} is NOT replaced with the actual value.
    /// </summary>
    [Fact(DisplayName = "Named placeholders should be substituted in formatted message")]
    public void NamedPlaceholdersShouldBeSubstitutedInFormattedMessage()
    {
        _sink.Clear();

        // Log with named placeholder
        Sys.Log.Info("User {UserId} logged in", 12345);

        AwaitCondition(() => _sink.Entries.Any(e =>
            e.Message.Contains("User") && (e.Message.Contains("12345") || e.Message.Contains("{UserId}"))));

        var entry = _sink.Entries.First(e =>
            e.Message.Contains("User") && (e.Message.Contains("12345") || e.Message.Contains("{UserId}")));

        // BUG: The message should contain the substituted value "12345", not the raw placeholder "{UserId}"
        entry.Message.Should().Contain("12345",
            "the formatted message should contain the substituted value, not the raw template placeholder");
        entry.Message.Should().NotContain("{UserId}",
            "the formatted message should NOT contain the raw template placeholder");
        
        entry.State.Should().ContainKey("UserId");
        entry.State["UserId"].Should().Be(12345);
        
        AssertMetadata(entry, "User {UserId} logged in");
    }

    /// <summary>
    /// Control test: Positional placeholders ({0}) should work correctly.
    /// </summary>
    [Fact(DisplayName = "Positional placeholders should be substituted correctly")]
    public void PositionalPlaceholdersShouldBeSubstitutedCorrectly()
    {
        _sink.Clear();

        // Log with positional placeholder
        Sys.Log.Info("User {0} logged in", 12345);

        AwaitCondition(() => _sink.Entries.Any(e =>
            e.Message.Contains("User") && e.Message.Contains("logged in")));

        var entry = _sink.Entries.First(e =>
            e.Message.Contains("User") && e.Message.Contains("logged in"));

        // Positional placeholders should work
        entry.Message.Should().Contain("12345",
            "positional placeholders should be substituted correctly");
        entry.Message.Should().NotContain("{0}",
            "positional placeholders should be replaced");
        
        entry.State.Should().ContainKey("0");
        entry.State["0"].Should().Be(12345);
        
        AssertMetadata(entry, "User {0} logged in");
    }

    /// <summary>
    /// BUG REPRO: Multiple named placeholders should all be substituted.
    /// This matches the Discord user's exact scenario.
    /// </summary>
    [Fact(DisplayName = "Multiple named placeholders")]
    public void MultipleNamedPlaceholdersDiscordScenario()
    {
        _sink.Clear();

        // Matches the bug log format:
        // _logger.Info("Published callback event: {Event} | ActorId: {ActorId}", eventName, actorId)
        Sys.Log.Info("Published callback event: {Event} | ActorId: {ActorId}", "UserLoggedIn", "actor-123");

        AwaitCondition(() => _sink.Entries.Any(e => e.Message.Contains("Published callback event")));

        var entry = _sink.Entries.First(e => e.Message.Contains("Published callback event"));

        // BUG: Values should be substituted
        entry.Message.Should().Contain("UserLoggedIn",
            "the Event value should be substituted");
        entry.Message.Should().Contain("actor-123",
            "the ActorId value should be substituted");
        entry.Message.Should().NotContain("{Event}",
            "should NOT contain raw {Event} placeholder");
        entry.Message.Should().NotContain("{ActorId}",
            "should NOT contain raw {ActorId} placeholder");
        
        entry.State.Should().ContainKey("Event");
        entry.State.Should().ContainKey("ActorId");
        entry.State["Event"].Should().Be("UserLoggedIn");
        entry.State["ActorId"].Should().Be("actor-123");
        
        AssertMetadata(entry, "Published callback event: {Event} | ActorId: {ActorId}");
    }

    /// <summary>
    /// Verify that structured properties ARE extracted correctly even when message formatting fails.
    /// This shows that the semantic logging infrastructure works - only the message formatting is broken.
    ///
    /// This test demonstrates the contrast:
    /// - State dictionary: Properties correctly extracted (UserId=12345, Email=user@example.com)
    /// - Message string: Values NOT substituted (shows raw "{UserId}" instead of "12345")
    /// </summary>
    [Fact(DisplayName = "Properties extracted correctly but message formatting broken")]
    public void PropertiesExtractedButMessageFormattingBroken()
    {
        _sink.Clear();

        Sys.Log.Info("User {UserId} with email {Email} logged in", 12345, "user@example.com");

        AwaitCondition(() => _sink.Entries.Any(e =>
            e.State.ContainsKey("UserId") || e.State.ContainsKey("Email")));

        var entry = _sink.Entries.First(e =>
            e.State.ContainsKey("UserId") || e.State.ContainsKey("Email"));

        // WORKS: Properties ARE extracted correctly into state dictionary
        entry.State.Should().ContainKey("UserId");
        entry.State.Should().ContainKey("Email");
        entry.State["UserId"].Should().Be(12345);
        entry.State["Email"].Should().Be("user@example.com");

        // BUG: Message should have substituted values, but it doesn't
        entry.Message.Should().Contain("12345",
            "the formatted message should contain the substituted UserId value");
        entry.Message.Should().Contain("user@example.com",
            "the formatted message should contain the substituted Email value");
        
        AssertMetadata(entry, "User {UserId} with email {Email} logged in");
    }

    /// <summary>
    /// BUG REPRO: Mixed positional and named placeholders (edge case).
    /// </summary>
    [Fact(DisplayName = "Mixed positional and named placeholders")]
    public void MixedPositionalAndNamedPlaceholders()
    {
        _sink.Clear();

        // Mix of positional and named
        Sys.Log.Info("User {UserId} action {0}", 12345, "Login");

        AwaitCondition(() => _sink.Entries.Any(e =>
            e.Message.Contains("User") && e.Message.Contains("action")));

        var entry = _sink.Entries.First(e =>
            e.Message.Contains("User") && e.Message.Contains("action"));

        // Both should be substituted
        entry.Message.Should().Contain("12345", "UserId should be substituted");
        entry.Message.Should().Contain("Login", "action should be substituted");
        
        entry.State.Should().ContainKey("UserId");
        entry.State.Should().ContainKey("0");
        entry.State["UserId"].Should().Be(12345);
        entry.State["0"].Should().Be("Login");
        
        AssertMetadata(entry, "User {UserId} action {0}");
    }

    /// <summary>
    /// Regression test: Plain string logs (no template placeholders) should still include
    /// Akka metadata (ActorPath, LogSource, Timestamp, Thread) as structured state properties.
    /// Before fix, non-semantic logs lost all metadata due to AkkaLogState lightweight constructor
    /// setting _hasSemanticProperties = false.
    /// </summary>
    [Fact(DisplayName = "Plain string logs should include Akka metadata")]
    public void PlainStringLogsShouldIncludeMetadata()
    {
        _sink.Clear();

        Sys.Log.Info("Server started successfully");

        AwaitCondition(() => _sink.Entries.Any(e => e.Message.Contains("Server started successfully")));

        var entry = _sink.Entries.First(e => e.Message.Contains("Server started successfully"));

        // Metadata should be present even for plain string logs
        AssertMetadata(entry);
    }

}

/// <summary>
/// Test sink that captures both the formatted message AND the structured state
/// </summary>
public class BugReproTestSink : ILogger
{
    private readonly ITestOutputHelper _output;
    public ConcurrentQueue<BugReproLogEntry> Entries { get; } = new();

    public BugReproTestSink(ITestOutputHelper output)
    {
        _output = output;
    }

    public void Clear()
    {
        Entries.Clear();
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        // Get the formatted message - this is where the bug manifests
        var message = formatter(state, exception);
        _output.WriteLine($"[{logLevel}] Message: \"{message}\"");

        // Capture state as dictionary
        var stateDict = new Dictionary<string, object>();
        if (state is IEnumerable<KeyValuePair<string, object>> kvps)
        {
            foreach (var kvp in kvps)
            {
                stateDict[kvp.Key] = kvp.Value;
                _output.WriteLine($"  State[{kvp.Key}] = {kvp.Value}");
            }
        }

        Entries.Enqueue(new BugReproLogEntry
        {
            LogLevel = logLevel,
            Message = message,
            State = stateDict,
            Exception = exception
        });
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => EmptyDisposable.Instance;
}

public class BugReproLogEntry
{
    public LogLevel LogLevel { get; init; }
    public string Message { get; init; } = string.Empty;
    public Dictionary<string, object> State { get; init; } = new();
    public Exception? Exception { get; init; }
}

public class BugReproTestLoggerFactory : ILoggerFactory
{
    private readonly BugReproTestSink _sink;

    public BugReproTestLoggerFactory(BugReproTestSink sink)
    {
        _sink = sink;
    }

    public void Dispose() { }

    public ILogger CreateLogger(string categoryName) => _sink;

    public void AddProvider(ILoggerProvider provider) { }
}
