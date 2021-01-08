//-----------------------------------------------------------------------
// <copyright file="CachedMatchCompiler.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace Akka.Tools.MatchHandler
{
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    internal class CachedMatchCompiler<T> : IMatchCompiler<T>
    {
        private readonly IMatchExpressionBuilder _expressionBuilder;
        private readonly IPartialActionBuilder _actionBuilder;
        private readonly ILambdaExpressionCompiler _expressionCompiler;
        private readonly ConcurrentDictionary<MatchBuilderSignature, Delegate> _cache = new ConcurrentDictionary<MatchBuilderSignature, Delegate>();
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly CachedMatchCompiler<T> Instance = new CachedMatchCompiler<T>(new MatchExpressionBuilder<T>(), new PartialActionBuilder(), new LambdaExpressionCompiler());

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="expressionBuilder">TBD</param>
        /// <param name="actionBuilder">TBD</param>
        /// <param name="expressionCompiler">TBD</param>
        public CachedMatchCompiler(IMatchExpressionBuilder expressionBuilder, IPartialActionBuilder actionBuilder, ILambdaExpressionCompiler expressionCompiler)
        {
            _expressionBuilder = expressionBuilder;
            _actionBuilder = actionBuilder;
            _expressionCompiler = expressionCompiler;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handlers">TBD</param>
        /// <param name="capturedArguments">TBD</param>
        /// <param name="signature">TBD</param>
        /// <returns>TBD</returns>
        public PartialAction<T> Compile(IReadOnlyList<TypeHandler> handlers, IReadOnlyList<Argument> capturedArguments, MatchBuilderSignature signature)
        {
            object[] delegateArguments = null;
            var compiledDelegate = _cache.GetOrAdd(signature, _ => CompileToDelegate(handlers, capturedArguments, out delegateArguments));

            //If we got a cached version of the delegate we need to restructure the captured arguments suitable for the delegate
            if(delegateArguments == null)
            {
                delegateArguments = _expressionBuilder.CreateArgumentValuesArray(capturedArguments);
            }

            var partialAction = _actionBuilder.Build<T>(new CompiledMatchHandlerWithArguments(compiledDelegate, delegateArguments));
            return partialAction;
        }

        private Delegate CompileToDelegate(IReadOnlyList<TypeHandler> handlers, IReadOnlyList<Argument> capturedArguments, out object[] delegateArguments)
        {
            var result = _expressionBuilder.BuildLambdaExpression(handlers);
            var compiledLambda = _expressionCompiler.Compile(result.LambdaExpression);
            delegateArguments = result.Arguments;
            return compiledLambda;
        }

#if !CORECLR
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handlers">TBD</param>
        /// <param name="capturedArguments">TBD</param>
        /// <param name="signature">TBD</param>
        /// <param name="typeBuilder">TBD</param>
        /// <param name="methodName">TBD</param>
        /// <param name="methodAttributes">TBD</param>
        /// <returns>TBD</returns>
        public void CompileToMethod(IReadOnlyList<TypeHandler> handlers, IReadOnlyList<Argument> capturedArguments, MatchBuilderSignature signature, TypeBuilder typeBuilder, string methodName, MethodAttributes methodAttributes = MethodAttributes.Public | MethodAttributes.Static)
        {
            var result = _expressionBuilder.BuildLambdaExpression(handlers);
            var lambdaExpression = result.LambdaExpression;
            var parameterTypes = lambdaExpression.Parameters.Select(p => p.Type).ToArray();
            var method = typeBuilder.DefineMethod(methodName, methodAttributes, typeof(bool), parameterTypes);
            _expressionCompiler.CompileToMethod(lambdaExpression, method);
        }
#endif
    }
}

