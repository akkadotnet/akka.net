﻿//-----------------------------------------------------------------------
// <copyright file="LambdaExpressionCompiler.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq.Expressions;

namespace Akka.Tools.MatchHandler
{
    /// <summary>
    /// TBD
    /// </summary>
    internal class LambdaExpressionCompiler : ILambdaExpressionCompiler
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="expression">TBD</param>
        /// <returns>TBD</returns>
        public Delegate Compile(LambdaExpression expression)
        {
            return expression.Compile();
        }
    }
}

