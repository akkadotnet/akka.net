//-----------------------------------------------------------------------
// <copyright file="AllTestForEventFilterBase_Instances.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Event;
using Akka.TestKit;

namespace Akka.Hosting.TestKit.Tests.TestEventListenerTests;

public class EventFilterDebugTests : AllTestForEventFilterBase<Debug>
{
    public EventFilterDebugTests() : base(LogLevel.DebugLevel){}

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
    public CustomEventFilterDebugTests() : base(LogLevel.DebugLevel) { }

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
    public EventFilterInfoTests() : base(LogLevel.InfoLevel) { }

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
    public CustomEventFilterInfoTests() : base(LogLevel.InfoLevel) { }

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
    public EventFilterWarningTests() : base(LogLevel.WarningLevel) { }

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
    public CustomEventFilterWarningTests() : base(LogLevel.WarningLevel) { }

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
    public EventFilterErrorTests() : base(LogLevel.ErrorLevel) { }

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
    public CustomEventFilterErrorTests() : base(LogLevel.ErrorLevel) { }

    protected override EventFilterFactory CreateTestingEventFilter()
    {
        return CreateEventFilter(Sys);
    }

    protected override void PublishMessage(object message, string source)
    {
        Sys.EventStream.Publish(new Error(null, source, GetType(), message));
    }
}