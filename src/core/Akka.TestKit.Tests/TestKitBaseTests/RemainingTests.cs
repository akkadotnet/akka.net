using System;
using Xunit;

namespace Akka.Testkit.Tests.TestKitBaseTests
{
    public class RemainingTests : Akka.TestKit.Xunit.TestKit
    {
        [Fact]
        public void Throw_if_remaining_is_called_outside_Within()
        {
            Assert.Throws<InvalidOperationException>(() => Remaining);
        }
    }
}