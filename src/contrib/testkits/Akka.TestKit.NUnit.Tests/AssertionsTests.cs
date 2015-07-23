//-----------------------------------------------------------------------
// <copyright file="TestKitTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//----------------------------------------------------------------------

using NUnit.Framework;

namespace Akka.TestKit.NUnit.Tests
{
    /*TODO: this class is not used*/[TestFixture]
    public class AssertionsTests
    {
        private readonly NUnitAssertions _assertions;

        public AssertionsTests()
        {
            _assertions = new NUnitAssertions();
        }

        [Test]
        [ExpectedException(typeof(AssertionException))]
        public void Fail_should_throw()
        {
            _assertions.Fail();
        }

        [Test]
        [ExpectedException(typeof(AssertionException))]
        public void AssertTrue_should_throw_on_false()
        {
            _assertions.AssertTrue(false);
        }

        [Test]
        public void AssertTrue_should_succeed_on_true()
        {
            _assertions.AssertTrue(true);
        }

        [Test]
        [ExpectedException(typeof(AssertionException))]
        public void AssertFalse_should_throw_on_true()
        {
            _assertions.AssertFalse(true);
        }

        [Test]
        public void AssertFalse_should_succeed_on_false()
        {
            _assertions.AssertFalse(false);
        }


        [Test]
        [ExpectedException(typeof(AssertionException))]
        public void AssertEqual_should_throw_on_not_equal()
        {
            _assertions.AssertEqual(42, 4711);
        }

        [Test]
        public void AssertEqual_should_succeed_on_equal()
        {
            _assertions.AssertEqual(42, 42);
        }


        [Test]
        [ExpectedException(typeof(AssertionException))]
        public void AssertEqualWithComparer_should_throw_on_not_equal()
        {
            _assertions.AssertEqual(42, 42, (x, y) => false);
        }

        [Test]
        public void AssertEqualWithComparer_should_succeed_on_equal()
        {
            _assertions.AssertEqual(42, 4711, (x, y) => true);
        }
    }
}
