// -----------------------------------------------------------------------
//  <copyright file="SemanticLoggingSpecs.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using FluentAssertions;
using FluentAssertions.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Akka.Hosting.Tests.Logging;

/// <summary>
/// Tests for semantic logging functionality added in Akka.NET 1.5.56.
/// Verifies that structured properties from log message templates are
/// accessible in Microsoft.Extensions.Logging state dictionaries.
/// </summary>
public class SemanticLoggingSpecs : IAsyncLifetime
{
    private readonly SemanticTestLogger _logger;
    private IHost? _host;
    private IActorRef? _testActor;

    public SemanticLoggingSpecs(ITestOutputHelper helper)
    {
        _logger = new SemanticTestLogger(helper);
    }

    public async ValueTask InitializeAsync()
    {
        _host = await SetupHost(_logger);
        var registry = _host.Services.GetRequiredService<ActorRegistry>();
        _testActor = registry.Get<TestActor>();
    }

    public ValueTask DisposeAsync()
    {
        _host?.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact(DisplayName = "Should extract named template properties and add to MEL state dictionary")]
    public async Task NamedTemplatePropertiesTest()
    {
        _logger.Clear();
        await WaitUntilSilent(5.Seconds());

        var reply = await _testActor.Ask<string>("User {UserId} with email {Email} logged in|12345|user@example.com");
        reply.Should().Be("OK");

        await AwaitAssertAsync(() =>
        {
            _logger.LogEntries.Should().NotBeEmpty();
            var entry = _logger.LogEntries.First(e => e.Message.Contains("User") && e.Message.Contains("logged in"));

            entry.State.Should().ContainKey("UserId");
            entry.State.Should().ContainKey("Email");
            entry.State["UserId"].Should().Be(12345);
            entry.State["Email"].Should().Be("user@example.com");
        });
    }

    [Fact(DisplayName = "Should extract positional template properties and add to MEL state dictionary")]
    public async Task PositionalTemplatePropertiesTest()
    {
        _logger.Clear();
        await WaitUntilSilent(5.Seconds());

        var reply = await _testActor.Ask<string>("User {0} logged in from {1}|Bob|192.168.1.1");
        reply.Should().Be("OK");

        await AwaitAssertAsync(() =>
        {
            _logger.LogEntries.Should().NotBeEmpty();
            var entry = _logger.LogEntries.First(e => e.Message.Contains("User") && e.Message.Contains("logged in from"));

            entry.State.Should().ContainKey("0");
            entry.State.Should().ContainKey("1");
            entry.State["0"].Should().Be("Bob");
            entry.State["1"].Should().Be("192.168.1.1");
        });
    }

    [Fact(DisplayName = "Should handle multiple named properties in template")]
    public async Task MultipleNamedPropertiesTest()
    {
        _logger.Clear();
        await WaitUntilSilent(5.Seconds());

        var reply = await _testActor.Ask<string>("Order {OrderId} for customer {CustomerId}: {Amount} {Currency}|ORD-001|CUST-456|99.99|USD");
        reply.Should().Be("OK");

        await AwaitAssertAsync(() =>
        {
            _logger.LogEntries.Should().NotBeEmpty();
            var entry = _logger.LogEntries.First(e => e.Message.Contains("Order") && e.Message.Contains("customer"));

            entry.State.Should().ContainKey("OrderId");
            entry.State.Should().ContainKey("CustomerId");
            entry.State.Should().ContainKey("Amount");
            entry.State.Should().ContainKey("Currency");
            entry.State["OrderId"].Should().Be("ORD-001");
            entry.State["CustomerId"].Should().Be("CUST-456");
            entry.State["Amount"].Should().Be(99.99);
            entry.State["Currency"].Should().Be("USD");
        });
    }

    [Fact(DisplayName = "Should preserve Akka metadata properties alongside semantic logging properties")]
    public async Task AkkaMetadataAndSemanticPropertiesTest()
    {
        _logger.Clear();
        await WaitUntilSilent(5.Seconds());

        var reply = await _testActor.Ask<string>("User {UserId} action|999");
        reply.Should().Be("OK");

        await AwaitAssertAsync(() =>
        {
            _logger.LogEntries.Should().NotBeEmpty();
            var entry = _logger.LogEntries.First(e => e.Message.Contains("User") && e.Message.Contains("action"));

            // Semantic property
            entry.State.Should().ContainKey("UserId");
            entry.State["UserId"].Should().Be(999);

            // Akka metadata properties
            entry.State.Should().ContainKey("ActorPath");
            entry.State.Should().ContainKey("LogSource");
            entry.State.Should().ContainKey("Thread");
            entry.State.Should().ContainKey("Timestamp");
        });
    }

    [Fact(DisplayName = "Should include {OriginalFormat} key per MEL convention")]
    public async Task OriginalFormatKeyTest()
    {
        _logger.Clear();
        await WaitUntilSilent(5.Seconds());

        var reply = await _testActor.Ask<string>("Processing item {ItemId}|42");
        reply.Should().Be("OK");

        await AwaitAssertAsync(() =>
        {
            _logger.LogEntries.Should().NotBeEmpty();
            var entry = _logger.LogEntries.First(e => e.Message.Contains("Processing item"));

            // MEL convention key
            entry.State.Should().ContainKey("{OriginalFormat}");
            entry.State["{OriginalFormat}"].Should().Be("Processing item {ItemId}");

            // Semantic property
            entry.State.Should().ContainKey("ItemId");
            entry.State["ItemId"].Should().Be(42);
        });
    }

