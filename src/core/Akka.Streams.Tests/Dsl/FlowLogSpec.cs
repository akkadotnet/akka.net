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
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowLogSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowLogSpec(ITestOutputHelper helper) : base("akka.loglevel = DEBUG", helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        private TestProbe _logProbe;

        protected override void AtStartup()
        {
            base.AtStartup();
            
            var p = CreateTestProbe();
            Sys.EventStream.Subscribe(p.Ref, typeof(LogEvent));
            _logProbe = p;
        }

        private async Task<string[]> LogMessages(int n, string tag)
        {
            return (await LogEvents<Debug>(n, tag)).Select(m => m.Message.ToString()).ToArray();
        }

        private async Task<T[]> LogEvents<T>(int n, string tag) where T : LogEvent
        {
            var count = 0;
            var messages = new List<T>();
            while (count < n)
            {
                var msg = (T) await _logProbe.FishForMessageAsync(m => m is T d && d.Message.ToString().StartsWith(tag));
                count++;
                messages.Add(msg);
            }
            return messages.ToArray();
        }

        private static readonly Type LogType = typeof (IMaterializer);

        [Fact]
        public async Task A_Log_on_Flow_must_debug_each_element()
        {
            var debugging = Flow.Create<int>().Log("my-debug");
            _ = Source.From([1, 2]).Via(debugging).RunWith(Sink.Ignore<int>(), Materializer);

            var msgs = await LogMessages(3, "[my-debug]");
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
            _ = Source.From([1, 2])
                .Via(debugging)
                .WithAttributes(disableElementLogging)
                .RunWith(Sink.Ignore<int>(), Materializer);

            var msgs = await LogMessages(1, "[my-debug]");
            msgs[0].Should().Be("[my-debug] Upstream finished.");
        }



        [Fact]
        public async Task A_Log_on_source_must_debug_each_element()
        {
            _ = Source.From([1, 2]).Log("flow-s2").RunWith(Sink.Ignore<int>(), Materializer);

            var msgs = await LogMessages(3, "[flow-s2]");
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

            var msgs = await LogMessages(2, "[flow-s3]");
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

            var error = (Error) await _logProbe.FishForMessageAsync(o => o is Error e && e.Message.ToString().StartsWith("[flow-s4]"));
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

            var msgs = await LogEvents<Debug>(2, "[flow-5]");
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

            var warnings = await LogEvents<Warning>(1, "[flow-6]");
            warnings[0].Message.ToString().Should().Be("[flow-6] Element: 42");
            
            var info = await LogEvents<Info>(1, "[flow-6]");
            info[0].Message.ToString().Should().Be("[flow-6] Upstream finished.");

            var cause = new TestException("test");
            _ = Source.Failed<int>(cause)
                .Log("flow-6e")
                .WithAttributes(logAttributes)
                .RunWith(Sink.Ignore<int>(), Materializer);

            var error = await LogEvents<Debug>(1, "[flow-6e]");
            error[0].Message.ToString().Should().Be("[flow-6e] Upstream failed, cause: Akka.Streams.TestKit.TestException test");
        }

        [Fact]
        public async Task A_Log_on_source_must_allow_configuring_log_levels_via_Method_argument()
        {
            _ = Source.Single(42)
                .Log("flow-6", logLevel: LogLevel.WarningLevel)
                .RunWith(Sink.Ignore<int>(), Materializer);

            var warnings = await LogEvents<Warning>(1, "[flow-6]");
            warnings[0].Message.ToString().Should().Be("[flow-6] Element: 42");
        }

        [Fact]
        public async Task A_Log_on_Source_must_follow_supervision_strategy_when_Exception_thrown()
        {
            var ex = new TestException("test");
            var future = Source.From(Enumerable.Range(1, 5))
                .Log("hi", _ => throw ex)
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                .RunWith(Sink.Aggregate<int, int>(0, (i, i1) => i + i1), Materializer);

            await future.WaitAsync(TimeSpan.FromMilliseconds(500));
            future.Result.Should().Be(0);
        }
    }
}
