//-----------------------------------------------------------------------
// <copyright file="DilatedTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.TestKit;
using Xunit;

namespace Akka.Testkit.Tests.TestKitBaseTests
{
    public class DilatedTests : AkkaSpec
    {
        private const int TimeFactor = 4;
        private const int Timeout = 50;
        private const int ExpectedTimeout = Timeout * TimeFactor;
        private const int DiffDelta = 80;

        public DilatedTests()
            : base("akka.test.timefactor=" + TimeFactor)
        {
        }

        [Fact]
        public void Dilates_correctly_using_timeFactor()
        {
            Assert.Equal(Dilated(TimeSpan.FromSeconds(1)), TimeSpan.FromSeconds(4));
        }

        [Fact]
        public void AwaitCondition_should_dilate_timeout()
        {
            var before = Now;
            Intercept(() => AwaitCondition(() => false, TimeSpan.FromMilliseconds(Timeout)));
            var after = Now;
            var diff = (after - before).TotalMilliseconds;
            Assert.True(Math.Abs(diff - ExpectedTimeout) <= DiffDelta);
        }


        [Fact]
        public void ReceiveN_should_dilate_timeout()
        {
            var before = Now;
            Intercept(() => ReceiveN(42, TimeSpan.FromMilliseconds(Timeout)));
            var after = Now;
            var diff = (after - before).TotalMilliseconds;
            Assert.True(Math.Abs(diff - ExpectedTimeout) <= DiffDelta);
        }
        [Fact]
        public void ExpectMsgAllOf_should_dilate_timeout()
        {
            var before = Now;
            Intercept(() => ExpectMsgAllOf(TimeSpan.FromMilliseconds(Timeout), "1", "2"));
            var after = Now;
            var diff = (after - before).TotalMilliseconds;
            Assert.True(Math.Abs(diff - ExpectedTimeout) <= DiffDelta, string.Format("Expected the timeout to be {0} but in fact it was {1}.", ExpectedTimeout, diff));
        }

        [Fact]
        public void FishForMessage_should_dilate_timeout()
        {
            var before = Now;
            Intercept(() => FishForMessage(_=>false, TimeSpan.FromMilliseconds(Timeout)));
            var after = Now;
            var diff = (after - before).TotalMilliseconds;
            Assert.True(Math.Abs(diff - ExpectedTimeout) <= DiffDelta, string.Format("Expected the timeout to be {0} but in fact it was {1}.", ExpectedTimeout, diff));
        }
    }
}

