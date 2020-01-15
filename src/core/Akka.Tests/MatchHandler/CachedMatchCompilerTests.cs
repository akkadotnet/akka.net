//-----------------------------------------------------------------------
// <copyright file="CachedMatchCompilerTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection.Emit;
using Akka.TestKit;
using Akka.Tools.MatchHandler;
using Xunit;

namespace Akka.Tests.MatchHandler
{
    public class CachedMatchCompilerTests
    {
        [Fact]
        public void When_compiling_first_time_correct_calls_are_made_to_MatchExpressionBuilder_and_PartialActionBuilder()
        {
            //Arrange
            object[] argumentValues = new object[0];
            Expression<Func<object, bool>> lambdaExpression = _ => true;
            var matchExpressionBuilder = new DummyMatchExpressionBuilder()
            {
                BuildLambdaExpressionResult = new MatchExpressionBuilderResult(lambdaExpression, argumentValues),
            };

            Func<object, bool> deleg = _ => true;
            var expressionCompiler = new DummyLambdaExpressionCompiler()
            {
                CompileResult = deleg,
            };

            PartialAction<object> partialAction = item => true;
            var partialActionBuilder = new DummyPartialActionBuilder()
            {
                BuildResult = partialAction,
            };

            var compiler = new CachedMatchCompiler<object>(matchExpressionBuilder, partialActionBuilder, expressionCompiler);

            //Act
            var typeHandlers = new List<TypeHandler>();
            var arguments = new List<Argument>();
            var resultPartialAction = compiler.Compile(typeHandlers, arguments, new MatchBuilderSignature(null));

            //Assert
            AssertOneCall(to: matchExpressionBuilder.BuildLambdaExpressionCalls, withArgument: typeHandlers, description: "BuildLambdaExpression");
            AssertOneCall(to: expressionCompiler.CompileCalls, withArgument: lambdaExpression, description: "Compile");
            AssertOneCall(to: partialActionBuilder.BuildCalls, description: "Build", check: i => ReferenceEquals(i.CompiledDelegate, deleg) && ReferenceEquals(i.DelegateArguments, argumentValues));
            Assert.Same(partialAction, resultPartialAction);

            AssertNoCall(to: matchExpressionBuilder.CreateArgumentValuesArrayCalls, description: "CreateArgumentValuesArray");
        }

        [Fact]
        public void When_compiling_second_time_with_same_signature_the_cached_version_should_be_used()
        {
            //Arrange
            object[] argumentValues = new object[0];
            Expression<Func<object, bool>> lambdaExpression = _ => true;
            var matchExpressionBuilder = new DummyMatchExpressionBuilder()
            {
                BuildLambdaExpressionResult = new MatchExpressionBuilderResult(lambdaExpression, argumentValues),
                CreateArgumentValuesArrayResult = argumentValues,
            };

            Func<object, bool> deleg = _ => true;
            var expressionCompiler = new DummyLambdaExpressionCompiler()
            {
                CompileResult = deleg,
            };

            PartialAction<object> partialAction = item => true;
            var partialActionBuilder = new DummyPartialActionBuilder()
            {
                BuildResult = partialAction,
            };

            var compiler = new CachedMatchCompiler<object>(matchExpressionBuilder, partialActionBuilder, expressionCompiler);

            var typeHandlers = new List<TypeHandler>();
            var arguments = new List<Argument>();
            compiler.Compile(typeHandlers, arguments, new MatchBuilderSignature(null));
            matchExpressionBuilder.ResetCalls();
            partialActionBuilder.ResetCalls();
            expressionCompiler.ResetCalls();

            //Act
            var resultPartialAction = compiler.Compile(typeHandlers, arguments, new MatchBuilderSignature(null));

            //Assert

            AssertOneCall(to: matchExpressionBuilder.CreateArgumentValuesArrayCalls, withArgument: arguments, description: "CreateArgumentValuesArray");
            AssertOneCall(to: partialActionBuilder.BuildCalls, description: "Build", check: i => ReferenceEquals(i.CompiledDelegate, deleg) && ReferenceEquals(i.DelegateArguments, argumentValues));
            Assert.Same(partialAction, resultPartialAction);

            AssertNoCall(to: matchExpressionBuilder.BuildLambdaExpressionCalls, description: "BuildLambdaExpression");
            AssertNoCall(to: expressionCompiler.CompileCalls, description: "Compile");
        }

