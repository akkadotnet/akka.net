//-----------------------------------------------------------------------
// <copyright file="BossActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.TestKit.Tests.TestActorRefTests
{
    public class BossActor : TActorBase
    {
        private TestActorRef<InternalActor> _child;

        public BossActor()
        {
            _child = new TestActorRef<InternalActor>(Context.System, Props.Create<InternalActor>(), Self, "child");
        }

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return new OneForOneStrategy(maxNrOfRetries: 5, withinTimeRange: TimeSpan.FromSeconds(1), localOnlyDecider: ex => ex is ActorKilledException ? Directive.Restart : Directive.Escalate);
        }

        protected override bool ReceiveMessage(object message)
        {
            if(message is string && ((string)message) == "sendKill")
            {
                _child.Tell(Kill.Instance);
                return true;
            }
            return false;
        }

        private class InternalActor : TActorBase
        {
            protected override void PreRestart(Exception reason, object message)
            {
                TestActorRefSpec.Counter--;
            }

            protected override void PostRestart(Exception reason)
            {
                TestActorRefSpec.Counter--;
            }

            protected override bool ReceiveMessage(object message)
            {
                return true;
            }
        }
    }
}

