//-----------------------------------------------------------------------
// <copyright file="BossActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Actor;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.TestKit.Tests.TestActorRefTests
{
    public class BossActor : TActorBase
    {
        private TestActorRef<InternalActor> _child;

        public BossActor(AtomicCounter counter, Thread parentThread, AtomicReference<Thread> otherThread) : base(parentThread, otherThread)
        {
            _child = new TestActorRef<InternalActor>(Context.System, Props.Create(() => new InternalActor(counter, parentThread, otherThread)), Self, "child");
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
            private readonly AtomicCounter _counter;

            public InternalActor(AtomicCounter counter, Thread parentThread, AtomicReference<Thread> otherThread) : base(parentThread, otherThread)
            {
                _counter = counter;
            }

            protected override void PreRestart(Exception reason, object message)
            {
                _counter.Decrement();
            }

            protected override void PostRestart(Exception reason)
            {
                _counter.Decrement();
            }

            protected override bool ReceiveMessage(object message)
            {
                return true;
            }
        }
    }
}

