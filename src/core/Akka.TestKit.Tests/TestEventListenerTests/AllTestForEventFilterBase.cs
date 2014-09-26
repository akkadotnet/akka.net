using System;
using System.Threading.Tasks;
using Akka.Event;
using Akka.TestKit;
using Xunit;
using Xunit.Sdk;

namespace Akka.Testkit.Tests.TestEventListenerTests
{
    public abstract class AllTestForEventFilterBase<TLogEvent> : EventFilterTestBase where TLogEvent : LogEvent
    {
        // ReSharper disable ConvertToLambdaExpression

        protected AllTestForEventFilterBase(string config)
            : base(config)
        {
            LogLevel = Logging.LogLevelFor<TLogEvent>();
        }

        protected LogLevel LogLevel { get; private set; }

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
        public void SingleMessageIsIntercepted()
        {
            EventFilter.ForLogLevel(LogLevel).ExpectOne(() => LogMessage("whatever"));
            TestSuccessful = true;
        }


        [Fact]
        public void CanInterceptMessagesWhenStartIsSpecified()
        {
            EventFilter.ForLogLevel(LogLevel, start: "what").ExpectOne(() => LogMessage("whatever"));
            TestSuccessful = true;
        }

        [Fact]
        public void DoNotInterceptMessagesWhenStartDoesNotMatch()
        {
            EventFilter.ForLogLevel(LogLevel, start: "what").ExpectOne(() =>
            {
                LogMessage("let-me-thru");
                LogMessage("whatever");
            });
            ExpectMsg<TLogEvent>(err => (string)err.Message == "let-me-thru");
            TestSuccessful = true;
        }

        [Fact]
        public void CanInterceptMessagesWhenMessageIsSpecified()
        {
            EventFilter.ForLogLevel(LogLevel, message: "whatever").ExpectOne(() => LogMessage("whatever"));
            TestSuccessful = true;
        }

        [Fact]
        public void DoNotInterceptMessagesWhenMessageDoesNotMatch()
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
        public void CanInterceptMessagesWhenContainsIsSpecified()
        {
            EventFilter.ForLogLevel(LogLevel, contains: "ate").ExpectOne(() => LogMessage("whatever"));
            TestSuccessful = true;
        }

        [Fact]
        public void DoNotInterceptMessagesWhenContainsDoesNotMatch()
        {
            EventFilter.ForLogLevel(LogLevel, contains: "eve").ExpectOne(() =>
            {
                LogMessage("let-me-thru");
                LogMessage("whatever");
            });
            ExpectMsg<TLogEvent>(err => (string)err.Message == "let-me-thru");
            TestSuccessful = true;
        }


        [Fact]
        public void CanInterceptMessagesWhenSourceIsSpecified()
        {
            EventFilter.ForLogLevel(LogLevel, source: GetType().FullName).ExpectOne(() => LogMessage("whatever"));
            TestSuccessful = true;
        }

        [Fact]
        public void DoNotInterceptMessagesWhenSourceDoesNotMatch()
        {
            EventFilter.ForLogLevel(LogLevel, source: "expected-source").ExpectOne(() =>
            {
                PublishMessage("message", source: "expected-source");
                PublishMessage("message", source: "let-me-thru");
            });
            ExpectMsg<TLogEvent>(err => err.LogSource == "let-me-thru");
            TestSuccessful = true;
        }



        [Fact]
        public void SpecifiedNumbersOfMessagesanBeIntercepted()
        {
            EventFilter.ForLogLevel(LogLevel).Expect(2, () =>
            {
                LogMessage("whatever");
                LogMessage("whatever");
            });
            TestSuccessful = true;
        }


        [Fact]
        public void MessagesCanBeMuted()
        {
            EventFilter.ForLogLevel(LogLevel).Mute(() =>
            {
                LogMessage("whatever");
                LogMessage("whatever");
            });
            TestSuccessful = true;
        }


        [Fact]
        public void MessagesCanBeMutedFromNowOn()
        {
            var unmutableFilter = EventFilter.ForLogLevel(LogLevel).Mute();
            LogMessage("whatever");
            LogMessage("whatever");
            unmutableFilter.Unmute();
            TestSuccessful = true;
        }

        [Fact]
        public void MessagesCanBeMutedFromNowOnWithUsing()
        {
            using(EventFilter.ForLogLevel(LogLevel).Mute())
            {
                LogMessage("whatever");
                LogMessage("whatever");
            }
            TestSuccessful = true;
        }


        [Fact]
        public void MakeSureAsyncWorks()
        {
            EventFilter.ForLogLevel(LogLevel).Expect(1, TimeSpan.FromMilliseconds(100), () =>
            {
                Task.Delay(TimeSpan.FromMilliseconds(10)).ContinueWith(t => { LogMessage("whatever"); });
            });
        }

        [Fact]
        public void ChainManyFilters()
        {
            EventFilter
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
        public void ShouldTimeoutIfTooFewMessages()
        {
            var exception = XAssert.Throws<AssertException>(() =>
            {
                EventFilter.ForLogLevel(LogLevel).Expect(2, TimeSpan.FromMilliseconds(50), () =>
                {
                    LogMessage("whatever");
                });
            });
            Assert.Contains("timeout", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ShouldLogWhenNotMuting()
        {
            const string message = "This should end up in the log since it's not filtered";
            LogMessage(message);
            ExpectMsg<TLogEvent>( msg => (string)msg.Message == message);
        }

        // ReSharper restore ConvertToLambdaExpression

    }
}