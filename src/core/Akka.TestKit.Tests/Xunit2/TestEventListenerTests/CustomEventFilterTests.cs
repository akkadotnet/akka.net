//-----------------------------------------------------------------------
// <copyright file="CustomEventFilterTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Event;
using Xunit;

namespace Akka.TestKit.Tests.Xunit2.TestEventListenerTests
{
    public class CustomEventFilterTests : EventFilterTestBase
    {
        // ReSharper disable ConvertToLambdaExpression
        public CustomEventFilterTests() : base("akka.loglevel=ERROR") { }

        protected override void SendRawLogEventMessage(object message)
        {
            Sys.EventStream.Publish(new Error(null, "CustomEventFilterTests", GetType(), message));
        }

        [Fact]
        public void CustomFilterShouldMatch()
        {
            EventFilter.Custom(logEvent => logEvent is Error && (string) logEvent.Message == "whatever").ExpectOne(() =>
            {
                Log.Error("whatever");
            });
        }

        [Fact]
        public void CustomFilterShouldMatch2()
        {
            EventFilter.Custom<Error>(logEvent => (string)logEvent.Message == "whatever").ExpectOne(() =>
            {
                Log.Error("whatever");
            });
        }
        // ReSharper restore ConvertToLambdaExpression
    }
}

