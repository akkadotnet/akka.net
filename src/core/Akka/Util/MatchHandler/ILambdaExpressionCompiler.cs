//-----------------------------------------------------------------------
// <copyright file="ILambdaExpressionCompiler.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq.Expressions;
using System.Reflection.Emit;

namespace Akka.Tools.MatchHandler
{
    /// <summary>
    /// TBD
    /// </summary>
    internal interface ILambdaExpressionCompiler
    {
        /// <summary>
        /// Produces a delegate that represents the lambda expression.
        /// </summary>
        /// <param name="expression">The expression to compile</param>
        /// <returns>A delegate containing the compiled version of the lambda.</returns>
        Delegate Compile(LambdaExpression expression);
    }
}

