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
}
