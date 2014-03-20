using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Akka.Reflection
{
    public static class ExpressionExtensions
    {
        public static IEnumerable<object> GetArguments(this NewExpression newExpression)
        {
            var arguments = new List<object>();
            foreach (Expression argumentExpression in newExpression.Arguments)
            {
                Expression conversion = Expression.Convert(argumentExpression, typeof (object));
                Expression<Func<object>> l = Expression.Lambda<Func<object>>(conversion);
                Func<object> f = l.Compile();
                object res = f();

                arguments.Add(res);
            }
            return arguments;
        }
    }
}