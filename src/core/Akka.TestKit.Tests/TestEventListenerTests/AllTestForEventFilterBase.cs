//-----------------------------------------------------------------------
// <copyright file="AllTestForEventFilterBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Event;
using FluentAssertions;
using Xunit;
using Xunit.Sdk;
using static FluentAssertions.FluentActions;

namespace Akka.TestKit.Tests.TestEventListenerTests
{
    public abstract class AllTestForEventFilterBase<TLogEvent> : EventFilterTestBase where TLogEvent : LogEvent
    {
        private readonly EventFilterFactory _testingEventFilter;

        protected AllTestForEventFilterBase(string config)
            : base(config)
        {
            LogLevel = Logging.LogLevelFor<TLogEvent>();
            _testingEventFilter = CreateTestingEventFilter();
        }

        protected LogLevel LogLevel { get; }
        protected abstract EventFilterFactory CreateTestingEventFilter();

        protected void LogMessage(string message)
        {
            Log.Log(LogLevel, message);
        }

        protected override void SendRawLogEventMessage(object message)
        {
            PublishMessage(message, "test");
        }

        protected abstract void PublishMessage(object message, string source);

        [Fact]
        public async Task Single_message_is_intercepted()
        {
            await _testingEventFilter.ForLogLevel(LogLevel)
                .ExpectOneAsync(() => LogMessage("whatever"));
            TestSuccessful = true;
        }


        [Fact]
        public async Task Can_intercept_messages_when_start_is_specified()
        {
            await _testingEventFilter.ForLogLevel(LogLevel, start: "what")
                .ExpectOneAsync(() => LogMessage("whatever"));
            TestSuccessful = true;
        }

        [Fact]
        public async Task Do_not_intercept_messages_when_start_does_not_match()
        {
            await _testingEventFilter.ForLogLevel(LogLevel, start: "what").ExpectOneAsync(() =>
            {
                LogMessage("let-me-thru");
                LogMessage("whatever");
            });
            await ExpectMsgAsync<TLogEvent>(err => (string)err.Message == "let-me-thru");
            TestSuccessful = true;
        }

        [Fact]
        public async Task Can_intercept_messages_when_message_is_specified()
        {
            await _testingEventFilter.ForLogLevel(LogLevel, message: "whatever")
                .ExpectOneAsync(() => LogMessage("whatever"));
            TestSuccessful = true;
        }

        [Fact]
        public async Task Do_not_intercept_messages_when_message_does_not_match()
        {
            await EventFilter.ForLogLevel(LogLevel, message: "whatever").ExpectOneAsync(() =>
            {
                LogMessage("let-me-thru");
                LogMessage("whatever");
            });
            await ExpectMsgAsync<TLogEvent>(err => (string)err.Message == "let-me-thru");
            TestSuccessful = true;
        }

        [Fact]
        public async Task Can_intercept_messages_when_contains_is_specified()
        {
            await _testingEventFilter.ForLogLevel(LogLevel, contains: "ate")
                .ExpectOneAsync(() => LogMessage("whatever"));
            TestSuccessful = true;
        }

        [Fact]
        public async Task Do_not_intercept_messages_when_contains_does_not_match()
        {
            await _testingEventFilter.ForLogLevel(LogLevel, contains: "eve").ExpectOneAsync(() =>
            {
                LogMessage("let-me-thru");
                LogMessage("whatever");
            });
            ExpectMsg<TLogEvent>(err => (string)err.Message == "let-me-thru");
            TestSuccessful = true;
        }


        [Fact]
        public async Task Can_intercept_messages_when_source_is_specified()
        {
            await _testingEventFilter.ForLogLevel(LogLevel, source: LogSource.FromType(GetType(), Sys))
                .ExpectOneAsync(() => LogMessage("whatever"));
            TestSuccessful = true;
        }

        [Fact]
        public async Task Do_not_intercept_messages_when_source_does_not_match()
        {
            await _testingEventFilter.ForLogLevel(LogLevel, source: "expected-source").ExpectOneAsync(() =>
            {
                PublishMessage("message", source: "expected-source");
                PublishMessage("message", source: "let-me-thru");
            });
            await ExpectMsgAsync<TLogEvent>(err => err.LogSource == "let-me-thru");
            TestSuccessful = true;
        }