        [Fact]
        public void When_getting_Instance_Then_same_instance_should_be_returned_every_time()
        {
            var firstInstance = CachedMatchCompiler<object>.Instance;
            var secondInstance = CachedMatchCompiler<object>.Instance;
            Assert.Same(firstInstance,secondInstance);
        }


        private void AssertNoCall<T>(IReadOnlyList<T> to, string description, Action<T, T, string> assert = null)
        {
            XAssert.Equal(0, to.Count, description + ": Expected no calls but in fact " + to.Count + " calls were made");
        }

        private void AssertOneCall<T>(IReadOnlyList<T> to, T withArgument, string description)
        {
            XAssert.Equal(1, to.Count, description + ": Expected one call but in fact " + to.Count + " calls were made");
            XAssert.Same(to[0], withArgument, description + ": Arguments do not match");
        }

        private void AssertOneCall<T>(IReadOnlyList<T> to, string description, Func<T, bool> check)
        {
            XAssert.Equal(1, to.Count, description + ": Expected one call but in fact " + to.Count + " calls were made");
            Assert.True(check(to[0]), description + ": did not match");
        }

        private class DummyMatchExpressionBuilder : IMatchExpressionBuilder
        {
            public readonly List<IReadOnlyList<TypeHandler>> BuildLambdaExpressionCalls = new List<IReadOnlyList<TypeHandler>>();
            public readonly List<IReadOnlyList<Argument>> CreateArgumentValuesArrayCalls = new List<IReadOnlyList<Argument>>();
            public MatchExpressionBuilderResult BuildLambdaExpressionResult;
            public object[] CreateArgumentValuesArrayResult;


            public MatchExpressionBuilderResult BuildLambdaExpression(IReadOnlyList<TypeHandler> typeHandlers)
            {
                BuildLambdaExpressionCalls.Add(typeHandlers);
                return BuildLambdaExpressionResult;
            }

            public object[] CreateArgumentValuesArray(IReadOnlyList<Argument> arguments)
            {
                CreateArgumentValuesArrayCalls.Add(arguments);
                return CreateArgumentValuesArrayResult;
            }

            public void ResetCalls()
            {
                BuildLambdaExpressionCalls.Clear();
                CreateArgumentValuesArrayCalls.Clear();
            }
        }

        private class DummyPartialActionBuilder : IPartialActionBuilder
        {
            public PartialAction<object> BuildResult;
            public List<CompiledMatchHandlerWithArguments> BuildCalls = new List<CompiledMatchHandlerWithArguments>();

            public PartialAction<T> Build<T>(CompiledMatchHandlerWithArguments handlerAndArgs)
            {
                Assert.True(typeof(T) == typeof(object), "The type should be object");
                BuildCalls.Add(handlerAndArgs);
                return (PartialAction<T>)(object)BuildResult;
            }

            public void ResetCalls()
            {
                BuildCalls.Clear();
            }
        }

        private class DummyLambdaExpressionCompiler : ILambdaExpressionCompiler
        {
            public Delegate CompileResult;
            public List<LambdaExpression> CompileCalls = new List<LambdaExpression>();
            public Delegate Compile(LambdaExpression expression)
            {
                CompileCalls.Add(expression);
                return CompileResult;
            }

            public void CompileToMethod(LambdaExpression expression, MethodBuilder method)
            {
                throw new NotImplementedException();
            }

            public void ResetCalls()
            {
                CompileCalls.Clear();
            }
        }

    }
}

