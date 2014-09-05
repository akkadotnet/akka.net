using System;
using System.Linq.Expressions;
using System.Reflection.Emit;

namespace Akka.Tools.MatchHandler
{
    public class LambdaExpressionCompiler : ILambdaExpressionCompiler
    {
        public Delegate Compile(LambdaExpression expression)
        {
            return expression.Compile();
        }

        public void CompileToMethod(LambdaExpression expression, MethodBuilder method)
        {
            expression.CompileToMethod(method);
        }
    }
}