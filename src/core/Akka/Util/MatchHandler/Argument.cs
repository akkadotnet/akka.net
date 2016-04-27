//-----------------------------------------------------------------------
// <copyright file="Argument.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Tools.MatchHandler
{
    public class Argument
    {
        private readonly PredicateAndHandler _predicateAndHandler;
        private readonly object _value;
        private readonly bool _valueIsActionOrFunc;

        public Argument(PredicateAndHandler predicateAndHandler, object value, bool valueIsActionOrFunc)
        {
            _predicateAndHandler = predicateAndHandler;
            _value = value;
            _valueIsActionOrFunc = valueIsActionOrFunc;
        }

        public PredicateAndHandler PredicateAndHandler{get { return _predicateAndHandler; }}
        public object Value{get { return _value; }}
        public bool ValueIsActionOrFunc{get { return _valueIsActionOrFunc; }}
    }
}

