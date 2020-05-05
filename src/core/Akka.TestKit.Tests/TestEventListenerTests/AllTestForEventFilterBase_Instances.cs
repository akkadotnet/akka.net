//-----------------------------------------------------------------------
// <copyright file="AllTestForEventFilterBase_Instances.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Event;

namespace Akka.TestKit.Tests.Xunit2.TestEventListenerTests
{
    public class EventFilterDebugTests : AllTestForEventFilterBase<Debug>
    {
        public EventFilterDebugTests() : base("akka.loglevel=DEBUG"){}

        protected override EventFilterFactory CreateTestingEventFilter()
        {
            return EventFilter;
        }

        protected override void PublishMessage(object message, string source)
        {
            Sys.EventStream.Publish(new Debug(source,GetType(),message));
        }
    }

    public class CustomEventFilterDebugTests : AllTestForEventFilterBase<Debug>
    {
        public CustomEventFilterDebugTests() : base("akka.loglevel=DEBUG") { }

        protected override EventFilterFactory CreateTestingEventFilter()
        {
            return CreateEventFilter(Sys);
        }

        protected override void PublishMessage(object message, string source)
        {
            Sys.EventStream.Publish(new Debug(source, GetType(), message));
        }
    }

    public class EventFilterInfoTests : AllTestForEventFilterBase<Info>
    {
        public EventFilterInfoTests() : base("akka.loglevel=INFO") { }

        protected override EventFilterFactory CreateTestingEventFilter()
        {
            return EventFilter;
        }

        protected override void PublishMessage(object message, string source)
        {
            Sys.EventStream.Publish(new Info(source, GetType(), message));
        }
    }

    public class CustomEventFilterInfoTests : AllTestForEventFilterBase<Info>
    {
        public CustomEventFilterInfoTests() : base("akka.loglevel=INFO") { }

        protected override EventFilterFactory CreateTestingEventFilter()
        {
            return CreateEventFilter(Sys);
        }

        protected override void PublishMessage(object message, string source)
        {
            Sys.EventStream.Publish(new Info(source, GetType(), message));
        }
    }


    public class EventFilterWarningTests : AllTestForEventFilterBase<Warning>
    {
        public EventFilterWarningTests() : base("akka.loglevel=WARNING") { }

        protected override EventFilterFactory CreateTestingEventFilter()
        {
            return EventFilter;
        }

        protected override void PublishMessage(object message, string source)
        {
            Sys.EventStream.Publish(new Warning(source, GetType(), message));
        }
    }

    public class CustomEventFilterWarningTests : AllTestForEventFilterBase<Warning>
    {
        public CustomEventFilterWarningTests() : base("akka.loglevel=WARNING") { }

        protected override EventFilterFactory CreateTestingEventFilter()
        {
            return CreateEventFilter(Sys);
        }

        protected override void PublishMessage(object message, string source)
        {
            Sys.EventStream.Publish(new Warning(source, GetType(), message));
        }
    }

    public class EventFilterErrorTests : AllTestForEventFilterBase<Error>
    {
        public EventFilterErrorTests() : base("akka.loglevel=ERROR") { }

        protected override EventFilterFactory CreateTestingEventFilter()
        {
            return EventFilter;
        }

        protected override void PublishMessage(object message, string source)
        {
            Sys.EventStream.Publish(new Error(null, source, GetType(), message));
        }
    }

    public class CustomEventFilterErrorTests : AllTestForEventFilterBase<Error>
    {
        public CustomEventFilterErrorTests() : base("akka.loglevel=ERROR") { }

        protected override EventFilterFactory CreateTestingEventFilter()
        {
            return CreateEventFilter(Sys);
        }

        protected override void PublishMessage(object message, string source)
        {
            Sys.EventStream.Publish(new Error(null, source, GetType(), message));
        }
    }
}