        [Fact]
        public async Task Specified_numbers_of_messages_can_be_intercepted()
        {
            await _testingEventFilter.ForLogLevel(LogLevel).ExpectAsync(2, () =>
            {
                LogMessage("whatever");
                LogMessage("whatever");
            });
            TestSuccessful = true;
        }

        [Fact]
        public async Task Expect_0_events_Should_work()
        {
            await Awaiting(async () =>
            {
                await EventFilter.Error().ExpectAsync(0, () =>
                {
                    Log.Error("something");
                });
            }).Should().ThrowAsync<Exception>("Expected 0 events");
        }

        [Fact]
        public async Task ExpectAsync_0_events_Should_work()
        {
            await Awaiting(async () =>
            {
                await EventFilter.Error().ExpectAsync(0, async () =>
                {
                    await Task.Delay(100); // bug only happens when error is not logged instantly
                    Log.Error("something");
                });
            }).Should().ThrowAsync<Exception>("Expected 0 errors logged, but there are error logs");
        }

        /// issue: InternalExpectAsync does not await actionAsync() - causing actionAsync to run as a detached task #5537
        [Fact]
        public async Task ExpectAsync_should_await_actionAsync()
        {
            await Assert.ThrowsAnyAsync<FalseException>(async () =>
            {
                await _testingEventFilter.ForLogLevel(LogLevel).ExpectAsync(0, actionAsync: async () =>
                {
                    Assert.False(true);
                    await Task.CompletedTask;
                });
            });
        }

        // issue: InterceptAsync seems to run func() as a detached task #5586
        [Fact]
        public async Task InterceptAsync_should_await_func()
        {
            await Assert.ThrowsAnyAsync<FalseException>(async () =>
            {
                await _testingEventFilter.ForLogLevel(LogLevel).ExpectAsync(0, async () =>
                {
                    Assert.False(true);
                    await Task.CompletedTask;
                }, TimeSpan.FromSeconds(.1));
            });
        }

        [Fact]
        public async Task Messages_can_be_muted()
        {
            await _testingEventFilter.ForLogLevel(LogLevel).MuteAsync(() =>
            {
                LogMessage("whatever");
                LogMessage("whatever");
            });
            TestSuccessful = true;
        }


        [Fact]
        public void Messages_can_be_muted_from_now_on()
        {
            var unmutableFilter = _testingEventFilter.ForLogLevel(LogLevel).Mute();
            LogMessage("whatever");
            LogMessage("whatever");
            unmutableFilter.Unmute();
            TestSuccessful = true;
        }

        [Fact]
        public void Messages_can_be_muted_from_now_on_with_using()
        {
            using(_testingEventFilter.ForLogLevel(LogLevel).Mute())
            {
                LogMessage("whatever");
                LogMessage("whatever");
            }
            TestSuccessful = true;
        }


        [Fact]
        public async Task Make_sure_async_works()
        {
            await _testingEventFilter.ForLogLevel(LogLevel).ExpectAsync(1, TimeSpan.FromSeconds(2), () =>
            {
                Task.Delay(TimeSpan.FromMilliseconds(10)).ContinueWith(t => { LogMessage("whatever"); });
            });
        }

        [Fact]
        public async Task Chain_many_filters()
        {
            await _testingEventFilter
                .ForLogLevel(LogLevel,message:"Message 1").And
                .ForLogLevel(LogLevel,message:"Message 3")
                .ExpectAsync(2,() =>
                {
                    LogMessage("Message 1");
                    LogMessage("Message 2");
                    LogMessage("Message 3");

                });
            await ExpectMsgAsync<TLogEvent>(m => (string) m.Message == "Message 2");
        }


        [Fact]
        public async Task Should_timeout_if_too_few_messages()
        {
            await Awaiting(async () =>
            {
                await _testingEventFilter.ForLogLevel(LogLevel).ExpectAsync(2, TimeSpan.FromMilliseconds(50), () =>
                {
                    LogMessage("whatever");
                });
            }).Should().ThrowAsync<TrueException>().WithMessage("timeout");
        }

        [Fact]
        public async Task Should_log_when_not_muting()
        {
            const string message = "This should end up in the log since it's not filtered";
            LogMessage(message);
            await ExpectMsgAsync<TLogEvent>( msg => (string)msg.Message == message);
        }
    }
}

