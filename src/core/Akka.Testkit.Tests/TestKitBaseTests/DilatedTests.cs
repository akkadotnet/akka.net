using System;
using Akka.TestKit;
using Xunit;

namespace Akka.Testkit.Tests.TestKitBaseTests
{
    public class DilatedTests : AkkaSpec
    {
        public DilatedTests() : base("akka.test.timefactor=2")
        {
        }

        [Fact]
        public void Dilates_correctly_using_timeFactor()
        {
            Assert.Equal(Dilated(TimeSpan.FromSeconds(1)), TimeSpan.FromSeconds(2));
        }

        [Fact]
        public void AwaitCondition_should_dilate_timeout()
        {
            var before = Now;
            Intercept(() => AwaitCondition(() => false, TimeSpan.FromMilliseconds(200)));
            var after = Now;
            var diff = (after - before).TotalMilliseconds;
            const int expectedDiff = 400;
            Assert.True(Math.Abs(diff - expectedDiff) <= 10);
        }
    }
}