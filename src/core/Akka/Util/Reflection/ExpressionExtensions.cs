//-----------------------------------------------------------------------
// <copyright file="ExpressionExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Akka.Util.Reflection
{
    /// <summary>
    /// TBD
    /// </summary>
    public static class ExpressionExtensions
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="newExpression">TBD</param>
        /// <returns>TBD</returns>
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

