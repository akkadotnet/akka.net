//-----------------------------------------------------------------------
// <copyright file="LogEventActivityContextSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Diagnostics;
using Akka.Event;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Event
{
    public class LogEventActivityContextSpec
    {
        private static readonly ActivitySource TestSource = new("Akka.Tests.LogEventActivityContextSpec");

        static LogEventActivityContextSpec()
        {
            // Enable activity creation for tests
            ActivitySource.AddActivityListener(new ActivityListener
            {
                ShouldListenTo = source => source.Name == TestSource.Name,
                Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllData
            });
        }

        [Fact(DisplayName = "LogEvent should capture ActivityContext when Activity.Current exists")]
        public void LogEvent_should_capture_ActivityContext_when_Activity_exists()
        {
            using var activity = TestSource.StartActivity("TestOperation");
            activity.Should().NotBeNull("ActivityListener should be registered");

            var logEvent = new Info("test", typeof(LogEventActivityContextSpec), "test message");

            logEvent.ActivityContext.Should().NotBeNull();
            logEvent.ActivityContext.Value.TraceId.Should().Be(activity!.TraceId);
            logEvent.ActivityContext.Value.SpanId.Should().Be(activity.SpanId);
        }

        [Fact(DisplayName = "LogEvent should have null ActivityContext when no Activity.Current")]
        public void LogEvent_should_have_null_ActivityContext_when_no_Activity()
        {
            // Ensure no activity is current
            Activity.Current = null;

            var logEvent = new Info("test", typeof(LogEventActivityContextSpec), "test message");

            logEvent.ActivityContext.Should().BeNull();
        }

        [Fact(DisplayName = "LogEvent should capture context at creation time, not access time")]
        public void LogEvent_should_capture_context_at_creation_time()
        {
            using var activity = TestSource.StartActivity("TestOperation");
            activity.Should().NotBeNull();

            var logEvent = new Info("test", typeof(LogEventActivityContextSpec), "test message");
            var capturedTraceId = logEvent.ActivityContext!.Value.TraceId;

            // Stop the activity
            activity!.Stop();
            Activity.Current = null;

            // LogEvent should still have the original context
            logEvent.ActivityContext.Should().NotBeNull();
            logEvent.ActivityContext.Value.TraceId.Should().Be(capturedTraceId);
        }

        [Fact(DisplayName = "All LogEvent types should capture ActivityContext")]
        public void All_LogEvent_types_should_capture_ActivityContext()
        {
            using var activity = TestSource.StartActivity("TestOperation");
            activity.Should().NotBeNull();

            var debug = new Akka.Event.Debug("test", typeof(LogEventActivityContextSpec), "debug");
            var info = new Akka.Event.Info("test", typeof(LogEventActivityContextSpec), "info");
            var warning = new Akka.Event.Warning("test", typeof(LogEventActivityContextSpec), "warning");
            var error = new Akka.Event.Error(null, "test", typeof(LogEventActivityContextSpec), "error");

            debug.ActivityContext.Should().NotBeNull();
            info.ActivityContext.Should().NotBeNull();
            warning.ActivityContext.Should().NotBeNull();
            error.ActivityContext.Should().NotBeNull();

            debug.ActivityContext.Value.TraceId.Should().Be(activity!.TraceId);
            info.ActivityContext.Value.TraceId.Should().Be(activity.TraceId);
            warning.ActivityContext.Value.TraceId.Should().Be(activity.TraceId);
            error.ActivityContext.Value.TraceId.Should().Be(activity.TraceId);
        }
    }
}