    [Fact(DisplayName = "Should handle format specifiers in named templates")]
    public async Task FormatSpecifiersInTemplatesTest()
    {
        _logger.Clear();
        await WaitUntilSilent(5.Seconds());

        // Template has format specifier :N2, but property name should be "Amount"
        var reply = await _testActor.Ask<string>("Total amount: {Amount:N2}|1234.5678");
        reply.Should().Be("OK");

        await AwaitAssertAsync(() =>
        {
            _logger.LogEntries.Should().NotBeEmpty();
            var entry = _logger.LogEntries.First(e => e.Message.Contains("Total amount"));

            // Property name is "Amount" (format specifier removed by Akka's parser)
            entry.State.Should().ContainKey("Amount");
            entry.State["Amount"].Should().Be(1234.5678);

            // Original format includes the specifier
            entry.State["{OriginalFormat}"].Should().Be("Total amount: {Amount:N2}");
        });
    }

    [Fact(DisplayName = "Should handle empty/no properties gracefully")]
    public async Task NoPropertiesTest()
    {
        _logger.Clear();
        await WaitUntilSilent(5.Seconds());

        var reply = await _testActor.Ask<string>("No template properties here|");
        reply.Should().Be("OK");

        await AwaitAssertAsync(() =>
        {
            _logger.LogEntries.Should().NotBeEmpty();
            var entry = _logger.LogEntries.First(e => e.Message.Contains("No template properties here"));

            // Should still have Akka metadata properties
            entry.State.Should().ContainKey("ActorPath");
            entry.State.Should().ContainKey("LogSource");
            entry.State.Should().ContainKey("Thread");
            entry.State.Should().ContainKey("Timestamp");

            // Should have {OriginalFormat}
            entry.State.Should().ContainKey("{OriginalFormat}");
        });
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
                if (cts.IsCancellationRequested)
                    return;

                count = _logger.ReceivedLogs;
            } while (previousCount != count);
        }
        finally
        {
            cts.Dispose();
        }
    }

    private async Task AwaitAssertAsync(Action assertion, TimeSpan? timeout = null, TimeSpan? interval = null)
    {
        var maxWait = timeout ?? TimeSpan.FromSeconds(3);
        var checkInterval = interval ?? TimeSpan.FromMilliseconds(100);
        var cts = new CancellationTokenSource(maxWait);

        Exception? lastException = null;
        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                assertion();
                return; // Assertion passed
            }
            catch (Exception ex)
            {
                lastException = ex;
                try
                {
                    await Task.Delay(checkInterval, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        // If we get here, we timed out - throw the last exception
        throw new TimeoutException(
            $"Assertion did not pass within {maxWait.TotalSeconds}s",
            lastException);
    }

    private static async Task<IHost> SetupHost(SemanticTestLogger logger)
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
                            setup.AddLoggerFactory(new SemanticTestLoggerFactory(logger));
                        })
                        .WithActors((system, registry) =>
                        {
                            var testActor = system.ActorOf(Props.Create(() => new TestActor()), "testActor");
                            registry.TryRegister<TestActor>(testActor);
                        });
                });
            }).Build();
        await host.StartAsync();
        return host;
    }

    /// <summary>
    /// Actor that logs semantic logging messages based on received strings
    /// Format: "template|arg1|arg2|..."
    /// </summary>
    private class TestActor : ReceiveActor
    {
        public TestActor()
        {
            var log = Context.GetLogger();
            Receive<string>(message =>
            {
                var parts = message.Split('|');
                var template = parts[0];
                var args = parts.Skip(1).Select(ParseArg).ToArray();

                log.Info(template, args);
                Sender.Tell("OK");
            });
        }

        private static object ParseArg(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return string.Empty;
            if (int.TryParse(arg, out var intVal)) return intVal;
            if (double.TryParse(arg, out var doubleVal)) return doubleVal;
            return arg;
        }
    }
}

/// <summary>
/// Test logger that captures structured state for semantic logging assertions
/// </summary>
public class SemanticTestLogger : ILogger
{
    private readonly ITestOutputHelper _helper;
    public readonly List<LogEntry> LogEntries = new();
    public int ReceivedLogs { get; private set; }

    public SemanticTestLogger(ITestOutputHelper helper)
    {
        _helper = helper;
    }

    public void Clear()
    {
        LogEntries.Clear();
        ReceivedLogs = 0;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        _helper.WriteLine($"[{logLevel}] {message}");
        ReceivedLogs++;

        // Capture state as dictionary for semantic logging assertions
        var stateDict = new Dictionary<string, object>();
        if (state is IEnumerable<KeyValuePair<string, object>> kvps)
        {
            foreach (var kvp in kvps)
            {
                stateDict[kvp.Key] = kvp.Value;
            }
        }

        LogEntries.Add(new LogEntry
        {
            LogLevel = logLevel,
            Message = message,
            State = stateDict,
            Exception = exception
        });
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => EmptyDisposable.Instance;
}

public class LogEntry
{
    public LogLevel LogLevel { get; set; }
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, object> State { get; set; } = new();
    public Exception? Exception { get; set; }
}

/// <summary>
/// Test logger factory for semantic logging tests
/// </summary>
public class SemanticTestLoggerFactory : ILoggerFactory
{
    private readonly SemanticTestLogger _logger;

    public SemanticTestLoggerFactory(SemanticTestLogger logger)
    {
        _logger = logger;
    }

    public void Dispose()
    {
    }

    public ILogger CreateLogger(string categoryName) => _logger;

    public void AddProvider(ILoggerProvider provider)
    {
    }
}
