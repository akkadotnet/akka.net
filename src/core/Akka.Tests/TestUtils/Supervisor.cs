//-----------------------------------------------------------------------
// <copyright file="Supervisor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.Tests.TestUtils
{
    /**
     * For testing Supervisor behavior, normally you don't supply the strategy
     * from the outside like this.
     */
    public class Supervisor : UntypedActor
    {
        private readonly SupervisorStrategy _supervisorStrategy;
        public Supervisor(SupervisorStrategy supervisorStrategy)
        {
            _supervisorStrategy = supervisorStrategy;
        }

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return _supervisorStrategy;
        }

        protected override void OnReceive(object message)
        {
            var props = message as Props;
            if (props != null)
            {
                Sender.Tell(Context.ActorOf(props));
            }
            else
            {
                var propsWithName = message as PropsWithName;
                if (propsWithName != null)
                {
                    Sender.Tell(Context.ActorOf(propsWithName.Props, propsWithName.Name));
                }
            }
        }

        protected override void PreRestart(Exception cause, object message)
        {
            // need to override the default of stopping all children upon restart, tests rely on keeping them around
        }
    }
}

