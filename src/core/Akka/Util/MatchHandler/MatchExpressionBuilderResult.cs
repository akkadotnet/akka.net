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