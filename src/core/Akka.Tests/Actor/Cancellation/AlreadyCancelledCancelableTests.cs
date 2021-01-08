//-----------------------------------------------------------------------
// <copyright file="AlreadyCancelledCancelableTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor.Cancellation
{
    public class AlreadyCancelledCancelableTests : AkkaSpec
    {
        [Fact]
        public void Should_be_cancelled()
        {
            var alreadyCanceledCancelable = AlreadyCanceledCancelable.Instance;
            alreadyCanceledCancelable.IsCancellationRequested.ShouldBeTrue();
        }

        [Fact]
        public void CancellationToken_should_be_cancelled()
        {
            var alreadyCanceledCancelable = AlreadyCanceledCancelable.Instance;
            alreadyCanceledCancelable.Token.IsCancellationRequested.ShouldBeTrue();
        }

        [Fact]
        public void Should_be_possible_to_call_Cancel()
        {
            var alreadyCanceledCancelable = AlreadyCanceledCancelable.Instance;
            alreadyCanceledCancelable.Cancel();
        }

        [Fact]
        public void Should_be_possible_to_call_Cancel_true()
        {
            var alreadyCanceledCancelable = AlreadyCanceledCancelable.Instance;
            alreadyCanceledCancelable.Cancel(true);
        }

        [Fact]
        public void Should_be_possible_to_call_Cancel_false()
        {
            var alreadyCanceledCancelable = AlreadyCanceledCancelable.Instance;
            alreadyCanceledCancelable.Cancel(false);
        }

        [Fact]
        public void Should_be_possible_to_call_CancelAfter()
        {
            var alreadyCanceledCancelable = AlreadyCanceledCancelable.Instance;
            alreadyCanceledCancelable.CancelAfter(0);
        }

        [Fact]
        public void Should_be_possible_to_call_CancelAfter_Timespan()
        {
            var alreadyCanceledCancelable = AlreadyCanceledCancelable.Instance;
            alreadyCanceledCancelable.CancelAfter(TimeSpan.Zero);
        }
    }
}

