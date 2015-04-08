using System.Collections.Generic;

namespace Akka.Tools.MatchHandler
{
    public interface IMatchExpressionBuilder
    {
        MatchExpressionBuilderResult BuildLambdaExpression(IReadOnlyList<TypeHandler> typeHandlers);
        object[] CreateArgumentValuesArray(IReadOnlyList<Argument> arguments);
    }
}