//-----------------------------------------------------------------------
// <copyright file="LambdaExpressionCompiler.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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

