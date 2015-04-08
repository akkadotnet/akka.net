//-----------------------------------------------------------------------
// <copyright file="IMatchExpressionBuilder.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;

namespace Akka.Tools.MatchHandler
{
    public interface IMatchExpressionBuilder
    {
        MatchExpressionBuilderResult BuildLambdaExpression(IReadOnlyList<TypeHandler> typeHandlers);
        object[] CreateArgumentValuesArray(IReadOnlyList<Argument> arguments);
    }
}
