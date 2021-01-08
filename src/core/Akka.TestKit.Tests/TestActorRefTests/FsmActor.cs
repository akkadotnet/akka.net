//-----------------------------------------------------------------------
// <copyright file="FsmActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.TestKit.Tests.TestActorRefTests
{
    public enum TestFsmState
    {
        First,
        Last
    }

    public class FsmActor : FSM<TestFsmState, string>
    {
        private readonly IActorRef _replyActor;

        public FsmActor(IActorRef replyActor)
        {
            _replyActor = replyActor;

            When(TestFsmState.First, e =>
            {
                if (e.FsmEvent.Equals("check"))
                {
                    _replyActor.Tell("first");
                }
                else if (e.FsmEvent.Equals("next"))
                {
                    return GoTo(TestFsmState.Last);
                }

                return Stay();
            });

            When(TestFsmState.Last, e =>
            {
                if (e.FsmEvent.Equals("check"))
                {
                    _replyActor.Tell("last");
                }
                else if (e.FsmEvent.Equals("next"))
                {
                    return GoTo(TestFsmState.First);
                }

                return Stay();
            });

            StartWith(TestFsmState.First, "foo");
        }
    }
}
