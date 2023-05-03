//-----------------------------------------------------------------------
// <copyright file="Argument.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Tools.MatchHandler
{
    /// <summary>
    /// TBD
    /// </summary>
    internal class Argument
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="predicateAndHandler">TBD</param>
        /// <param name="value">TBD</param>
        /// <param name="valueIsActionOrFunc">TBD</param>
        public Argument(PredicateAndHandler predicateAndHandler, object value, bool valueIsActionOrFunc)
        {
            PredicateAndHandler = predicateAndHandler;
            Value = value;
            ValueIsActionOrFunc = valueIsActionOrFunc;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public PredicateAndHandler PredicateAndHandler { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public object Value { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public bool ValueIsActionOrFunc { get; }
    }
}

