//-----------------------------------------------------------------------
// <copyright file="AllTestForEventFilterBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Event;
using Xunit;
using Xunit.Sdk;

namespace Akka.TestKit.Tests.Xunit2.TestEventListenerTests
{
    public abstract class AllTestForEventFilterBase<TLogEvent> : EventFilterTestBase where TLogEvent : LogEvent
    {
        // ReSharper disable ConvertToLambdaExpression
        private readonly EventFilterFactory _testingEventFilter;

        protected AllTestForEventFilterBase(string config)
            : base(config)
        {
            LogLevel = Logging.LogLevelFor<TLogEvent>();
            // ReSharper disable once VirtualMemberCallInContructor
            _testingEventFilter = CreateTestingEventFilter();
        }

        protected LogLevel LogLevel { get; private set; }
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
        public void Single_message_is_intercepted()
        {
            _testingEventFilter.ForLogLevel(LogLevel).ExpectOne(() => LogMessage("whatever"));
            TestSuccessful = true;
        }


        [Fact]
        public void Can_intercept_messages_when_start_is_specified()
        {
            _testingEventFilter.ForLogLevel(LogLevel, start: "what").ExpectOne(() => LogMessage("whatever"));
            TestSuccessful = true;
        }

        [Fact]
        public void Do_not_intercept_messages_when_start_does_not_match()
        {
            _testingEventFilter.ForLogLevel(LogLevel, start: "what").ExpectOne(() =>
            {
                LogMessage("let-me-thru");
                LogMessage("whatever");
            });
            ExpectMsg<TLogEvent>(err => (string)err.Message == "let-me-thru");
            TestSuccessful = true;
        }

        [Fact]
        public void Can_intercept_messages_when_message_is_specified()
        {
            _testingEventFilter.ForLogLevel(LogLevel, message: "whatever").ExpectOne(() => LogMessage("whatever"));
            TestSuccessful = true;
        }

        [Fact]
        public void Do_not_intercept_messages_when_message_does_not_match()
        {
            EventFilter.ForLogLevel(LogLevel, message: "whatever").ExpectOne(() =>
            {
                LogMessage("let-me-thru");
                LogMessage("whatever");
            });
            ExpectMsg<TLogEvent>(err => (string)err.Message == "let-me-thru");
            TestSuccessful = true;
        }

        [Fact]
        public void Can_intercept_messages_when_contains_is_specified()
        {
            _testingEventFilter.ForLogLevel(LogLevel, contains: "ate").ExpectOne(() => LogMessage("whatever"));
            TestSuccessful = true;
        }

        [Fact]
        public void Do_not_intercept_messages_when_contains_does_not_match()
        {
            _testingEventFilter.ForLogLevel(LogLevel, contains: "eve").ExpectOne(() =>
            {
                LogMessage("let-me-thru");
                LogMessage("whatever");
            });
            ExpectMsg<TLogEvent>(err => (string)err.Message == "let-me-thru");
            TestSuccessful = true;
        }


        [Fact]
        public void Can_intercept_messages_when_source_is_specified()
        {
            _testingEventFilter.ForLogLevel(LogLevel, source: LogSource.FromType(GetType(), Sys)).ExpectOne(() => LogMessage("whatever"));
            TestSuccessful = true;
        }

        [Fact]
        public void Do_not_intercept_messages_when_source_does_not_match()
        {
            _testingEventFilter.ForLogLevel(LogLevel, source: "expected-source").ExpectOne(() =>
            {
                PublishMessage("message", source: "expected-source");
                PublishMessage("message", source: "let-me-thru");
            });
            ExpectMsg<TLogEvent>(err => err.LogSource == "let-me-thru");
            TestSuccessful = true;
        }



        [Fact]
        public void Specified_numbers_of_messagesan_be_intercepted()
        {
            _testingEventFilter.ForLogLevel(LogLevel).Expect(2, () =>
            {
                LogMessage("whatever");
                LogMessage("whatever");
            });
            TestSuccessful = true;
        }


        [Fact]
        public void Messages_can_be_muted()
        {
            _testingEventFilter.ForLogLevel(LogLevel).Mute(() =>
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
        public void Make_sure_async_works()
        {
            _testingEventFilter.ForLogLevel(LogLevel).Expect(1, TimeSpan.FromMilliseconds(100), () =>
            {
                Task.Delay(TimeSpan.FromMilliseconds(10)).ContinueWith(t => { LogMessage("whatever"); });
            });
        }

        [Fact]
        public void Chain_many_filters()
        {
            _testingEventFilter
                .ForLogLevel(LogLevel,message:"Message 1").And
                .ForLogLevel(LogLevel,message:"Message 3")
                .Expect(2,() =>
                {
                    LogMessage("Message 1");
                    LogMessage("Message 2");
                    LogMessage("Message 3");

                });
            ExpectMsg<TLogEvent>(m => (string) m.Message == "Message 2");
        }


        [Fact]
        public void Should_timeout_if_too_few_messages()
        {
            var exception = XAssert.Throws<TrueException>(() =>
            {
                _testingEventFilter.ForLogLevel(LogLevel).Expect(2, TimeSpan.FromMilliseconds(50), () =>
                {
                    LogMessage("whatever");
                });
            });
            Assert.Contains("timeout", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Should_log_when_not_muting()
        {
            const string message = "This should end up in the log since it's not filtered";
            LogMessage(message);
            ExpectMsg<TLogEvent>( msg => (string)msg.Message == message);
        }

        // ReSharper restore ConvertToLambdaExpression

    }
}

