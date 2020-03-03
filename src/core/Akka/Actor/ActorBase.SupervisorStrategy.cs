//-----------------------------------------------------------------------
// <copyright file="ActorBase.SupervisorStrategy.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Actor
{
    public abstract partial class ActorBase
    {
        private SupervisorStrategy _supervisorStrategy;

        /// <summary>
        /// Gets or sets a <see cref="SupervisorStrategy"/>.
        /// When getting, if a previously <see cref="SupervisorStrategy"/> has been set it's returned; otherwise calls
        /// <see cref="SupervisorStrategy">SupervisorStrategy()</see>, stores and returns it.
        /// </summary>
        internal SupervisorStrategy SupervisorStrategyInternal
        {
            get { return _supervisorStrategy ?? (_supervisorStrategy = SupervisorStrategy()); }
            set { _supervisorStrategy = value; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        protected virtual SupervisorStrategy SupervisorStrategy()
        {
            return Actor.SupervisorStrategy.DefaultStrategy;
        }
    }
}

