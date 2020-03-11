//-----------------------------------------------------------------------
// <copyright file="ExceptionEventFilterTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Event;
using Akka.TestKit;
using Xunit;
using Xunit.Sdk;

namespace Akka.Testkit.Tests.TestEventListenerTests
{
    //public class ExceptionEventFilterTests : EventFilterTestBase
    //{
    //    public ExceptionEventFilterTests()
    //        : base("akka.logLevel=ERROR")
    //    {
    //    }
    //    public class TestFinished : Exception { }
    //    public class SomeException : Exception { }

    //    protected override void SendInitLoggerMessage(InitLoggerMessage message)
    //    {
    //        throw new NotImplementedException();
    //    }

    //    protected override void AfterTest()
    //    {
    //        //After every test we make sure no uncatched messages have been logged
    //        EnsureNoMoreLoggedMessages();
    //        base.AfterTest();
    //    }

    //    private void EnsureNoMoreLoggedMessages()
    //    {
    //        //We log a TestFinished exception. When it arrives to TestActor we know no other message has been logged7
    //        //If we receive something else it means another message was logged, and ExpectMsg will fail
    //        Log.Error(new TestFinished(), "Finished");
    //        ExpectMsg<Error>(err => err.Cause is TestFinished,"cause to be <TestFinished>");
    //    }

    //    [Fact]
    //    public void SingleExceptionIsIntercepted()
    //    {
    //        EventFilter.Exception<SomeException>().Intercept(() => Log.Error(new SomeException(), "whatever"));
    //    }

    //    [Fact]
    //    public void CanInterceptMessagesWhenStartIsSpecified()
    //    {
    //        EventFilter.Exception<SomeException>(start: "what").Intercept(() => Log.Error(new SomeException(), "whatever"));
    //    }

    //    [Fact]
    //    public void DoNotInterceptMessagesWhenStartDoesNotMatch()
    //    {
    //        EventFilter.Exception<SomeException>(start: "this is clearly not in message").Intercept(() => Log.Error(new SomeException(), "whatever"));
    //        ExpectMsg<Error>(err => (string)err.Message == "whatever");
    //    }

    //    [Fact]
    //    public void CanInterceptMessagesWhenMessageIsSpecified()
    //    {
    //        EventFilter.Exception<SomeException>(message: "whatever").Intercept(() => Log.Error(new SomeException(), "whatever"));
    //    }

    //    [Fact]
    //    public void DoNotInterceptMessagesWhenMessageDoesNotMatch()
    //    {
    //        EventFilter.Exception<SomeException>(message: "this is clearly not the message").Intercept(() => Log.Error(new SomeException(), "whatever"));
    //        ExpectMsg<Error>(err => (string)err.Message == "whatever");
    //    }

    //    [Fact]
    //    public void CanInterceptMessagesWhenContainsIsSpecified()
    //    {
    //        EventFilter.Exception<SomeException>(contains: "ate").Intercept(() => Log.Error(new SomeException(), "whatever"));
    //    }

    //    [Fact]
    //    public void DoNotInterceptMessagesWhenContainsDoesNotMatch()
    //    {
    //        EventFilter.Exception<SomeException>(contains: "this is clearly not in the message").Intercept(() => Log.Error(new SomeException(), "whatever"));
    //        ExpectMsg<Error>(err => (string)err.Message == "whatever");
    //    }


    //    [Fact]
    //    public void CanInterceptMessagesWhenSourceIsSpecified()
    //    {
    //        EventFilter.Exception<SomeException>(source: GetType().FullName).Intercept(() => Log.Error(new SomeException(), "whatever"));
    //    }

    //    [Fact]
    //    public void DoNotInterceptMessagesWhenSourceDoesNotMatch()
    //    {
    //        EventFilter.Exception<SomeException>(source: "this is clearly not the source").Intercept(() => Log.Error(new SomeException(), "whatever"));
    //        ExpectMsg<Error>(err => (string)err.Message == "whatever");
    //    }


    //    [Fact]
    //    public void SpecifiedNumbersOfExceptionsCanBeIntercepted()
    //    {
    //        EventFilter.Exception<SomeException>(occurrences: 2).Intercept(() =>
    //        {
    //            Log.Error(new SomeException(), "whatever");
    //            Log.Error(new SomeException(), "whatever");
    //        });
    //    }

    //    [Fact]
    //    public void ShouldFailIfMoreExceptionsThenSpecifiedAreLogged()
    //    {
    //        var exception = XAssert.Throws<AssertException>(() =>
    //            EventFilter.Exception<SomeException>(occurrences: 2).Intercept(() =>
    //            {
    //                Log.Error(new SomeException(), "whatever");
    //                Log.Error(new SomeException(), "whatever");
    //                Log.Error(new SomeException(), "whatever");
    //            }));
    //        Assert.Contains("1 messages too many", exception.Message, StringComparison.OrdinalIgnoreCase);
    //    }

    //}
}

