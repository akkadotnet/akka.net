using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;

namespace Akka.Tools.MatchHandler
{
    public interface IMatchExpressionBuilder
    {
        MatchExpressionBuilderResult BuildLambdaExpression(IReadOnlyList<TypeHandler> typeHandlers);
        object[] CreateArgumentValuesArray(IReadOnlyList<Argument> arguments);
    }
}