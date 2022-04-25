//-----------------------------------------------------------------------
// <copyright file="CustomEventFilterTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Event;
using Xunit;

namespace Akka.TestKit.Tests.TestEventListenerTests
{
    public abstract class CustomEventFilterTestsBase : EventFilterTestBase
    {
        protected CustomEventFilterTestsBase() : base("akka.loglevel=ERROR") { }

        protected override void SendRawLogEventMessage(object message)
        {
            Sys.EventStream.Publish(new Error(null, "CustomEventFilterTests", GetType(), message));
        }

        protected abstract EventFilterFactory CreateTestingEventFilter();

        [Fact]
        public async Task Custom_filter_should_match()
        {
            var eventFilter = CreateTestingEventFilter();
            await eventFilter.Custom(logEvent => logEvent is Error && (string) logEvent.Message == "whatever")
                .ExpectOneAsync(() =>
                {
                    Log.Error("whatever");
                });
        }

        [Fact]
        public async Task Custom_filter_should_match2()
        {
            var eventFilter = CreateTestingEventFilter();
            await eventFilter.Custom<Error>(logEvent => (string)logEvent.Message == "whatever")
                .ExpectOneAsync(() =>
                {
                    Log.Error("whatever");
                });
        }
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

