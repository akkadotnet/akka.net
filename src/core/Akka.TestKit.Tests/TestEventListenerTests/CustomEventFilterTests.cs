//-----------------------------------------------------------------------
// <copyright file="CustomEventFilterTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Event;
using Xunit;

namespace Akka.TestKit.Tests.Xunit2.TestEventListenerTests
{
    public abstract class CustomEventFilterTestsBase : EventFilterTestBase
    {
        // ReSharper disable ConvertToLambdaExpression
        public CustomEventFilterTestsBase() : base("akka.loglevel=ERROR") { }

        protected override void SendRawLogEventMessage(object message)
        {
            Sys.EventStream.Publish(new Error(null, "CustomEventFilterTests", GetType(), message));
        }

        protected abstract EventFilterFactory CreateTestingEventFilter();

        [Fact]
        public void Custom_filter_should_match()
        {
            var eventFilter = CreateTestingEventFilter();
            eventFilter.Custom(logEvent => logEvent is Error && (string) logEvent.Message == "whatever").ExpectOne(() =>
            {
                Log.Error("whatever");
            });
        }

        [Fact]
        public void Custom_filter_should_match2()
        {
            var eventFilter = CreateTestingEventFilter();
            eventFilter.Custom<Error>(logEvent => (string)logEvent.Message == "whatever").ExpectOne(() =>
            {
                Log.Error("whatever");
            });
        }
        // ReSharper restore ConvertToLambdaExpression
    }

    public class CustomEventFilterTests : CustomEventFilterTestsBase
    {
        protected override EventFilterFactory CreateTestingEventFilter()
        {
            return EventFilter;
        }
    }

    public class CustomEventFilterCustomFilterTests : CustomEventFilterTestsBase
    {
        protected override EventFilterFactory CreateTestingEventFilter()
        {
            return CreateEventFilter(Sys);
        }
    }
}

