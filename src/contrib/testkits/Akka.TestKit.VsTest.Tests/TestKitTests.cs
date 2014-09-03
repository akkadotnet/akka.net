using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Akka.TestKit.VsTest.Tests
{
    [TestClass]
    public class TestKitTests : TestKit
    {
        [TestCleanup]
        public void Cleanup()
        {
            Shutdown();
        }

        [TestMethod]
        public void Expect_a_message()
        {
            TestActor.Tell("Test");
            ExpectMsg("Test");
        }
    }
}
