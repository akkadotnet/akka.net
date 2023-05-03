//-----------------------------------------------------------------------
// <copyright file="MatchExpressionBuilderResult.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq.Expressions;

namespace Akka.Tools.MatchHandler
{
    /// <summary>
    /// TBD
    /// </summary>
    internal class MatchExpressionBuilderResult
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="lambdaExpression">TBD</param>
        /// <param name="arguments">TBD</param>
        /// <returns>TBD</returns>
        public MatchExpressionBuilderResult(LambdaExpression lambdaExpression, object[] arguments)
        {
            LambdaExpression = lambdaExpression;
            Arguments = arguments;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public LambdaExpression LambdaExpression { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public object[] Arguments { get; }
    }
}

