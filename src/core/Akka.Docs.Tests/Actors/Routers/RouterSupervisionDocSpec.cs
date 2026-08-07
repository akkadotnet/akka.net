//-----------------------------------------------------------------------
// <copyright file="RouterSupervisionDocSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Routing;

namespace DocsExamples.Actors.Routers
{
    public class RouterSupervisionDocSpec
    {
        public class Worker : ReceiveActor
        {
            public Worker()
            {
                ReceiveAny(_ => { });
            }
        }

        public void CreatePoolWithCustomSupervisorStrategy(ActorSystem system)
        {
            #region pool-with-supervisor-strategy
            var pool = new RoundRobinPool(5)
                .WithSupervisorStrategy(new OneForOneStrategy(
                    maxNrOfRetries: 10,
                    withinTimeRange: TimeSpan.FromMinutes(1),
                    localOnlyDecider: ex =>
                    {
                        if (ex is ActorInitializationException)
                            return Directive.Stop;
                        return Directive.Restart;
                    }));

            var router = system.ActorOf(Props.Create<Worker>().WithRouter(pool), "workers");
            #endregion
        }

        public void CreatePoolFromConfigWithSupervisorStrategy(ActorSystem system)
        {
            #region from-config-with-supervisor-strategy
            var router = system.ActorOf(
                Props.Create<Worker>().WithRouter(
                    FromConfig.Instance.WithSupervisorStrategy(
                        new OneForOneStrategy(ex => Directive.Restart))),
                "workers");
            #endregion
        }
    }
}
