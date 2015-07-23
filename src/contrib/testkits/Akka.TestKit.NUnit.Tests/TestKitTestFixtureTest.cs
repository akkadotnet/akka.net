//-----------------------------------------------------------------------
// <copyright file="TestKitTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.TestKit.TestActors;
using NUnit.Framework;

namespace Akka.TestKit.NUnit.Tests
{
    /*TODO: this class is not used*/[TestFixture]
    public class TestKitTestFixtureTest : TestKit
    {
        [Test]
        public void Can_create_more_than_one_test_in_a_fixture_with_the_same_actor_name_test1()
        {
            Sys.ActorOf<BlackHoleActor>("actor-name");
        }

        [Test]
        public void Can_create_more_than_one_test_in_a_fixture_with_the_same_actor_name_test2()
        {
            Sys.ActorOf<BlackHoleActor>("actor-name");
        }
    }
}