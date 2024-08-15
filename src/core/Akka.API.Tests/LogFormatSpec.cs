// -----------------------------------------------------------------------
//  <copyright file="LogFormatSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

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
///     Regression test for https://github.com/akkadotnet/akka.net/issues/7255
///     Need to assert that the default log format is still working as expected.
/// </summary>
public sealed class DefaultLogFormatSpec : TestKit.Xunit2.TestKit
{
    private readonly CustomLogger _logger;

    public DefaultLogFormatSpec() : base(CustomLoggerSetup())
    {
        _logger = (CustomLogger)Sys.Settings.StdoutLogger;
    }

    public static ActorSystemSetup CustomLoggerSetup()
    {
        var hocon = @$"
            akka.loglevel = DEBUG
            akka.stdout-logger-class = ""{typeof(CustomLogger).AssemblyQualifiedName}""";
        var bootstrapSetup = BootstrapSetup.Create().WithConfig(ConfigurationFactory.ParseString(hocon));
        return ActorSystemSetup.Create(bootstrapSetup);
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

        await Verifier.Verify(text);
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
        var pattern = @"\[\d{2}/\d{2}/\d{4} \d{2}:\d{2}:\d{2}\.\d{3}Z\]";

        // Replace all occurrences of the datetime with the constant value
        var result = Regex.Replace(logs, pattern, $"[{replacement}]", RegexOptions.Multiline);

        return result;
    }

    public class CustomLogger : StandardOutLogger
    {
        private readonly ConcurrentBag<LogEvent> _events = new();
        public IReadOnlyCollection<LogEvent> Events => _events;

        protected override void Log(object message)
        {
            base.Log(message); // log first, just so we can be sure it's hit STDOUT
            if (message is LogEvent e) _events.Add(e);
        }
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
}