using System;
using System.Collections.Generic;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;
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
        public void TestOutputLogger_WithBadFormattingMustNotThrow()
        {
            // Need to wait until TestOutputLogger initializes
            Thread.Sleep(200);
            Sys.EventStream.Subscribe(TestActor, typeof(LogEvent));

            Sys.Log.Error(new FakeException("BOOM"), Case.t, Case.p);
            ExpectMsg<Error>().Cause.Should().BeOfType<FakeException>();
            ExpectMsg<Error>().Cause.Should().BeOfType<AggregateException>();

            Sys.Log.Warning(Case.t, Case.p);
            ExpectMsg<Warning>();
            ExpectMsg<Error>().Cause.Should().BeOfType<FormatException>();

            Sys.Log.Info(Case.t, Case.p);
            ExpectMsg<Info>();
            ExpectMsg<Error>().Cause.Should().BeOfType<FormatException>();

            Sys.Log.Debug(Case.t, Case.p);
            ExpectMsg<Debug>();
            ExpectMsg<Error>().Cause.Should().BeOfType<FormatException>();
        }

        [Fact]
        public void DefaultLogger_WithBadFormattingMustNotThrow()
        {
            var config = ConfigurationFactory.ParseString("akka.loggers = [\"Akka.Event.DefaultLogger\"]");
            var sys2 = ActorSystem.Create("DefaultLoggerTest", config.WithFallback(Sys.Settings.Config));
            var probe = CreateTestProbe(sys2);

            sys2.EventStream.Subscribe(probe, typeof(LogEvent));

            sys2.Log.Error(new FakeException("BOOM"), Case.t, Case.p);
            probe.ExpectMsg<Error>().Cause.Should().BeOfType<FakeException>();

            sys2.Log.Warning(Case.t, Case.p);
            probe.ExpectMsg<Warning>();

            sys2.Log.Info(Case.t, Case.p);
            probe.ExpectMsg<Info>();

            sys2.Log.Debug(Case.t, Case.p);
            probe.ExpectMsg<Debug>();

            sys2.Terminate().Wait();
        }

        [Fact]
        public void StandardOutLogger_WithBadFormattingMustNotThrow()
        {
            var config = ConfigurationFactory.ParseString("akka.loggers = [\"Akka.Event.StandardOutLogger\"]");
            var sys2 = ActorSystem.Create("StandardOutLoggerTest", config.WithFallback(Sys.Settings.Config));
            var probe = CreateTestProbe(sys2);

            sys2.EventStream.Subscribe(probe, typeof(LogEvent));

            sys2.Log.Error(new FakeException("BOOM"), Case.t, Case.p);
            probe.ExpectMsg<Error>().Cause.Should().BeOfType<FakeException>();

            sys2.Log.Warning(Case.t, Case.p);
            probe.ExpectMsg<Warning>();

            sys2.Log.Info(Case.t, Case.p);
            probe.ExpectMsg<Info>();

            sys2.Log.Debug(Case.t, Case.p);
            probe.ExpectMsg<Debug>();

            sys2.Terminate().Wait();
        }

        [Theory]
        [MemberData(nameof(LogEventFactory))]
        public void StandardOutLogger_PrintLogEvent_WithBadLogFormattingMustNotThrow(LogEvent @event)
        {
            var obj = new object();
            obj.Invoking(o => StandardOutLogger.PrintLogEvent(@event)).Should().NotThrow();
        }

        public static IEnumerable<object[]> LogEventFactory()
        {
            var ex = new FakeException("BOOM");
            var logSource = LogSource.Create(nameof(LoggerSpec));
            var ls = logSource.Source;
            var lc = logSource.Type;
            var formatter = new DefaultLogMessageFormatter();

            yield return new object[] { new Error(ex, ls, lc, new LogMessage(formatter, Case.t, Case.p)) }; 

            yield return new object[] {new Warning(ex, ls, lc, new LogMessage(formatter, Case.t, Case.p))};

            yield return new object[] {new Info(ex, ls, lc, new LogMessage(formatter, Case.t, Case.p))};

            yield return new object[] {new Debug(ex, ls, lc, new LogMessage(formatter, Case.t, Case.p))};
        }

        private class FakeException : Exception
        {
            public FakeException(string message) : base(message)
            { }
        }
    }
}
