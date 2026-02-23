//-----------------------------------------------------------------------
// <copyright file="LogFilterEvaluatorSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Loggers;

/// <summary>
/// Goal of these specs are mostly to make sure our default <see cref="Regex"/>s are working as expected
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public class LogFilterEvaluatorSpecs
{
    public class LogFilterSetupSpecs : AkkaSpec
    {
        // <CreateLoggerSetup>
        public static Setup LoggerSetup()
        {
            
            var builder = new LogFilterBuilder();
            builder.ExcludeSourceContaining("Akka.Tests")
                .ExcludeMessageContaining("foo-bar");
            return builder.Build();
        }
        // </CreateLoggerSetup>
        
        // <ActorSystemSetup>
        public static ActorSystemSetup CustomLoggerSetup()
        {
            var hocon = @$"akka.stdout-logger-class = ""{typeof(CustomLogger).AssemblyQualifiedName}""";
            var bootstrapSetup = BootstrapSetup.Create().WithConfig(ConfigurationFactory.ParseString(hocon));
            return ActorSystemSetup.Create(bootstrapSetup, LoggerSetup());
        }
        // </ActorSystemSetup>
        
        // create a custom MinimalLogger that subclasses StandardOutLogger
        public class CustomLogger : StandardOutLogger
        {
            protected override void Log(object message)
            {
                if (message is LogEvent e)
                {
                    if (Filter.ShouldTryKeepMessage(e, out _))
                    {
                        _events.Add(e);
                    }
                }
               
            }
            
            private readonly List<LogEvent> _events = new();
            public IReadOnlyList<LogEvent> Events => _events;
        }
    
        public LogFilterSetupSpecs(ITestOutputHelper output) : base(CustomLoggerSetup(),
            output: output)
        {
            _logger = (CustomLogger)Sys.Settings.StdoutLogger;
        }

        private readonly CustomLogger _logger;
        
        [Fact]
        public async Task LogFilterEnd2EndSpec()
        {
            // subscribe to warning level log events
            Sys.EventStream.Subscribe(TestActor, typeof(Warning));
            
            // produce three warning messages - that hits the source filter, another that hits the message filter, and a third that hits neither
            var loggingAdapter1 = Logging.GetLogger(Sys, "Akka.Tests.Test1");
            var loggingAdapter2 = Logging.GetLogger(Sys, "Akka.Util.Test2");
            
            // should be filtered out based on Source
            loggingAdapter1.Warning("test");
            
            // should be filtered out based on message content
            loggingAdapter2.Warning("foo-bar");
            
            // should be allowed through
            loggingAdapter2.Warning("baz");
            
            // expect only the last message to be received
            ReceiveN(3);
            
            // check that the last message was the one that was allowed through
            await AwaitAssertAsync(() => _logger.Events.Count.Should().Be(1));
            var msg = _logger.Events[0];
            msg.Message.Should().Be("baz");
            msg.LogSource.Should().StartWith("Akka.Util.Test2");
        }
    }

    public class LogSourceCases
    {
        public static readonly TheoryData<LogEvent, bool> LogSourceContainsCases = new()
        {
            {
                // exact match (text, not case)
                new Debug("Akka.Tests", typeof(IActorRef), "TEST"), false
            },
            {
                // test with stuff after the match
                new Debug("Akka.Tests.Test2", typeof(IActorRef), "TEST"), false
            },
            {
                // test with stuff before the match
                new Debug("LOL.Akka.Tests", typeof(IActorRef), "TEST"), false
            },
            { new Debug("Akka.Util", typeof(IActorRef), "TEST"), true }
        };

        [Theory]
        [MemberData(nameof(LogSourceContainsCases))]
        public void ShouldFilterByLogSourceContains(LogEvent e, bool expected)
        {
            var ruleBuilder = new LogFilterBuilder().ExcludeSourceContaining("Akka.Tests");
            var evaluator = ruleBuilder.Build().CreateEvaluator();

            var keepMessage = evaluator.ShouldTryKeepMessage(e, out _);

            Assert.Equal(expected, keepMessage);
        }

        // add a test case for LogSource starts with
        public static readonly TheoryData<LogEvent, bool> LogSourceStartsWithCases = new()
        {
            {
                // exact match
                new Debug("Akka.Tests", typeof(IActorRef), "TEST"), false
            },
            {
                // test with stuff after the match
                new Debug("Akka.Tests.Test2", typeof(IActorRef), "TEST"), false
            },
            {
                // test with stuff before the match
                new Debug("LOL.Akka.Tests", typeof(IActorRef), "TEST"), true
            },
            { new Debug("Akka.Util", typeof(IActorRef), "TEST"), true }
        };

        [Theory]
        [MemberData(nameof(LogSourceStartsWithCases))]
        public void ShouldFilterByLogSourceStartsWith(LogEvent e, bool expected)
        {
            var ruleBuilder = new LogFilterBuilder().ExcludeSourceStartingWith("Akka.Tests");
            var evaluator = ruleBuilder.Build().CreateEvaluator();

            var keepMessage = evaluator.ShouldTryKeepMessage(e, out _);

            Assert.Equal(expected, keepMessage);
        }

        // add a test case for LogSource ends with
        public static readonly TheoryData<LogEvent, bool> LogSourceEndsWithCases = new()
        {
            {
                // exact match
                new Debug("Akka.Tests", typeof(IActorRef), "TEST"), false
            },
            {
                // test with stuff after the match
                new Debug("Akka.Tests.Test2", typeof(IActorRef), "TEST"), true
            },
            {
                // test with stuff before the match
                new Debug("LOL.Akka.Tests", typeof(IActorRef), "TEST"), false
            },
            { new Debug("Akka.Util", typeof(IActorRef), "TEST"), true }
        };

        [Theory]
        [MemberData(nameof(LogSourceEndsWithCases))]
        public void ShouldFilterByLogSourceEndsWith(LogEvent e, bool expected)
        {
            var ruleBuilder = new LogFilterBuilder().ExcludeSourceEndingWith("Akka.Tests");
            var evaluator = ruleBuilder.Build().CreateEvaluator();

            var keepMessage = evaluator.ShouldTryKeepMessage(e, out _);

            Assert.Equal(expected, keepMessage);
        }
    }

    public class LogMessageCases
    {
        public static readonly TheoryData<LogEvent, bool> LogMessageContainsCases = new()
        {
            {
                // exact match
                new Debug("Akka.Tests", typeof(IActorRef), "TEST"), false
            },
            {
                // test with stuff after the match
                new Debug("Akka.Tests", typeof(IActorRef), "TEST2"), false
            },
            {
                // test with stuff before the match
                new Debug("Akka.Tests", typeof(IActorRef), "LOLTEST"), false
            },
            { new Debug("Akka.Tests", typeof(IActorRef), "LOL"), true }
        };

        [Theory]
        [MemberData(nameof(LogMessageContainsCases))]
        public void ShouldFilterByLogMessageContains(LogEvent e, bool expected)
        {
            var ruleBuilder = new LogFilterBuilder().ExcludeMessageContaining("TEST");
            var evaluator = ruleBuilder.Build().CreateEvaluator();

            var keepMessage = evaluator.ShouldTryKeepMessage(e, out _);

            Assert.Equal(expected, keepMessage);
        }
    }

    /// <summary>
    /// Tests that log filtering works correctly with semantic logging templates
    /// </summary>
    public class SemanticLoggingFilterCases
    {
        private static ILoggingAdapter CreateAdapter()
        {
            var system = ActorSystem.Create("test-system");
            return Logging.GetLogger(system, "TestLogger");
        }

        [Fact]
        public void ShouldFilterSemanticLogByFormattedMessageContent()
        {
            // Arrange: filter should exclude messages containing "12345"
            var ruleBuilder = new LogFilterBuilder().ExcludeMessageContaining("12345");
            var evaluator = ruleBuilder.Build().CreateEvaluator();

            var adapter = CreateAdapter();

            // Act: log with semantic template - the formatted value contains "12345"
            adapter.Info("User {UserId} logged in", 12345);

            // Get the LogEvent that was created
            var logEvent = new Info(
                null,
                "TestLogger",
                typeof(SemanticLoggingFilterCases),
                new DefaultLogMessage(
                    SemanticLogMessageFormatter.Instance,
                    "User {UserId} logged in",
                    12345));

            // Assert: should be filtered out because formatted message contains "12345"
            var keepMessage = evaluator.ShouldTryKeepMessage(logEvent, out _);
            Assert.False(keepMessage);
        }

        [Fact]
        public void ShouldKeepSemanticLogWhenFormattedMessageDoesNotMatchFilter()
        {
            // Arrange: filter excludes messages containing "admin"
            var ruleBuilder = new LogFilterBuilder().ExcludeMessageContaining("admin");
            var evaluator = ruleBuilder.Build().CreateEvaluator();

            // Act: log message where neither template nor formatted value contains "admin"
            var logEvent = new Info(
                null,
                "TestLogger",
                typeof(SemanticLoggingFilterCases),
                new DefaultLogMessage(
                    SemanticLogMessageFormatter.Instance,
                    "User {UserId} logged in from {IpAddress}",
                    123, "192.168.1.1"));

            // Assert: should NOT be filtered (kept)
            var keepMessage = evaluator.ShouldTryKeepMessage(logEvent, out _);
            Assert.True(keepMessage);
        }

        [Fact]
        public void ShouldFilterSemanticLogByPropertyValue()
        {
            // Arrange: filter excludes messages containing "CRITICAL"
            var ruleBuilder = new LogFilterBuilder().ExcludeMessageContaining("CRITICAL");
            var evaluator = ruleBuilder.Build().CreateEvaluator();

            // Act: the property value contains "CRITICAL"
            var logEvent = new Warning(
                null,
                "TestLogger",
                typeof(SemanticLoggingFilterCases),
                new DefaultLogMessage(
                    SemanticLogMessageFormatter.Instance,
                    "Alert level {AlertLevel} triggered",
                    "CRITICAL"));

            // Assert: should be filtered because formatted message is "Alert level CRITICAL triggered"
            var keepMessage = evaluator.ShouldTryKeepMessage(logEvent, out _);
            Assert.False(keepMessage);
        }

        [Fact]
        public void ShouldFilterSemanticLogWithMultipleProperties()
        {
            // Arrange: filter excludes messages containing "ERROR"
            var ruleBuilder = new LogFilterBuilder().ExcludeMessageContaining("ERROR");
            var evaluator = ruleBuilder.Build().CreateEvaluator();

            // Act: multiple properties, one contains "ERROR"
            var logEvent = new Error(
                null,
                "TestLogger",
                typeof(SemanticLoggingFilterCases),
                new DefaultLogMessage(
                    SemanticLogMessageFormatter.Instance,
                    "Status: {Status}, Code: {ErrorCode}, User: {UserId}",
                    "ERROR", 500, 789));

            // Assert: should be filtered
            var keepMessage = evaluator.ShouldTryKeepMessage(logEvent, out _);
            Assert.False(keepMessage);
        }

        [Fact]
        public void ShouldHandlePositionalTemplatesWithFiltering()
        {
            // Arrange: filter excludes "timeout"
            var ruleBuilder = new LogFilterBuilder().ExcludeMessageContaining("timeout");
            var evaluator = ruleBuilder.Build().CreateEvaluator();

            // Act: positional template (backward compatibility)
            var logEvent = new Warning(
                null,
                "TestLogger",
                typeof(SemanticLoggingFilterCases),
                new DefaultLogMessage(
                    SemanticLogMessageFormatter.Instance,
                    "Operation {0} failed with {1}",
                    "database query", "timeout"));

            // Assert: should be filtered because formatted contains "timeout"
            var keepMessage = evaluator.ShouldTryKeepMessage(logEvent, out _);
            Assert.False(keepMessage);
        }

        [Fact]
        public void ShouldFilterBySourceWithSemanticLogging()
        {
            // Arrange: filter excludes source starting with "Akka.Tests"
            var ruleBuilder = new LogFilterBuilder().ExcludeSourceStartingWith("Akka.Tests");
            var evaluator = ruleBuilder.Build().CreateEvaluator();

            // Act: semantic log from filtered source
            var logEvent = new Info(
                null,
                "Akka.Tests.MyTest",
                typeof(SemanticLoggingFilterCases),
                new DefaultLogMessage(
                    SemanticLogMessageFormatter.Instance,
                    "Test user {UserId} created",
                    999));

            // Assert: should be filtered by source (message content irrelevant)
            var keepMessage = evaluator.ShouldTryKeepMessage(logEvent, out _);
            Assert.False(keepMessage);
        }

        [Fact]
        public void ShouldKeepSemanticLogWhenSourceAndMessagePass()
        {
            // Arrange: filter excludes "ERROR" in message and "Akka.Tests" in source
            var ruleBuilder = new LogFilterBuilder()
                .ExcludeMessageContaining("ERROR")
                .ExcludeSourceContaining("Akka.Tests");
            var evaluator = ruleBuilder.Build().CreateEvaluator();

            // Act: semantic log that doesn't match either filter
            var logEvent = new Info(
                null,
                "Akka.Cluster.Gossip",
                typeof(SemanticLoggingFilterCases),
                new DefaultLogMessage(
                    SemanticLogMessageFormatter.Instance,
                    "Node {NodeAddress} joined cluster",
                    "akka.tcp://system@localhost:8080"));

            // Assert: should NOT be filtered (kept)
            var keepMessage = evaluator.ShouldTryKeepMessage(logEvent, out _);
            Assert.True(keepMessage);
        }

        [Fact]
        public void ShouldFilterComplexSemanticLogWithFormatSpecifiers()
        {
            // Arrange: filter excludes messages containing "1,234.56" (formatted number)
            var ruleBuilder = new LogFilterBuilder().ExcludeMessageContaining("1,234.56");
            var evaluator = ruleBuilder.Build().CreateEvaluator();

            // Act: semantic template with format specifier
            var logEvent = new Info(
                null,
                "TestLogger",
                typeof(SemanticLoggingFilterCases),
                new DefaultLogMessage(
                    SemanticLogMessageFormatter.Instance,
                    "Amount {Amount:N2} processed",
                    1234.56m));

            // Assert: should be filtered because formatted output contains "1,234.56"
            var keepMessage = evaluator.ShouldTryKeepMessage(logEvent, out _);
            Assert.False(keepMessage);
        }
    }
}
