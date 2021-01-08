//-----------------------------------------------------------------------
// <copyright file="ExampleFSMActorTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using System.Collections.Immutable;
using Xunit;

namespace DocsExamples.Actor.FiniteStateMachine
{
    #region FSMTest
    public class ExampleFSMActorTests : TestKit
    {
        [Fact]
        public void Simple_finite_state_machine_must_batch_correctly()
        {
            var buncher = Sys.ActorOf(Props.Create<ExampleFSMActor>());
            buncher.Tell(new SetTarget(TestActor));
            buncher.Tell(new Queue(42));
            buncher.Tell(new Queue(43));
            ExpectMsg<Batch>().Obj.Should().BeEquivalentTo(ImmutableList.Create(42, 43));
            buncher.Tell(new Queue(44));
            buncher.Tell(new Flush());
            buncher.Tell(new Queue(45));
            ExpectMsg<Batch>().Obj.Should().BeEquivalentTo(ImmutableList.Create(44));
            ExpectMsg<Batch>().Obj.Should().BeEquivalentTo(ImmutableList.Create(45));
        }

        [Fact]
        public void Simple_finite_state_machine_must_not_batch_if_unitialized()
        {
            var buncher = Sys.ActorOf(Props.Create<ExampleFSMActor>());
            buncher.Tell(new Queue(42));
            ExpectNoMsg();
        }
    }
    #endregion
}
