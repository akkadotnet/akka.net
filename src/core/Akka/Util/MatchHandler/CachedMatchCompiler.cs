//-----------------------------------------------------------------------
// <copyright file="CachedMatchCompiler.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
    public class CachedMatchCompiler<T> : IMatchCompiler<T>
    {
        private readonly IMatchExpressionBuilder _expressionBuilder;
        private readonly IPartialActionBuilder _actionBuilder;
        private readonly ILambdaExpressionCompiler _expressionCompiler;
        private readonly ConcurrentDictionary<MatchBuilderSignature, Delegate> _cache = new ConcurrentDictionary<MatchBuilderSignature, Delegate>();
        public static readonly CachedMatchCompiler<T> Instance = new CachedMatchCompiler<T>(new MatchExpressionBuilder<T>(), new PartialActionBuilder(), new LambdaExpressionCompiler());

        public CachedMatchCompiler(IMatchExpressionBuilder expressionBuilder, IPartialActionBuilder actionBuilder, ILambdaExpressionCompiler expressionCompiler)
        {
            _expressionBuilder = expressionBuilder;
            _actionBuilder = actionBuilder;
            _expressionCompiler = expressionCompiler;
        }

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

        public void CompileToMethod(IReadOnlyList<TypeHandler> handlers, IReadOnlyList<Argument> capturedArguments, MatchBuilderSignature signature, TypeBuilder typeBuilder, string methodName, MethodAttributes methodAttributes = MethodAttributes.Public | MethodAttributes.Static)
        {
            var result = _expressionBuilder.BuildLambdaExpression(handlers);
            var lambdaExpression = result.LambdaExpression;
            var parameterTypes = lambdaExpression.Parameters.Select(p => p.Type).ToArray();
            var method = typeBuilder.DefineMethod(methodName, methodAttributes, typeof(bool), parameterTypes);
            _expressionCompiler.CompileToMethod(lambdaExpression, method);
        }
    }
}
