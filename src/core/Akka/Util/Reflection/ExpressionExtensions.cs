//-----------------------------------------------------------------------
// <copyright file="ExpressionExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Akka.Util.Reflection
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
