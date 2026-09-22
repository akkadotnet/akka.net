// -----------------------------------------------------------------------
//  <copyright file="LogStateSnapshotSpecs.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Akka.Actor;
using Akka.Event;
using Akka.Hosting;
using Microsoft.Extensions.Logging;
using VerifyXunit;
using Xunit;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Akka.Hosting.Tests.Logging;

/// <summary>
/// Verify snapshot tests for LoggerFactoryLogger structured state output.
/// Follows the same sanitization pattern as DefaultLogFormatSpec in Akka.NET.
/// </summary>
public class LogStateSnapshotSpecs : TestKit.TestKit
{
    private readonly SnapshotTestSink _sink;

    public LogStateSnapshotSpecs(ITestOutputHelper output) : base(output: output)
    {
        _sink = new SnapshotTestSink(output);
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        builder.ConfigureLoggers(setup =>
        {
            setup.LogLevel = Event.LogLevel.InfoLevel;
            setup.ClearLoggers();
            setup.AddLoggerFactory(new SnapshotTestLoggerFactory(_sink));
        });
    }

    [Fact]
    public System.Threading.Tasks.Task StructuredLog_StateSnapshot()
    {
        _sink.Clear();
        Sys.Log.Info("User {UserId} performed {Action}", 12345, "Login");
        AwaitCondition(() => _sink.Entries.Any(e => e.Message.Contains("12345")));
        var entry = _sink.Entries.First(e => e.Message.Contains("12345"));
        return Verifier.Verify(FormatEntry(entry));
    }

    [Fact]
    public System.Threading.Tasks.Task PlainStringLog_StateSnapshot()
    {
        _sink.Clear();
        Sys.Log.Info("Server started successfully");
        AwaitCondition(() => _sink.Entries.Any(e => e.Message.Contains("Server started successfully")));
        var entry = _sink.Entries.First(e => e.Message.Contains("Server started successfully"));
        return Verifier.Verify(FormatEntry(entry));
    }

    [Fact]
    public System.Threading.Tasks.Task PositionalPlaceholderLog_StateSnapshot()
    {
        _sink.Clear();
        Sys.Log.Info("User {0} logged in", 99);
        AwaitCondition(() => _sink.Entries.Any(e => e.Message.Contains("99")));
        var entry = _sink.Entries.First(e => e.Message.Contains("99"));
        return Verifier.Verify(FormatEntry(entry));
    }

    private static string FormatEntry(SnapshotLogEntry entry)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"LogLevel: {entry.LogLevel}");
        sb.AppendLine($"Message: {SanitizeMessage(entry.Message)}");
        sb.AppendLine("State:");
        foreach (var kvp in entry.State.OrderBy(k => k.Key))
        {
            sb.AppendLine($"  [{kvp.Key}] = {SanitizeValue(kvp.Key, kvp.Value)}");
        }
        return sb.ToString();
    }

    private static string SanitizeMessage(string message)
    {
        message = Regex.Replace(message,
            @"\[(DEBUG|INFO|WARNING|ERROR)\]",
            "[LEVEL]");
        message = Regex.Replace(message,
            @"\[\d{2}/\d{2}/\d{4} \d{2}:\d{2}:\d{2}\.\d{3}Z?\]",
            "[DateTime]");
        message = Regex.Replace(message,
            @"\[Thread \d+\]",
            "[Thread 0001]");
        message = Regex.Replace(message,
            @"\[akka://[^\]]+\]",
            "[ActorPath]");
        return message;
    }

    private static string SanitizeValue(string key, object? value)
    {
        return key switch
        {
            "ActorPath" => "akka://test/...",
            "Timestamp" => "DateTime",
            "Thread" => "1",
            "LogSource" => "LogSource",
            _ => value?.ToString() ?? "null"
        };
    }
}

internal class SnapshotLogEntry
{
    public LogLevel LogLevel { get; init; }
    public string Message { get; init; } = string.Empty;
    public Dictionary<string, object> State { get; init; } = new();
}

internal class SnapshotTestSink : ILogger
{
    private readonly ITestOutputHelper _output;
    public ConcurrentQueue<SnapshotLogEntry> Entries { get; } = new();

    public SnapshotTestSink(ITestOutputHelper output) { _output = output; }

    public void Clear() => Entries.Clear();

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        _output.WriteLine($"[{logLevel}] Message: \"{message}\"");

        var stateDict = new Dictionary<string, object>();
        if (state is IEnumerable<KeyValuePair<string, object>> kvps)
        {
            foreach (var kvp in kvps)
            {
                stateDict[kvp.Key] = kvp.Value;
                _output.WriteLine($"  State[{kvp.Key}] = {kvp.Value}");
            }
        }

        Entries.Enqueue(new SnapshotLogEntry
        {
            LogLevel = logLevel,
            Message = message,
            State = stateDict,
        });
    }

    public bool IsEnabled(LogLevel logLevel) => true;
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => EmptyDisposable.Instance;
}

internal class SnapshotTestLoggerFactory : ILoggerFactory
{
    private readonly SnapshotTestSink _sink;
    public SnapshotTestLoggerFactory(SnapshotTestSink sink) { _sink = sink; }
    public void Dispose() { }
    public ILogger CreateLogger(string categoryName) => _sink;
    public void AddProvider(ILoggerProvider provider) { }
}
