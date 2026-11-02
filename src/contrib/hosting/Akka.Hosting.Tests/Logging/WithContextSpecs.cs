// -----------------------------------------------------------------------
//  <copyright file="WithContextSpecs.cs" company="Akka.NET Project">
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
/// Tests for core Akka.NET WithContext() logging context enrichment
/// flowing through to Microsoft.Extensions.Logging state dictionaries.
/// </summary>
public class WithContextSpecs : IAsyncLifetime
{
    private readonly SemanticTestLogger _logger;
    private IHost? _host;
    private IActorRef? _testActor;

    public WithContextSpecs(ITestOutputHelper helper)
    {
        _logger = new SemanticTestLogger(helper);
    }

    public async ValueTask InitializeAsync()
    {
        _host = await SetupHost(_logger);
        var registry = _host.Services.GetRequiredService<ActorRegistry>();
        _testActor = registry.Get<WithContextTestActor>();
    }

    public ValueTask DisposeAsync()
    {
        _host?.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact(DisplayName = "WithContext single property should appear in MEL state dictionary")]
    public async Task WithContext_SingleProperty_AppearsInMelState()
    {
        _logger.Clear();
        await WaitUntilSilent(5.Seconds());

        // Format: "ctx:Key=Value|template|arg1|arg2"
        var reply = await _testActor!.Ask<string>("ctx:TenantId=TENANT-001|Processing request");
        reply.Should().Be("OK");

        await AwaitAssertAsync(() =>
        {
            _logger.LogEntries.Should().NotBeEmpty();
            var entry = _logger.LogEntries.First(e => e.Message.Contains("Processing request"));

            entry.State.Should().ContainKey("TenantId");
            entry.State["TenantId"].Should().Be("TENANT-001");
        });
    }

    [Fact(DisplayName = "WithContext multiple properties should all appear in MEL state dictionary")]
    public async Task WithContext_MultipleProperties_AllAppearInState()
    {
        _logger.Clear();
        await WaitUntilSilent(5.Seconds());

        // Multiple ctx: properties separated by commas
        var reply = await _testActor!.Ask<string>("ctx:TenantId=TENANT-002,CorrelationId=CORR-123,Region=us-east-1|Multi-context request");
        reply.Should().Be("OK");

        await AwaitAssertAsync(() =>
        {
            _logger.LogEntries.Should().NotBeEmpty();
            var entry = _logger.LogEntries.First(e => e.Message.Contains("Multi-context request"));

            entry.State.Should().ContainKey("TenantId");
            entry.State.Should().ContainKey("CorrelationId");
            entry.State.Should().ContainKey("Region");
            entry.State["TenantId"].Should().Be("TENANT-002");
            entry.State["CorrelationId"].Should().Be("CORR-123");
            entry.State["Region"].Should().Be("us-east-1");
        });
    }

    [Fact(DisplayName = "WithContext combined with semantic template should have both context and template properties")]
    public async Task WithContext_CombinedWithSemanticTemplate_BothAppearInState()
    {
        _logger.Clear();
        await WaitUntilSilent(5.Seconds());

        var reply = await _testActor!.Ask<string>("ctx:TenantId=TENANT-003|User {UserId} performed {Action}|42|login");
        reply.Should().Be("OK");

        await AwaitAssertAsync(() =>
        {
            _logger.LogEntries.Should().NotBeEmpty();
            var entry = _logger.LogEntries.First(e => e.Message.Contains("User") && e.Message.Contains("performed"));

            // Context property
            entry.State.Should().ContainKey("TenantId");
            entry.State["TenantId"].Should().Be("TENANT-003");

            // Template properties
            entry.State.Should().ContainKey("UserId");
            entry.State.Should().ContainKey("Action");
            entry.State["UserId"].Should().Be(42);
            entry.State["Action"].Should().Be("login");
        });
    }

    [Fact(DisplayName = "WithContext should not pollute unrelated log events")]
    public async Task WithContext_DoesNotPolluteUnrelatedLogs()
    {
        _logger.Clear();
        await WaitUntilSilent(5.Seconds());

        // First: log with context
        var reply1 = await _testActor!.Ask<string>("ctx:SecretContext=should-not-leak|Context message with {Marker}|CTX");
        reply1.Should().Be("OK");

        // Second: log without context
        var reply2 = await _testActor!.Ask<string>("Plain message with {Marker}|PLAIN");
        reply2.Should().Be("OK");

        await AwaitAssertAsync(() =>
        {
            _logger.LogEntries.Should().NotBeEmpty();

            var contextEntry = _logger.LogEntries.First(e =>
                e.State.ContainsKey("Marker") && "CTX".Equals(e.State["Marker"]));
            var plainEntry = _logger.LogEntries.First(e =>
                e.State.ContainsKey("Marker") && "PLAIN".Equals(e.State["Marker"]));

            contextEntry.State.Should().ContainKey("SecretContext");
            plainEntry.State.Should().NotContainKey("SecretContext",
                "context properties should not leak to loggers without that context");
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
                return;
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

        throw new TimeoutException(
            $"Assertion did not pass within {maxWait.TotalSeconds}s",
            lastException);
    }

    private static async Task<IHost> SetupHost(SemanticTestLogger logger)
    {
        var host = new HostBuilder()
            .ConfigureServices(collection =>
            {
                collection.AddAkka("WithContextTestSys", configurationBuilder =>
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
                            var testActor = system.ActorOf(Props.Create(() => new WithContextTestActor()), "withContextTestActor");
                            registry.TryRegister<WithContextTestActor>(testActor);
                        });
                });
            }).Build();
        await host.StartAsync();
        return host;
    }

    /// <summary>
    /// Actor that supports WithContext() enrichment.
    /// Message format: "ctx:Key1=Val1,Key2=Val2|template|arg1|arg2|..."
    /// Or without context: "template|arg1|arg2|..."
    /// </summary>
    private class WithContextTestActor : ReceiveActor
    {
        public WithContextTestActor()
        {
            var log = Context.GetLogger();
            Receive<string>(message =>
            {
                ILoggingAdapter currentLog = log;

                // Parse ctx: prefix for WithContext properties
                if (message.StartsWith("ctx:"))
                {
                    var ctxEnd = message.IndexOf('|');
                    var ctxPart = message.Substring(4, ctxEnd - 4);
                    message = message.Substring(ctxEnd + 1);

                    foreach (var kvp in ctxPart.Split(','))
                    {
                        var eqIdx = kvp.IndexOf('=');
                        var key = kvp.Substring(0, eqIdx);
                        var val = kvp.Substring(eqIdx + 1);
                        currentLog = currentLog.WithContext(key, val);
                    }
                }

                var parts = message.Split('|');
                var template = parts[0];
                var args = parts.Skip(1).Select(ParseArg).ToArray();

                if (args.Length > 0)
                    currentLog.Info(template, args);
                else
                    currentLog.Info(template);

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
