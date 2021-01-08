//-----------------------------------------------------------------------
// <copyright file="Argument.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Tools.MatchHandler
{
    /// <summary>
    /// TBD
    /// </summary>
    internal class Argument
    {
        private readonly PredicateAndHandler _predicateAndHandler;
        private readonly object _value;
        private readonly bool _valueIsActionOrFunc;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="predicateAndHandler">TBD</param>
        /// <param name="value">TBD</param>
        /// <param name="valueIsActionOrFunc">TBD</param>
        public Argument(PredicateAndHandler predicateAndHandler, object value, bool valueIsActionOrFunc)
        {
            _predicateAndHandler = predicateAndHandler;
            _value = value;
            _valueIsActionOrFunc = valueIsActionOrFunc;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public PredicateAndHandler PredicateAndHandler{get { return _predicateAndHandler; }}
        /// <summary>
        /// TBD
        /// </summary>
        public object Value{get { return _value; }}
        /// <summary>
        /// TBD
        /// </summary>
        public bool ValueIsActionOrFunc{get { return _valueIsActionOrFunc; }}
    }
}

