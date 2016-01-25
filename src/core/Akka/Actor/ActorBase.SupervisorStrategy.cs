//-----------------------------------------------------------------------
// <copyright file="ActorBase.SupervisorStrategy.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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

        protected virtual SupervisorStrategy SupervisorStrategy()
        {
            return Actor.SupervisorStrategy.DefaultStrategy;
        }
    }
}

