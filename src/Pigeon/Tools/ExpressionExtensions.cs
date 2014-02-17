using Pigeon.Actor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Pigeon.Reflection
{
    public static class ExpressionExtensions
    {
        public static IEnumerable<object> GetArguments(this NewExpression newExpression)
        {
            var arguments = new List<object>();
            foreach (var argumentExpression in newExpression.Arguments)
            {
                var l = Expression.Lambda<Func<object>>(argumentExpression);
                var f = l.Compile();
                var res = f();

                arguments.Add(res);
            }
            return arguments;
        }
    }
}