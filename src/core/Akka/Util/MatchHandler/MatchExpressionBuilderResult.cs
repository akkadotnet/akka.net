//-----------------------------------------------------------------------
// <copyright file="MatchExpressionBuilderResult.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq.Expressions;

namespace Akka.Tools.MatchHandler
{
    public class MatchExpressionBuilderResult
    {
        private readonly LambdaExpression _lambdaExpression;
        private readonly object[] _arguments;

        public MatchExpressionBuilderResult(LambdaExpression lambdaExpression, object[] arguments)
        {
            _lambdaExpression = lambdaExpression;
            _arguments = arguments;
        }

        public LambdaExpression LambdaExpression { get { return _lambdaExpression; } }

        public object[] Arguments { get { return _arguments; } }
    }
}

