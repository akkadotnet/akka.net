//-----------------------------------------------------------------------
// <copyright file="AssertionsTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Akka.TestKit.VsTest.Tests
{
    [TestClass]
    public class AssertionsTests
    {
        private VsTestAssertions _assertions;

        public AssertionsTests()
        {
            _assertions = new VsTestAssertions();
        }

        [TestMethod]
        [ExpectedException(typeof(AssertFailedException))]
        public void Fail_should_throw()
        {
            _assertions.Fail();
        }

        [TestMethod]
        [ExpectedException(typeof(AssertFailedException))]
        public void AssertTrue_should_throw_on_false()
        {
            _assertions.AssertTrue(false);
        }

        [TestMethod]
        public void AssertTrue_should_succeed_on_true()
        {
            _assertions.AssertTrue(true);
        }

        [TestMethod]
        [ExpectedException(typeof(AssertFailedException))]
        public void AssertFalse_should_throw_on_true()
        {
            _assertions.AssertFalse(true);
        }

        [TestMethod]
        public void AssertFalse_should_succeed_on_false()
        {
            _assertions.AssertFalse(false);
        }


        [TestMethod]
        [ExpectedException(typeof(AssertFailedException))]
        public void AssertEqual_should_throw_on_not_equal()
        {
            _assertions.AssertEqual(42, 4711);
        }

        [TestMethod]
        public void AssertEqual_should_succeed_on_equal()
        {
            _assertions.AssertEqual(42, 42);
        }


        [TestMethod]
        [ExpectedException(typeof(AssertFailedException))]
        public void AssertEqualWithComparer_should_throw_on_not_equal()
        {
            _assertions.AssertEqual(42, 42,(x,y)=>false);
        }

        [TestMethod]
        public void AssertEqualWithComparer_should_succeed_on_equal()
        {
            _assertions.AssertEqual(42, 4711,(x,y)=>true);
        }

    }
}

