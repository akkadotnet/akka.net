﻿//-----------------------------------------------------------------------
// <copyright file="AllTestForEventFilterBase_Instances.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Event;

namespace Akka.TestKit.Tests.Xunit2.TestEventListenerTests
{
    /*TODO: this class is not used*/public class EventFilterDebugTests : AllTestForEventFilterBase<Debug>
    {
        public EventFilterDebugTests() : base("akka.loglevel=DEBUG"){}

        protected override void PublishMessage(object message, string source)
        {
            Sys.EventStream.Publish(new Debug(source,GetType(),message));
        }
    }

    /*TODO: this class is not used*/public class EventFilterInfoTests : AllTestForEventFilterBase<Info>
    {
        public EventFilterInfoTests() : base("akka.loglevel=INFO") { }

        protected override void PublishMessage(object message, string source)
        {
            Sys.EventStream.Publish(new Info(source, GetType(), message));
        }
    }

    /*TODO: this class is not used*/public class EventFilterWarningTests : AllTestForEventFilterBase<Warning>
    {
        public EventFilterWarningTests() : base("akka.loglevel=WARNING") { }

        protected override void PublishMessage(object message, string source)
        {
            Sys.EventStream.Publish(new Warning(source, GetType(), message));
        }
    }

    /*TODO: this class is not used*/public class EventFilterErrorTests : AllTestForEventFilterBase<Error>
    {
        public EventFilterErrorTests() : base("akka.loglevel=ERROR") { }

        protected override void PublishMessage(object message, string source)
        {
            Sys.EventStream.Publish(new Error(null, source, GetType(), message));
        }
    }
}

