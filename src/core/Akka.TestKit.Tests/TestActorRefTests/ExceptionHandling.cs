//-----------------------------------------------------------------------
// <copyright file="ExceptionHandling.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static FluentAssertions.FluentActions;

namespace Akka.TestKit.Tests.TestActorRefTests
{
    public class ExceptionHandling: TestKit.Xunit2.TestKit
    {
        private class GiveError
        { }

        private class GiveErrorAsync
        { }
        
        private class ExceptionActor : ReceiveActor
        {
            public ExceptionActor()
            {
                Receive<GiveError>((b) => throw new Exception("WAT"));

                ReceiveAsync<GiveErrorAsync>(async (b) =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(0.1));
                    throw new Exception("WATASYNC");
                });
            }
        }

        public ExceptionHandling(ITestOutputHelper helper) : base("akka.loglevel = debug", helper)
        {
        }
        
        [Fact]
        public void GetException()
        {
            var props = Props.Create<ExceptionActor>();
            var subject = new TestActorRef<ExceptionActor>(Sys, props, null, "testA");
            Invoking(() => subject.Receive(new GiveError()))
                .Should().Throw<Exception>().WithMessage("WAT");
        }
        
        [Fact]
        public async Task GetExceptionAsync()
        {
            var props = Props.Create<ExceptionActor>();
            var subject = new TestActorRef<ExceptionActor>(Sys, props, null, "testB");
            await Awaiting(() => subject.ReceiveAsync(new GiveErrorAsync()))
                .Should().ThrowAsync<Exception>().WithMessage("WATASYNC");
        }
    }
}
