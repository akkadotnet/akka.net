//-----------------------------------------------------------------------
// <copyright file="LogFormatSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Event;
using VerifyXunit;
using Xunit;

namespace Akka.API.Tests;

/// <summary>
/// Regression test for https://github.com/akkadotnet/akka.net/issues/7255
///
/// Need to assert that the default log format is still working as expected.
/// </summary>
public sealed class DefaultLogFormatSpec : TestKit.Xunit2.TestKit
{
    public DefaultLogFormatSpec() : base(CustomLoggerSetup())
    {
        _logger = (CustomLogger)Sys.Settings.StdoutLogger;
    }

    private readonly CustomLogger _logger;

    public class CustomLogger : StandardOutLogger
    {
        protected override void Log(object message)
        {
            base.Log(message); // log first, just so we can be sure it's hit STDOUT
            if (message is LogEvent e)
            {
                _events.Add(e);
            }

        }

        private readonly ConcurrentBag<LogEvent> _events = new();
        public IReadOnlyCollection<LogEvent> Events => _events;
    }

    public static ActorSystemSetup CustomLoggerSetup()
    {
        var hocon = @$"
            akka.loglevel = DEBUG
            akka.stdout-logger-class = ""{typeof(CustomLogger).AssemblyQualifiedName}""";
        var bootstrapSetup = BootstrapSetup.Create().WithConfig(ConfigurationFactory.ParseString(hocon));
        return ActorSystemSetup.Create(bootstrapSetup);
    }

    public class OutputRedirector : IDisposable
    {
        private readonly TextWriter _originalOutput;
        private readonly StreamWriter _writer;

        public OutputRedirector(string filePath)
        {
            _originalOutput = Console.Out;
            _writer = new StreamWriter(filePath) { AutoFlush = true };
            Console.SetOut(_writer);
        }

        public void Dispose()
        {
            Console.SetOut(_originalOutput);
            _writer.Dispose();
        }
    }

    [Fact]
    public async Task ShouldUseDefaultLogFormat()
    {
        // arrange
        var filePath = Path.GetTempFileName();

        // act
        using (new OutputRedirector(filePath))
        {
            Sys.Log.Debug("This is a test {0} {1}", 1, "cheese");
            Sys.Log.Info("This is a test {0}", 1);
            Sys.Log.Warning("This is a test {0}", 1);
            Sys.Log.Error("This is a test {0}", 1);

            // force all logs to be received
            await AwaitConditionAsync(() =>
            {
                return _logger.Events.Count(c => c.Message.ToString()!.Contains("This is a test")) == 4;
            });
        }

        // assert
        // ReSharper disable once MethodHasAsyncOverload
        var text = File.ReadAllText(filePath);

        // need to sanitize the thread id
        text = SanitizeDateTime(text);
        text = SanitizeThreadNumber(text);
        // to resolve https://github.com/akkadotnet/akka.net/issues/7421
        text = SanitizeTestEventListener(text);

        await Verifier.Verify(text);
    }

    [Fact]
    public async Task ShouldHandleSemanticLogEdgeCases()
    {
        // arrange
        var filePath = Path.GetTempFileName();

        // act
        using (new OutputRedirector(filePath))
        {
            // Named properties
            Sys.Log.Debug("User {UserId} logged in from {IpAddress}", 12345, "192.168.1.1");
            Sys.Log.Info("Processing order {OrderId} for customer {CustomerId}", "ORD-001", "CUST-999");

            // Positional properties (old style)
            Sys.Log.Warning("Processing item {0} of {1}", 5, 10);

            // Mixed types - use F2 instead of C for culture-independent output
            Sys.Log.Info("Order total is ${Amount:F2} with {ItemCount} items", 123.45m, 3);

            // Edge cases
            Sys.Log.Debug("Empty template");
            Sys.Log.Info("Single property {Value}", 42);
            Sys.Log.Warning("Null value: {NullValue}", null);
            Sys.Log.Error("Exception occurred for user {UserId}", 999);

            // Special characters and escaping
            Sys.Log.Debug("Path: {FilePath}, Size: {FileSize} bytes", @"C:\temp\file.txt", 1024);

            // Boolean and date types - use explicit date format for culture-independent output
            Sys.Log.Info("User {Username} is active: {IsActive}, joined on {JoinDate:yyyy-MM-dd}", "john.doe", true, DateTime.Parse("2024-01-15"));

            // Long strings and alignment
            Sys.Log.Debug("Request from {RemoteAddress} to endpoint {Endpoint} took {DurationMs}ms", "192.168.1.100:54321", "/api/v1/users", 250);

            // force all logs to be received - wait for the last log message
            await AwaitConditionAsync(() => Task.FromResult(_logger.Events.Any(e => e.Message.ToString()!.Contains("took 250ms"))), TimeSpan.FromSeconds(5));
        }

        // assert
        // ReSharper disable once MethodHasAsyncOverload
        var text = File.ReadAllText(filePath);

        // need to sanitize the thread id and timestamps
        text = SanitizeDateTime(text);
        text = SanitizeThreadNumber(text);
        text = SanitizeTestEventListener(text);
        text = SanitizeDefaultLoggersStarted(text);
        text = SanitizeCustomLoggerRemoved(text);

        await Verifier.Verify(text);
    }

    [Fact]
    public async Task ShouldIncludeContextInDefaultLogFormat()
    {
        // arrange
        var filePath = Path.GetTempFileName();

        // act
        using (new OutputRedirector(filePath))
        {
            var enrichedLog = Sys.Log
                .WithContext("Tenant", "foo")
                .WithContext("Partition", 12);

            enrichedLog.Info("Contexted {Value}", 42);

            using (var scope = Sys.Log.BeginScope("RequestId", "REQ-123"))
            {
                scope.Log.Info("Scoped {Value}", 7);
            }

            await AwaitConditionAsync(() =>
            {
                return _logger.Events.Any(e => e.Message.ToString()!.Contains("Contexted"))
                       && _logger.Events.Any(e => e.Message.ToString()!.Contains("Scoped"));
            });
        }

        // assert
        // ReSharper disable once MethodHasAsyncOverload
        var text = File.ReadAllText(filePath);
        text = SanitizeDateTime(text);
        text = SanitizeThreadNumber(text);
        text = SanitizeTestEventListener(text);
        text = SanitizeDefaultLoggersStarted(text);
        text = SanitizeCustomLoggerRemoved(text);

        await Verifier.Verify(text);
    }

    private static string SanitizeDefaultLoggersStarted(string logs)
    {
        var pattern = @"^.*Default Loggers started.*$\r?\n?";
        var result = Regex.Replace(logs, pattern, string.Empty, RegexOptions.Multiline);
        return result;
    }

    private static string SanitizeCustomLoggerRemoved(string logs)
    {
        var pattern = @"^.*CustomLogger being removed.*$\r?\n?";
        var result = Regex.Replace(logs, pattern, string.Empty, RegexOptions.Multiline);
        return result;
    }

    private static string SanitizeTestEventListener(string logs)
    {
        var pattern = @"^.*Akka\.TestKit\.TestEventListener.*$";
        var result = Regex.Replace(logs, pattern, string.Empty, RegexOptions.Multiline);
        return result;
    }

    private static string SanitizeThreadNumber(string log)
    {
        var pattern = @"(\[Thread )\d+(\])";
        var replacement = "[Thread 0001]";
        var result = Regex.Replace(log, pattern, replacement);
        return result;
    }

    private static string SanitizeDateTime(string logs, string replacement = "DateTime")
    {
        // Regular expression to match the datetime
        string pattern = @"\[\d{2}/\d{2}/\d{4} \d{2}:\d{2}:\d{2}\.\d{3}Z\]";

        // Replace all occurrences of the datetime with the constant value
        string result = Regex.Replace(logs, pattern, $"[{replacement}]", RegexOptions.Multiline);

        return result;
    }
}
