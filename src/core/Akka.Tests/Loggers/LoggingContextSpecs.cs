// <copyright file="LoggingContextSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Loggers
{
    public class LoggingContextSpecs : AkkaSpec
    {
        private static readonly Config SpecConfig = ConfigurationFactory.ParseString(@"
            akka.loglevel = INFO
            akka.stdout-loglevel = OFF
            akka.loggers = []
        ");

        public LoggingContextSpecs(ITestOutputHelper output) : base(SpecConfig, output)
        {
        }

        #region LoggingContextExample
        private void LoggingContextExample()
        {
            var log = Logging.GetLogger(Sys, "example");

            var enrichedLog = log
                .WithContext("Tenant", "foo")
                .WithContext("Partition", 12);

            enrichedLog.Info("Processing {Offset}", 42);

            using (var scope = log.BeginScope("RequestId", "REQ-123"))
            {
                scope.Log.Info("Handling request {RequestId}", "REQ-123");
            }

            var behaviorLog = log.WithPrefix("stoppingBehavior");
            behaviorLog.Info("Shutdown requested");
        }
        #endregion

        [Fact(DisplayName = "ILoggingAdapter.WithContext should append properties and display segments")]
        public void WithContext_should_append_properties_and_display_segments()
        {
            Sys.EventStream.Subscribe(TestActor, typeof(LogEvent));
            var log = Logging.GetLogger(Sys, "context-spec");
            var contextLog = log
                .WithContext("Tenant", "foo")
                .WithContext("Partition", 12);

            contextLog.Info("Processing {RequestId}", 123);

            var logEvent = ExpectMsg<Info>(e => e.Message.ToString().Contains("Processing"));

            logEvent.LogSource.Should().Be("context-spec");
            logEvent.TryGetProperties(out var properties).Should().BeTrue();
            properties.Should().ContainKey("RequestId").WhoseValue.Should().Be(123);
            properties.Should().ContainKey("Tenant").WhoseValue.Should().Be("foo");
            properties.Should().ContainKey("Partition").WhoseValue.Should().Be(12);

            var display = logEvent.ToDisplayString();
            display.Should().Contain("[context-spec]");
            display.Should().Contain("[Tenant=foo]");
            display.Should().Contain("[Partition=12]");
        }

        [Fact(DisplayName = "ILoggingAdapter.BeginScope should apply context only within scope")]
        public void BeginScope_should_apply_context_only_within_scope()
        {
            Sys.EventStream.Subscribe(TestActor, typeof(LogEvent));
            var log = Logging.GetLogger(Sys, "scope-spec");

            using (var scope = log.BeginScope("Tenant", "foo"))
            {
                scope.Log.Info("Scoped {Id}", 1);
                var scopedEvent = ExpectMsg<Info>(e => e.Message.ToString().Contains("Scoped"));
                scopedEvent.TryGetProperties(out var scopedProperties).Should().BeTrue();
                scopedProperties.Should().ContainKey("Tenant").WhoseValue.Should().Be("foo");
            }

            log.Info("Outside {Id}", 2);
            var outsideEvent = ExpectMsg<Info>(e => e.Message.ToString().Contains("Outside"));
            outsideEvent.TryGetProperties(out var outsideProperties).Should().BeTrue();
            outsideProperties.Should().NotContainKey("Tenant");
        }

        [Fact(DisplayName = "ILoggingAdapter.WithPrefix should prefix the message")]
        public void WithPrefix_should_prefix_message()
        {
            Sys.EventStream.Subscribe(TestActor, typeof(LogEvent));
            var log = Logging.GetLogger(Sys, "prefix-spec");
            var prefixedLog = log.WithPrefix("stoppingBehavior");

            prefixedLog.Info("State {State}", "Stopping");

            var logEvent = ExpectMsg<Info>(e => e.Message.ToString().Contains("Stopping"));
            logEvent.Message.ToString().Should().StartWith("stoppingBehavior: ");
        }
    }
}
