//-----------------------------------------------------------------------
// <copyright file="ActorSystemTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using Akka.Actor;
using Akka.Actor.Internal;
using Xunit;

namespace Akka.Tests
{

    public class ActorSystemTests
    {
        public class TestActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
            }
        }

        [Fact]
        public void ActorSystem_ActorOf_adds_a_child_to_Guardian()
        {
            //arrange
            var system = new ActorSystemImpl("test");
            system.Start(); //When we create a system manually we have to start it ourselves

            //act
            var child = system.ActorOf<TestActor>("test");

            //assert
            var children = system.Provider.Guardian.Children;
            Assert.Contains(children, c => c == child);
        }

        [Fact]
        public void ActorOf_gives_child_unique_name_if_not_specified()
        {
            //arrange
            var system = new ActorSystemImpl("test");
            system.Start(); //When we create a system manually we have to start it ourselves
            //act
            var child1 = system.ActorOf<TestActor>();
            var child2 = system.ActorOf<TestActor>();

            //assert
            Assert.NotEqual(child1.Path, child2.Path);
        }
    }
}

