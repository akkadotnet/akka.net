using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Akka.Hosting.Tests.Logging;

public class TestLogger : ILogger
{
    private readonly ITestOutputHelper _helper;
    public bool Recording { get; private set; }
    private string? _stopsWhen;

    public readonly List<string> Debugs = new();
    public readonly List<string> Infos = new();
    public readonly List<string> Warnings = new();
    public readonly List<string> Errors = new();

    public TestLogger(ITestOutputHelper helper)
    {
        _helper = helper;
    }

    public int TotalLogs => Debugs.Count + Infos.Count + Warnings.Count + Errors.Count;

    public int ReceivedLogs { get; private set; }

    public void StartRecording()
    {
        _helper.WriteLine("Logger starts recording");
        Recording = true;
    }

    public void StopWhenReceives(string message)
    {
        _stopsWhen = message;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        _helper.WriteLine($"[{logLevel}] {message}");
        ReceivedLogs++;

        if (!Recording)
            return;

        if (!string.IsNullOrEmpty(_stopsWhen) && message.Contains(_stopsWhen))
        {
            _helper.WriteLine("Logger stops recording");
            Recording = false;
        }

        switch (logLevel)
        {
            case LogLevel.Debug:
                Debugs.Add(message);
                break;
            case LogLevel.Information:
                Infos.Add(message);
                break;
            case LogLevel.Warning:
                Warnings.Add(message);
                break;
            case LogLevel.Error:
                Errors.Add(message);
                break;
            default:
                throw new Exception($"Unsupported LogLevel: {logLevel}");
        }
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return EmptyDisposable.Instance;
    }
}

public class EmptyDisposable : IDisposable
{
    public static readonly EmptyDisposable Instance = new EmptyDisposable();

    private EmptyDisposable()
    {
    }

#pragma warning disable CA1816
    public void Dispose()
#pragma warning restore CA1816
    {
    }
}