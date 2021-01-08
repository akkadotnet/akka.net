//-----------------------------------------------------------------------
// <copyright file="MatchExpressionBuilderResult.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        private readonly LambdaExpression _lambdaExpression;
        private readonly object[] _arguments;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="lambdaExpression">TBD</param>
        /// <param name="arguments">TBD</param>
        /// <returns>TBD</returns>
        public MatchExpressionBuilderResult(LambdaExpression lambdaExpression, object[] arguments)
        {
            _lambdaExpression = lambdaExpression;
            _arguments = arguments;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public LambdaExpression LambdaExpression { get { return _lambdaExpression; } }

        /// <summary>
        /// TBD
        /// </summary>
        public object[] Arguments { get { return _arguments; } }
    }
}

