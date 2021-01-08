//-----------------------------------------------------------------------
// <copyright file="PredicateAndHandler.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq.Expressions;

namespace Akka.Tools.MatchHandler
{
    /// <summary>
    /// TBD
    /// </summary>
    internal class PredicateAndHandler
    {
        private readonly HandlerKind _handlerKind;
        private readonly bool _handlerFirstArgumentShouldBeBaseType;

        private PredicateAndHandler(HandlerKind handlerKind, bool handlerFirstArgumentShouldBeBaseType)
        {
            _handlerKind = handlerKind;
            _handlerFirstArgumentShouldBeBaseType = handlerFirstArgumentShouldBeBaseType;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public HandlerKind HandlerKind { get { return _handlerKind; } }
        /// <summary>
        /// TBD
        /// </summary>
        public IReadOnlyList<Argument> Arguments { get; private set; }
        /// <summary>
        /// TBD
        /// </summary>
        public bool HandlerFirstArgumentShouldBeBaseType { get { return _handlerFirstArgumentShouldBeBaseType; } }

        /// <summary>
        /// TBD
        /// </summary>
        public Expression ActionOrFuncExpression { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        public Expression PredicateExpression { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="action">TBD</param>
        /// <param name="predicate">TBD</param>
        /// <param name="handlerFirstArgumentShouldBeBaseType">TBD</param>
        /// <returns>TBD</returns>
        public static PredicateAndHandler CreateAction(object action, object predicate = null, bool handlerFirstArgumentShouldBeBaseType=false)
        {
            if(predicate == null)
            {
                var actionHandler = new PredicateAndHandler(HandlerKind.Action, handlerFirstArgumentShouldBeBaseType);
                actionHandler.Arguments = new[] { new Argument(actionHandler, action, true) };
                return actionHandler;
            }
            var predicateActionHandler = new PredicateAndHandler(HandlerKind.ActionWithPredicate, handlerFirstArgumentShouldBeBaseType);
            predicateActionHandler.Arguments = new[] { new Argument(predicateActionHandler, action, true), new Argument(predicateActionHandler, predicate, false) };

            return predicateActionHandler;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="func">TBD</param>
        /// <param name="handlerFirstArgumentShouldBeBaseType">TBD</param>
        /// <returns>TBD</returns>
        public static PredicateAndHandler CreateFunc(object func, bool handlerFirstArgumentShouldBeBaseType=false)
        {
            var funcHandler = new PredicateAndHandler(HandlerKind.Func, handlerFirstArgumentShouldBeBaseType);
            funcHandler.Arguments = new[] { new Argument(funcHandler, func, true) };

            return funcHandler;
        }
    }
}

