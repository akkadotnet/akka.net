//-----------------------------------------------------------------------
// <copyright file="CachedMatchCompiler.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    /// Caching implementation of the match compiler that stores compiled expressions for reuse.
    /// </summary>
    /// <typeparam name="T">The type of messages to match against.</typeparam>
    internal class CachedMatchCompiler<T> : IMatchCompiler<T>
    {
        private readonly IMatchExpressionBuilder _expressionBuilder;
        private readonly IPartialActionBuilder _actionBuilder;
        private readonly ILambdaExpressionCompiler _expressionCompiler;
        private readonly ConcurrentDictionary<MatchBuilderSignature, Delegate> _cache = new();
        
        /// <summary>
        /// Singleton instance of the cached match compiler using default implementations.
        /// </summary>
        public static readonly CachedMatchCompiler<T> Instance = new(new MatchExpressionBuilder<T>(), new PartialActionBuilder(), new LambdaExpressionCompiler());

        /// <summary>
        /// Initializes a new instance of the <see cref="CachedMatchCompiler{T}"/> class.
        /// </summary>
        /// <param name="expressionBuilder">The expression builder to use for creating match expressions.</param>
        /// <param name="actionBuilder">The action builder to use for creating partial actions.</param>
        /// <param name="expressionCompiler">The compiler to use for compiling lambda expressions.</param>
        public CachedMatchCompiler(IMatchExpressionBuilder expressionBuilder, IPartialActionBuilder actionBuilder, ILambdaExpressionCompiler expressionCompiler)
        {
            _expressionBuilder = expressionBuilder;
            _actionBuilder = actionBuilder;
            _expressionCompiler = expressionCompiler;
        }

        /// <summary>
        /// Compiles a list of type handlers into a partial action, using cached versions when available.
        /// </summary>
        /// <param name="handlers">The list of type handlers to compile.</param>
        /// <param name="capturedArguments">The arguments captured by the match statement.</param>
        /// <param name="signature">The signature of the match builder, used as a cache key.</param>
        /// <returns>A compiled partial action that can match and handle messages.</returns>
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
    }
}

