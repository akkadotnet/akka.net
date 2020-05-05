//-----------------------------------------------------------------------
// <copyright file="RouteeCreationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------


using System;
using Akka.Actor;
using Akka.Routing;
using Akka.TestKit;
using Xunit;
using FluentAssertions;

namespace Akka.Tests.Routing
{
    public class RouteeCreationSpec : AkkaSpec
    {
        private class RouteeActor : ReceiveActor
        {
            public RouteeActor(IActorRef testActor)
            {
                Context.ActorSelection(Self.Path).Tell(new Identify(Self.Path), testActor);
            }
        }

        private class ForwardActor : ReceiveActor
        {
            private readonly IActorRef _testActor;

            public ForwardActor(IActorRef testActor)
            {
                _testActor = testActor;
                Context.Parent.Tell("one");

                Receive<string>(s => s == "one", c =>
                {
                    _testActor.Forward("two");
                });
            }
        }

        [Fact]
        public void Creating_routees_must_result_in_visible_routees()
        {
            int n = 100;
            Sys.ActorOf(new RoundRobinPool(n).Props(Props.Create(() => new RouteeActor(TestActor))));

            for (int i = 1; i <= n; i++)
            {
                ExpectMsg<ActorIdentity>().Subject.Should().NotBeNull();
            }
        }

        [Fact]
        public void Creating_routees_must_allow_sending_to_context_parent()
        {
            int n = 100;
            Sys.ActorOf(new RoundRobinPool(n).Props(Props.Create(() => new ForwardActor(TestActor))));
            var gotIt = ReceiveWhile<string>(msg =>
            {
                if (msg.Equals("two"))
                {
                    return LastSender.ToString();
                }

                return null;
            }, msgs: n);

            ExpectNoMsg(100.Milliseconds());

            gotIt.Count.Should().Be(n, $"Got only {gotIt.Count} from [{string.Join(", ", gotIt)}]");
        }
    }
}
