//-----------------------------------------------------------------------
// <copyright file="LoggerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using Akka.Tests.Shared.Internals;
using Xunit;
using FluentAssertions;

namespace Akka.Tests.Loggers
{
    public class LoggerSpec : AkkaSpec
    {
        private static readonly Config Config = ConfigurationFactory.ParseString(@"
akka.loglevel = DEBUG
akka.stdout-loglevel = DEBUG");

        public static readonly (string t, string[] p) Case =  
            ("This is {0} a {1} janky formatting. {4}", new []{"also", "very", "not cool"});

        public LoggerSpec(ITestOutputHelper output) : base(Config, output)
        { }

        [Fact]
        public async Task TestOutputLogger_WithBadFormattingMustNotThrow()
        {
            // Need to wait until TestOutputLogger initializes
            await Task.Delay(500);
            Sys.EventStream.Subscribe(TestActor, typeof(LogEvent));

            // Bad format strings should now produce diagnostic messages rather than throwing FormatException.
            // Only the original log event should be published (no secondary Error event).
            Sys.Log.Error(new FakeException("BOOM"), Case.t, Case.p);
            var errorEvt = await ExpectMsgAsync<Error>();
            errorEvt.Cause.Should().BeOfType<FakeException>();
            errorEvt.ToString().Should().Contain("[INVALID LOG FORMAT]");

            Sys.Log.Warning(Case.t, Case.p);
            var warningEvt = await ExpectMsgAsync<Warning>();
            warningEvt.ToString().Should().Contain("[INVALID LOG FORMAT]");

            Sys.Log.Info(Case.t, Case.p);
            var infoEvt = await ExpectMsgAsync<Info>();
            infoEvt.ToString().Should().Contain("[INVALID LOG FORMAT]");

            Sys.Log.Debug(Case.t, Case.p);
            var debugEvt = await ExpectMsgAsync<Debug>();
            debugEvt.ToString().Should().Contain("[INVALID LOG FORMAT]");
        }

        [Fact]
        public async Task DefaultLogger_WithBadFormattingMustNotThrow()
        {
            var config = ConfigurationFactory.ParseString("akka.loggers = [\"Akka.Event.DefaultLogger\"]");
            var sys2 = ActorSystem.Create("DefaultLoggerTest", config.WithFallback(Sys.Settings.Config));
            var probe = CreateTestProbe(sys2);

            sys2.EventStream.Subscribe(probe, typeof(LogEvent));

            sys2.Log.Error(new FakeException("BOOM"), Case.t, Case.p);
            (await probe.ExpectMsgAsync<Error>()).Cause.Should().BeOfType<FakeException>();

            sys2.Log.Warning(Case.t, Case.p);
            await probe.ExpectMsgAsync<Warning>();

            sys2.Log.Info(Case.t, Case.p);
            await probe.ExpectMsgAsync<Info>();

            sys2.Log.Debug(Case.t, Case.p);
            await probe.ExpectMsgAsync<Debug>();

            await sys2.Terminate();
        }

        [Fact]
        public async Task StandardOutLogger_WithBadFormattingMustNotThrow()
        {
            var config = ConfigurationFactory.ParseString("akka.loggers = [\"Akka.Event.StandardOutLogger\"]");
            var sys2 = ActorSystem.Create("StandardOutLoggerTest", config.WithFallback(Sys.Settings.Config));
            var probe = CreateTestProbe(sys2);

            sys2.EventStream.Subscribe(probe, typeof(LogEvent));

            sys2.Log.Error(new FakeException("BOOM"), Case.t, Case.p);
            (await probe.ExpectMsgAsync<Error>()).Cause.Should().BeOfType<FakeException>();

            sys2.Log.Warning(Case.t, Case.p);
            await probe.ExpectMsgAsync<Warning>();

            sys2.Log.Info(Case.t, Case.p);
            await probe.ExpectMsgAsync<Info>();

            sys2.Log.Debug(Case.t, Case.p);
            await probe.ExpectMsgAsync<Debug>();

            await sys2.Terminate();
        }

        [Theory]
        [MemberData(nameof(LogEventFactory))]
        public void StandardOutLogger_PrintLogEvent_WithBadLogFormattingMustNotThrow(LogEvent @event)
        {
            var obj = new object();
            obj.Invoking(_ => StandardOutLogger.PrintLogEvent(@event, LogFilterEvaluator.NoFilters)).Should().NotThrow();
        }

        public static IEnumerable<object[]> LogEventFactory()
        {
            var ex = new FakeException("BOOM");
            var logSource = LogSource.Create(nameof(LoggerSpec));
            var ls = logSource.Source;
            var lc = logSource.Type;
            var formatter =  DefaultLogMessageFormatter.Instance;

            yield return new object[] { new Error(ex, ls, lc, new DefaultLogMessage(formatter, Case.t, Case.p)) }; 

            yield return new object[] {new Warning(ex, ls, lc, new DefaultLogMessage(formatter, Case.t, Case.p))};

            yield return new object[] {new Info(ex, ls, lc, new DefaultLogMessage(formatter, Case.t, Case.p))};

            yield return new object[] {new Debug(ex, ls, lc, new DefaultLogMessage(formatter, Case.t, Case.p))};
        }

        private class FakeException : Exception
        {
            public FakeException(string message) : base(message)
            { }
        }
        
        [Fact]
        public async Task ShouldBeAbleToReplaceStandardOutLoggerWithCustomMinimalLogger()
        {
            var config = ConfigurationFactory
                .ParseString("akka.stdout-logger-class = \"Akka.Tests.Loggers.LoggerSpec+CustomLogger, Akka.Tests\"")
                .WithFallback(ConfigurationFactory.Default()); 
            
            var system = ActorSystem.Create("MinimalLoggerTest", config);
            system.Settings.StdoutLogger.Should().BeOfType<CustomLogger>();
            await system.Terminate();
        }
        
        public class CustomLogger : MinimalLogger
        {
            protected override void Log(object message)
            {
                Console.WriteLine(message);
            }
        }
        
    }
}
