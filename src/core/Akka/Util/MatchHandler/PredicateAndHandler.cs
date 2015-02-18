using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Akka.Tools.MatchHandler
{
    public class PredicateAndHandler
    {
        private readonly HandlerKind _handlerKind;
        private readonly bool _handlerFirstArgumentShouldBeBaseType;

        private PredicateAndHandler(HandlerKind handlerKind, bool handlerFirstArgumentShouldBeBaseType)
        {
            _handlerKind = handlerKind;
            _handlerFirstArgumentShouldBeBaseType = handlerFirstArgumentShouldBeBaseType;
        }

        public HandlerKind HandlerKind { get { return _handlerKind; } }
        public IReadOnlyList<Argument> Arguments { get; private set; }
        public bool HandlerFirstArgumentShouldBeBaseType { get { return _handlerFirstArgumentShouldBeBaseType; } }

        public Expression ActionOrFuncExpression { get; set; }
        public Expression PredicateExpression { get; set; }        
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

        public static PredicateAndHandler CreateFunc(object func, bool handlerFirstArgumentShouldBeBaseType=false)
        {
            var funcHandler = new PredicateAndHandler(HandlerKind.Func, handlerFirstArgumentShouldBeBaseType);
            funcHandler.Arguments = new[] { new Argument(funcHandler, func, true) };

            return funcHandler;
        }
    }
}