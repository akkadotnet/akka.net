//-----------------------------------------------------------------------
// <copyright file="FlowLogSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Event;
using Akka.Streams.Dsl;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowLogSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowLogSpec(ITestOutputHelper helper) : base("akka.loglevel = DEBUG", helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys, settings);

            var p = CreateTestProbe();
            Sys.EventStream.Subscribe(p.Ref, typeof(LogEvent));
            LogProbe = p;
        }

        private TestProbe LogProbe { get; }

        private async Task<string[]> LogMessagesAsync(int n, string tag)
            => (await LogEventsAsync<Debug>(n, tag)).Select(x => x.Message.ToString()).ToArray();

        private async Task<T[]> LogEventsAsync<T>(int n, string tag) where T : LogEvent
        {
            var messages = new List<T>();
            while (messages.Count < n)
            {
                var message = await LogProbe.FishForMessageAsync<T>(x => x.Message.ToString().StartsWith(tag));
                messages.Add(message);
            }

            return messages.ToArray();
        }

        private Type LogType => typeof (IMaterializer);

        [Fact]
        public async Task A_Log_on_Flow_must_debug_each_element()
        {
            var debugging = Flow.Create<int>().Log("my-debug");
            _ = Source.From(new[] {1, 2}).Via(debugging).RunWith(Sink.Ignore<int>(), Materializer);

            var msgs = await LogMessagesAsync(3, "[my-debug]");
            msgs[0].Should().Be("[my-debug] Element: 1");
            msgs[1].Should().Be("[my-debug] Element: 2");
            msgs[2].Should().Be("[my-debug] Upstream finished.");
        }

        [Fact]
        public async Task A_Log_on_Flow_must_allow_disabling_elements_logging()
        {
            var disableElementLogging = Attributes.CreateLogLevels(Attributes.LogLevels.Off, LogLevel.DebugLevel,
                LogLevel.DebugLevel);
            var debugging = Flow.Create<int>().Log("my-debug");
            _ = Source.From(new[] {1, 2})
                .Via(debugging)
                .WithAttributes(disableElementLogging)
                .RunWith(Sink.Ignore<int>(), Materializer);

            var msgs = await LogMessagesAsync(1, "[my-debug]");
            msgs[0].Should().Be("[my-debug] Upstream finished.");
        }



        [Fact]
        public async Task A_Log_on_source_must_debug_each_element()
        {
            _ = Source.From(new[] {1, 2}).Log("flow-s2").RunWith(Sink.Ignore<int>(), Materializer);

            var msgs = await LogMessagesAsync(3, "[flow-s2]");
            msgs[0].Should().Be("[flow-s2] Element: 1");
            msgs[1].Should().Be("[flow-s2] Element: 2");
            msgs[2].Should().Be("[flow-s2] Upstream finished.");
        }

        [Fact]
        public async Task A_Log_on_source_must_allow_extracting_value_to_be_logged()
        {
            _ = Source.Single((1, "42"))
                .Log("flow-s3", t => t.Item2)
                .RunWith(Sink.Ignore<(int, string)>(), Materializer);

            var msgs = await LogMessagesAsync(2, "[flow-s3]");
            msgs[0].Should().Be("[flow-s3] Element: 42");
            msgs[1].Should().Be("[flow-s3] Upstream finished.");
        }

        [Fact]
        public async Task A_Log_on_source_must_log_upstream_failure()
        {
            var cause = new TestException("test");
            _ = Source.Failed<int>(cause)
                .Log("flow-s4")
                .RunWith(Sink.Ignore<int>(), Materializer);

            var error = await LogProbe.FishForMessageAsync<Error>(x => x.Message.ToString().StartsWith("[flow-s4]"));
            error.Cause.Should().Be(cause);
            error.Message.ToString().Should().Be("[flow-s4] Upstream failed.");
        }

        [Fact]
        public async Task A_Log_on_source_must_allow_passing_in_custom_LoggingAdapter()
        {
            var log = new BusLogging(Sys.EventStream, "com.example.ImportantLogger", LogType, DefaultLogMessageFormatter.Instance);

            _ = Source.Single(42)
                .Log("flow-5", log: log)
                .RunWith(Sink.Ignore<int>(), Materializer);

            var msgs = await LogEventsAsync<Debug>(2, "[flow-5]");
            msgs.All(m => m.LogSource.Equals("com.example.ImportantLogger")).Should().BeTrue();
            msgs.All(m => m.LogClass == LogType).Should().BeTrue();
            msgs[0].Message.ToString().Should().Be("[flow-5] Element: 42");
            msgs[1].Message.ToString().Should().Be("[flow-5] Upstream finished.");
        }

        [Fact]
        public async Task A_Log_on_source_must_allow_configuring_log_levels_via_Attributes()
        {
            var logAttributes = Attributes.CreateLogLevels(LogLevel.WarningLevel, LogLevel.InfoLevel,
                LogLevel.DebugLevel);

            _ = Source.Single(42)
                .Log("flow-6")
                .WithAttributes(Attributes.CreateLogLevels(LogLevel.WarningLevel, LogLevel.InfoLevel,
                    LogLevel.DebugLevel))
                .RunWith(Sink.Ignore<int>(), Materializer);

            var warnings = await LogEventsAsync<Warning>(1, "[flow-6]");
            warnings[0].Message.ToString().Should().Be("[flow-6] Element: 42");

            var info = await LogEventsAsync<Info>(1, "[flow-6]");
            info[0].Message.ToString().Should().Be("[flow-6] Upstream finished.");

            var cause = new TestException("test");
            _ = Source.Failed<int>(cause)
                .Log("flow-6e")
                .WithAttributes(logAttributes)
                .RunWith(Sink.Ignore<int>(), Materializer);

            var error = await LogEventsAsync<Debug>(1, "[flow-6e]");
            error[0].Message.ToString().Should().Be("[flow-6e] Upstream failed, cause: Akka.Streams.TestKit.TestException test");
        }

        [Fact]
        public async Task A_Log_on_source_must_allow_configuring_log_levels_via_Method_argument()
        {
            _ = Source.Single(42)
                .Log("flow-6", logLevel: LogLevel.WarningLevel)
                .RunWith(Sink.Ignore<int>(), Materializer);

            var warnings = await LogEventsAsync<Warning>(1, "[flow-6]");
            warnings[0].Message.ToString().Should().Be("[flow-6] Element: 42");
        }

        [Fact]
        public void A_Log_on_Source_must_follow_supervision_strategy_when_Exception_thrown()
        {
            var ex = new TestException("test");
            var future = Source.From(Enumerable.Range(1, 5))
                .Log("hi", _ => { throw ex; })
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                .RunWith(Sink.Aggregate<int, int>(0, (i, i1) => i + i1), Materializer);

            future.Wait(TimeSpan.FromMilliseconds(500)).Should().BeTrue();
            future.Result.Should().Be(0);
        }
    }
}
