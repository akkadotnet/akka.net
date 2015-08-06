//-----------------------------------------------------------------------
// <copyright file="TestKitTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//----------------------------------------------------------------------

using System;
using Machine.Specifications;

namespace Akka.TestKit.MSpec.Tests
{
    class AssertionTest
    {
        protected static Exception Exception;

        protected static MSpecAssertions Assertions = new MSpecAssertions();
    }

    [Subject(typeof(MSpecAssertions))]
    class When_fail_is_called : AssertionTest
    {
        private Because of = () => Exception = Catch.Exception(() => Assertions.Fail());

        protected It should_throw = () => Exception.ShouldBeOfExactType(typeof(SpecificationException));
    }

    [Subject(typeof (MSpecAssertions))]
    class When_AssertTrue_is_called_with_false : AssertionTest
    {
        private Because of = () => Exception = Catch.Exception(() => Assertions.AssertTrue(false));

        protected It should_throw = () => Exception.ShouldBeOfExactType(typeof(SpecificationException));
    }

    [Subject(typeof(MSpecAssertions))]
    class When_AssertTrue_is_called_with_true : AssertionTest
    {
        private Because of = () => Exception = Catch.Exception(() => Assertions.AssertTrue(true));

        protected It should_succeed = () => Exception.ShouldBeNull();
    }

    [Subject(typeof(MSpecAssertions))]
    class When_AssertFalse_is_called_with_true : AssertionTest
    {
        private Because of = () => Exception = Catch.Exception(() => Assertions.AssertFalse(true));

        protected It should_throw = () => Exception.ShouldBeOfExactType(typeof(SpecificationException));
    }

    [Subject(typeof(MSpecAssertions))]
    class When_AssertFalse_is_called_with_false : AssertionTest
    {
        private Because of = () => Exception = Catch.Exception(() => Assertions.AssertFalse(false));

        protected It should_succeed = () => Exception.ShouldBeNull();
    }


    [Subject(typeof (MSpecAssertions))]
    class When_AssertEqual_is_called_with_parameter_that_are_not_equal : AssertionTest
    {
        private Because of = () => Exception = Catch.Exception(() => Assertions.AssertEqual(42, 4711));

        protected It should_throw = () => Exception.ShouldBeOfExactType(typeof(SpecificationException));
    }


    [Subject(typeof(MSpecAssertions))]
    class When_AssertEqual_is_called_with_parameter_that_are_equal : AssertionTest
    {
        private Because of = () => Exception = Catch.Exception(() => Assertions.AssertEqual(42, 42));

        protected It should_succeed = () => Exception.ShouldBeNull();
    }


    [Subject(typeof(MSpecAssertions))]
    class When_AssertEqualWithComparer_is_called_and_the_comprarer_returns_false : AssertionTest
    {
        private Because of = () => Exception = Catch.Exception(() => Assertions.AssertEqual(42, 42, (x, y) => false));

        protected It should_throw = () => Exception.ShouldBeOfExactType(typeof(SpecificationException));
    }


    [Subject(typeof(MSpecAssertions))]
    class When_AssertEqualWithComparer_is_called_and_the_comprarer_returns_true : AssertionTest
    {
        private Because of = () => Exception = Catch.Exception(() => Assertions.AssertEqual(42, 4711, (x, y) => true));

        protected It should_succeed = () => Exception.ShouldBeNull();
    }
}
