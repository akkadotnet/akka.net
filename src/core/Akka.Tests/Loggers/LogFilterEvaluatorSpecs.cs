// -----------------------------------------------------------------------
//  <copyright file="LogFilterEvaluatorSpecs.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;
using Xunit;

namespace Akka.Tests.Loggers;

/// <summary>
/// Goal of these specs are mostly to make sure our default <see cref="Regex"/>s are working as expected
/// </summary>
public class LogFilterEvaluatorSpecs
{
    public class LogSourceCases
    {
        public static readonly TheoryData<LogEvent, bool> LogSourceContainsCases = new()
        {
            {
                // exact match
                new Debug("Akka.Tests", typeof(IActorRef), "TEST"), true
            },
            {
                // test with stuff after the match
                new Debug("Akka.Tests.Test2", typeof(IActorRef), "TEST"), true
            },
            {
                // test with stuff before the match
                new Debug("LOL.Akka.Tests", typeof(IActorRef), "TEST"), true
            },
            { new Debug("Akka.Util", typeof(IActorRef), "TEST"), false }
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
                new Debug("Akka.Tests", typeof(IActorRef), "TEST"), true
            },
            {
                // test with stuff after the match
                new Debug("Akka.Tests.Test2", typeof(IActorRef), "TEST"), true
            },
            {
                // test with stuff before the match
                new Debug("LOL.Akka.Tests", typeof(IActorRef), "TEST"), false
            },
            { new Debug("Akka.Util", typeof(IActorRef), "TEST"), false }
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
                new Debug("Akka.Tests", typeof(IActorRef), "TEST"), true
            },
            {
                // test with stuff after the match
                new Debug("Akka.Tests.Test2", typeof(IActorRef), "TEST"), false
            },
            {
                // test with stuff before the match
                new Debug("LOL.Akka.Tests", typeof(IActorRef), "TEST"), true
            },
            { new Debug("Akka.Util", typeof(IActorRef), "TEST"), false }
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
                new Debug("Akka.Tests", typeof(IActorRef), "TEST"), true
            },
            {
                // test with stuff after the match
                new Debug("Akka.Tests", typeof(IActorRef), "TEST2"), true
            },
            {
                // test with stuff before the match
                new Debug("Akka.Tests", typeof(IActorRef), "LOLTEST"), true
            },
            { new Debug("Akka.Tests", typeof(IActorRef), "LOL"), false }
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
        
        // add a test case for LogMessage starts with
        public static readonly TheoryData<LogEvent, bool> LogMessageStartsWithCases = new()
        {
            {
                // exact match
                new Debug("Akka.Tests", typeof(IActorRef), "TEST"), true
            },
            {
                // test with stuff after the match
                new Debug("Akka.Tests", typeof(IActorRef), "TEST2"), true
            },
            {
                // test with stuff before the match
                new Debug("Akka.Tests", typeof(IActorRef), "LOLTEST"), false
            },
            { new Debug("Akka.Tests", typeof(IActorRef), "LOL"), false }
        };
    }
}