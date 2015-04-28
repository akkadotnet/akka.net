//-----------------------------------------------------------------------
// <copyright file="TestKitTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//----------------------------------------------------------------------

using Akka.Actor;
using NUnit.Framework;

namespace Akka.TestKit.NUnit.Tests
{
    [TestFixture]
    public class TestKitTests : TestKit
    {
        [TearDown]
        public void Cleanup()
        {
            Shutdown();
        }

        [Test]
        public void Expect_a_message()
        {
            TestActor.Tell("Test");
            ExpectMsg("Test");
        }
    }
}