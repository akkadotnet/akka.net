//-----------------------------------------------------------------------
// <copyright file="MatchExpressionBuilder_BuildLambdaExpression_Tests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.TestKit;
using Akka.Tools.MatchHandler;
using Xunit;

namespace Akka.Tests.MatchHandler
{
    public class MatchExpressionBuilder_BuildLambdaExpression_Tests
    {
        [Fact]
        public void Build_one_action()
        {
            string updatedValue = null;

            //Handle a string with one action
            var typeHandlers = new List<TypeHandler>();
            var stringHandler = new TypeHandler(typeof(string));
            Action<string> action = s => { updatedValue = s; };
            var handler = PredicateAndHandler.CreateAction(action);
            stringHandler.Handlers.Add(handler);
            typeHandlers.Add(stringHandler);
            var builder = new MatchExpressionBuilder<object>();

            //Build the expression
            var result = builder.BuildLambdaExpression(typeHandlers);
            //Verify returned arguments
            XAssert.Equal(1, result.Arguments.Length, "Should only contain the action delegate");
            Assert.Same(action, result.Arguments[0]);

            //Compile the expression and test it
            var lambda = (Func<object, Action<String>, bool>)result.LambdaExpression.Compile();
            lambda("the value", action);
            Assert.Equal("the value", updatedValue);
            lambda(4711, action);
            Assert.Equal("the value", updatedValue);
        }

        [Fact]
        public void Build_one_action_for_same_type()
        {
            string updatedValue = null;

            //Handle a string with one action
            var typeHandlers = new List<TypeHandler>();
            var stringHandler = new TypeHandler(typeof(string));
            Action<string> action = s => { updatedValue = s; };
            var handler = PredicateAndHandler.CreateAction(action);
            stringHandler.Handlers.Add(handler);
            typeHandlers.Add(stringHandler);
            var builder = new MatchExpressionBuilder<string>();

            //Build the expression
            var result = builder.BuildLambdaExpression(typeHandlers);
            //Verify returned arguments
            XAssert.Equal(1, result.Arguments.Length, "Should only contain the action delegate");
            Assert.Same(action, result.Arguments[0]);

            //Compile the expression and test it
            var lambda = (Func<string, Action<String>, bool>)result.LambdaExpression.Compile();
            lambda("the value", action);
            Assert.Equal("the value", updatedValue);
        }

        [Fact]
        public void Build_one_action_and_predicate()
        {
            string updatedValue = null;

            //Handle a string with one action
            var typeHandlers = new List<TypeHandler>();
            var stringHandler = new TypeHandler(typeof(string));
            Action<string> action = s => { updatedValue = s; };
            Predicate<string> predicate = s => s.Length > 5;
            var handler = PredicateAndHandler.CreateAction(action, predicate: predicate);
            stringHandler.Handlers.Add(handler);
            typeHandlers.Add(stringHandler);
            var builder = new MatchExpressionBuilder<object>();

            //Build the expression
            var result = builder.BuildLambdaExpression(typeHandlers);
            //Verify returned arguments
            XAssert.Equal(2, result.Arguments.Length, "Should contain the action and predicate delegate");
            Assert.Same(action, result.Arguments[0]);
            Assert.Same(predicate, result.Arguments[1]);

            //Compile the expression and test it
            var lambda = (Func<object, Action<String>, Predicate<string>, bool>)result.LambdaExpression.Compile();
            lambda("short", action, predicate);
            Assert.Null(updatedValue);
            lambda("longer value", action, predicate);
            Assert.Equal("longer value", updatedValue);
            lambda(4711, action, predicate);
            Assert.Equal("longer value", updatedValue);
        }

        [Fact]
        public void Build_two_actions_and_predicate_for_same_type()
        {
            string updatedValue = null;

            //Handle a string with one action
            var typeHandlers = new List<TypeHandler>();
            var stringHandler = new TypeHandler(typeof(string));
            Action<string> action1 = s => { updatedValue = s; };
            Predicate<string> predicate1 = s => s.Length > 5 && s.Length < 15;
            var handler1 = PredicateAndHandler.CreateAction(action1, predicate: predicate1);
            stringHandler.Handlers.Add(handler1);
            Action<string> action2 = s => { updatedValue = "action2"; };
            Predicate<string> predicate2 = s => s.Length >= 15 && s.Length < 20;
            var handler2 = PredicateAndHandler.CreateAction(action2, predicate: predicate2);
            stringHandler.Handlers.Add(handler2);
            typeHandlers.Add(stringHandler);
            var builder = new MatchExpressionBuilder<object>();

            //Build the expression
            var result = builder.BuildLambdaExpression(typeHandlers);
            //Verify returned arguments
            XAssert.Equal(4, result.Arguments.Length, "Should contain the action and predicate delegate");
            Assert.Same(action1, result.Arguments[0]);
            Assert.Same(predicate1, result.Arguments[1]);
            Assert.Same(action2, result.Arguments[2]);
            Assert.Same(predicate2, result.Arguments[3]);

            //Compile the expression and test it
            var lambda = (Func<object, Action<String>, Predicate<string>, Action<String>, Predicate<string>, bool>)result.LambdaExpression.Compile();
            lambda("short", action1, predicate1, action2, predicate2);
            Assert.Null(updatedValue);
            lambda("longer value", action1, predicate1, action2, predicate2);
            Assert.Equal("longer value", updatedValue);
            lambda("1234567890123456789", action1, predicate1, action2, predicate2);
            Assert.Equal("action2", updatedValue);
            lambda(4711, action1, predicate1, action2, predicate2);
            Assert.Equal("action2", updatedValue);
        }
        [Fact]
        public void Build_two_actions_and_predicate_for_different_types()
        {
            string updatedValue = null;

            //Handle a string with one action
            var typeHandlers = new List<TypeHandler>();
            var stringHandler = new TypeHandler(typeof(string));
            Action<string> action1 = s => { updatedValue = s; };
            Predicate<string> predicate1 = s => s.Length > 5 && s.Length < 15;
            var handler1 = PredicateAndHandler.CreateAction(action1, predicate: predicate1);
            stringHandler.Handlers.Add(handler1);
            typeHandlers.Add(stringHandler);

            var intHandler = new TypeHandler(typeof(int));
            //Handle an int with one action
            Action<int> action2 = i => { updatedValue = i.ToString(); };
            Predicate<int> predicate2 = i => i < 10;
            var handler2 = PredicateAndHandler.CreateAction(action2, predicate: predicate2);
            intHandler.Handlers.Add(handler2);
            typeHandlers.Add(intHandler);

            //Build the expression
            var builder = new MatchExpressionBuilder<object>();
            var result = builder.BuildLambdaExpression(typeHandlers);

            //Verify returned arguments
            XAssert.Equal(4, result.Arguments.Length, "Should contain the action and predicate delegates");
            Assert.Same(action1, result.Arguments[0]);
            Assert.Same(predicate1, result.Arguments[1]);
            Assert.Same(action2, result.Arguments[2]);
            Assert.Same(predicate2, result.Arguments[3]);

            //Compile the expression and test it
            var lambda = (Func<object, Action<String>, Predicate<string>, Action<int>, Predicate<int>, bool>)result.LambdaExpression.Compile();
            lambda("short", action1, predicate1, action2, predicate2);
            Assert.Null(updatedValue);
            lambda("longer value", action1, predicate1, action2, predicate2);
            Assert.Equal("longer value", updatedValue);
            lambda("12345678901234567890", action1, predicate1, action2, predicate2);
            Assert.Equal("longer value", updatedValue);
            lambda(2, action1, predicate1, action2, predicate2);
            Assert.Equal("2", updatedValue);
            lambda(4711, action1, predicate1, action2, predicate2);
            Assert.Equal("2", updatedValue);
        }

        [Fact]
        public void Build_two_funcs_for_different_types()
        {
            string updatedValue = null;

            //Handle a string with one action
            var typeHandlers = new List<TypeHandler>();
            var stringHandler = new TypeHandler(typeof(string));
            Func<string, bool> func1 = s =>
            {
                if(s.Length > 5)
                {
                    updatedValue = s;
                    return true;
                }
                return false;
            };
            var handler1 = PredicateAndHandler.CreateFunc(func1);
            stringHandler.Handlers.Add(handler1);
            typeHandlers.Add(stringHandler);

            var intHandler = new TypeHandler(typeof(int));
            //Handle an int with one action
            Func<int, bool> func2 = i =>
            {
                if(i < 10)
                {
                    updatedValue = i.ToString();
                    return true;
                }
                return false;
            };
            var handler2 = PredicateAndHandler.CreateFunc(func2);
            intHandler.Handlers.Add(handler2);
            typeHandlers.Add(intHandler);

            //Build the expression
            var builder = new MatchExpressionBuilder<object>();
            var result = builder.BuildLambdaExpression(typeHandlers);

            //Verify returned arguments
            XAssert.Equal(2, result.Arguments.Length, "Should contain the funcs");
            Assert.Same(func1, result.Arguments[0]);
            Assert.Same(func2, result.Arguments[1]);

            //Compile the expression and test it
            var lambda = (Func<object, Func<String, bool>, Func<int, bool>, bool>)result.LambdaExpression.Compile();
            lambda("short", func1, func2);
            Assert.Null(updatedValue);
            lambda("longer value", func1, func2);
            Assert.Equal("longer value", updatedValue);
            lambda(2, func1, func2);
            Assert.Equal("2", updatedValue);
            lambda(4711, func1, func2);
            Assert.Equal("2", updatedValue);
        }

        [Fact]
        public void Build_three_funcs_for_two_different_types()
        {
            string updatedValue = null;

            //Handle a string with one action
            var typeHandlers = new List<TypeHandler>();
            var stringHandler1 = new TypeHandler(typeof(string));
            Func<string, bool> func1 = s =>
            {
                if(s.Length > 5)
                {
                    updatedValue = s;
                    return true;
                }
                return false;
            };
            var handler1 = PredicateAndHandler.CreateFunc(func1);
            stringHandler1.Handlers.Add(handler1);
            typeHandlers.Add(stringHandler1);

            var intHandler = new TypeHandler(typeof(int));
            //Handle an int with one action
            Func<int, bool> func2 = i =>
            {
                if(i < 10)
                {
                    updatedValue = i.ToString();
                    return true;
                }
                return false;
            };
            var handler2 = PredicateAndHandler.CreateFunc(func2);
            intHandler.Handlers.Add(handler2);
            typeHandlers.Add(intHandler);

            var stringHandler2 = new TypeHandler(typeof(string));
            Func<string, bool> func3 = s =>
            {
                updatedValue = "func3:" + s;
                return true;
            };
            var handler3 = PredicateAndHandler.CreateFunc(func3);
            stringHandler2.Handlers.Add(handler3);
            typeHandlers.Add(stringHandler2);

            //Build the expression
            var builder = new MatchExpressionBuilder<object>();
            var result = builder.BuildLambdaExpression(typeHandlers);

            //Verify returned arguments
            XAssert.Equal(3, result.Arguments.Length, "Should contain the funcs");
            Assert.Same(func1, result.Arguments[0]);
            Assert.Same(func2, result.Arguments[1]);
            Assert.Same(func3, result.Arguments[2]);

            //Compile the expression and test it
            var lambda = (Func<object, Func<String, bool>, Func<int, bool>, Func<String, bool>, bool>)result.LambdaExpression.Compile();
            lambda("short", func1, func2, func3);
            Assert.Equal("func3:short", updatedValue);
            lambda("longer value", func1, func2, func3);
            Assert.Equal("longer value", updatedValue);
            lambda(2, func1, func2, func3);
            Assert.Equal("2", updatedValue);
            lambda(4711, func1, func2, func3);
            Assert.Equal("2", updatedValue);
        }


        [Fact]
        public void Build_with_exactly_15_arguments()
        {
            //15 is the maximum number of arguments without having to put actions/funcs/predicates in an object array.
            //This comes from BuildLambdaExpression builds a Func, and Funcs may only have 16 input args: Func<T1,...,T16,TResult>
            //T1-argument is reserved for the obligatory value argument, which leaves us with 15 arguments to be used for the
            //actions/funcs/predicates
            string updatedValue = null;

            //Handle a string with one action
            var typeHandlers = new List<TypeHandler>();
            var stringHandler = new TypeHandler(typeof(string));
            Action<string> actionString = s => { };
            Predicate<string> predicateString = s => false;
            for(var i = 0; i < 7; i++)
            {
                stringHandler.Handlers.Add(PredicateAndHandler.CreateAction(actionString, predicate: predicateString));
            }
            typeHandlers.Add(stringHandler);

            var intHandler = new TypeHandler(typeof(int));
            //Handle an int with one action
            Action<int> actionInt = i => { updatedValue = i.ToString(); };
            var handler2 = PredicateAndHandler.CreateAction(actionInt);
            intHandler.Handlers.Add(handler2);
            typeHandlers.Add(intHandler);

            //Build the expression
            var builder = new MatchExpressionBuilder<object>();
            var result = builder.BuildLambdaExpression(typeHandlers);

            //Verify returned arguments
            XAssert.Equal(15, result.Arguments.Length, "Should contain the action and predicate delegates");
            var lastArgument = result.Arguments.Last();
            Assert.IsNotType<object[]>(lastArgument); //Last argument should not be an object[]

            //Compile the expression and test it
            var lambda = (Func<object, Action<String>, Predicate<string>, Action<String>, Predicate<string>, Action<String>, Predicate<string>, Action<String>, Predicate<string>, Action<String>, Predicate<string>, Action<String>, Predicate<string>, Action<String>, Predicate<string>, Action<int>, bool>)
                result.LambdaExpression.Compile();

            lambda("some value", actionString, predicateString, actionString, predicateString, actionString, predicateString, actionString, predicateString, actionString, predicateString, actionString, predicateString, actionString, predicateString, actionInt);
            Assert.Null(updatedValue);
            lambda(4711, actionString, predicateString, actionString, predicateString, actionString, predicateString, actionString, predicateString, actionString, predicateString, actionString, predicateString, actionString, predicateString, actionInt);
            Assert.Equal("4711", updatedValue);
        }

        [Fact]
        public void Build_with_more_than_15_arguments()
        {
            //15 is the maximum number of arguments without having to put actions/funcs/predicates in an object array.
            //This comes from BuildLambdaExpression builds a Func, and Funcs may only have 16 input args: Func<T1,...,T16,TResult>
            //T1-argument is reserved for the obligatory value argument, which leaves us with 15 arguments to be used for the
            //actions/funcs/predicates. If we use more than that T16-argument will be an object[] containing the rest

            string updatedValue = null;

            //Handle a string with one action
            var typeHandlers = new List<TypeHandler>();
            var stringHandler = new TypeHandler(typeof(string));
            Action<string> actionString = s => { };
            Predicate<string> predicateString = s => false;
            for(var i = 0; i < 7; i++)
            {
                stringHandler.Handlers.Add(PredicateAndHandler.CreateAction(actionString, predicate: predicateString));
            }
            typeHandlers.Add(stringHandler);

            var intHandler = new TypeHandler(typeof(int));
            //Handle an int with one action
            Action<int> actionInt = i => { updatedValue = i.ToString(); };
            Predicate<int> predicateInt = i => i < 100;
            var handler2 = PredicateAndHandler.CreateAction(actionInt, predicate: predicateInt);
            intHandler.Handlers.Add(handler2);
            typeHandlers.Add(intHandler);

            //Build the expression
            var builder = new MatchExpressionBuilder<object>();
            var result = builder.BuildLambdaExpression(typeHandlers);

            //Verify returned arguments
            XAssert.Equal(15, result.Arguments.Length, "Should contain the action and predicate delegates");
            var lastArgument = result.Arguments.Last();
            Assert.IsType<object[]>(lastArgument);
            var extraParamsArray = (object[])lastArgument;
            Assert.Equal(2, extraParamsArray.Length);
            Assert.Same(actionInt, extraParamsArray[0]);
            Assert.Same(predicateInt, extraParamsArray[1]);

            //Compile the expression and test it
            var lambda = (Func<object, Action<String>, Predicate<string>, Action<String>, Predicate<string>, Action<String>, Predicate<string>, Action<String>, Predicate<string>, Action<String>, Predicate<string>, Action<String>, Predicate<string>, Action<String>, Predicate<string>, object[], bool>)
                result.LambdaExpression.Compile();

            lambda("some value", actionString, predicateString, actionString, predicateString, actionString, predicateString, actionString, predicateString, actionString, predicateString, actionString, predicateString, actionString, predicateString, extraParamsArray);
            Assert.Null(updatedValue);
            lambda(4711, actionString, predicateString, actionString, predicateString, actionString, predicateString, actionString, predicateString, actionString, predicateString, actionString, predicateString, actionString, predicateString, extraParamsArray);
            Assert.Null(updatedValue);
            lambda(42, actionString, predicateString, actionString, predicateString, actionString, predicateString, actionString, predicateString, actionString, predicateString, actionString, predicateString, actionString, predicateString, extraParamsArray);
            Assert.Equal("42", updatedValue);

        }

        [Fact]
        public void Build_one_action_that_expects_base_type()
        {
            string updatedValue = null;

            //Handle a string with one action
            var typeHandlers = new List<TypeHandler>();
            var stringHandler = new TypeHandler(typeof(string));
            Action<object> action = s => { updatedValue = s is string ? "WasString:" + (string)s : "WasNotString" + s.ToString(); };
            var handler = PredicateAndHandler.CreateAction(action, handlerFirstArgumentShouldBeBaseType: true);
            stringHandler.Handlers.Add(handler);
            typeHandlers.Add(stringHandler);
            var builder = new MatchExpressionBuilder<object>();

            //Build the expression
            var result = builder.BuildLambdaExpression(typeHandlers);
            //Verify returned arguments
            XAssert.Equal(1, result.Arguments.Length, "Should only contain the action delegate");
            Assert.Same(action, result.Arguments[0]);

            //Compile the expression and test it
            var lambda = (Func<object, Action<object>, bool>)result.LambdaExpression.Compile();
            lambda(4711, action);
            Assert.Null(updatedValue);
            lambda("the value", action);
            Assert.Equal("WasString:the value", updatedValue);
        }

        [Fact]
        public void Build_one_action_and_predicate_that_expects_base_type()
        {
            string updatedValue = null;

            //Handle a string with one action
            var typeHandlers = new List<TypeHandler>();
            var stringHandler = new TypeHandler(typeof(string));
            Action<object> action = s => { updatedValue = (string)s; };
            Predicate<object> predicate = s => ((string) s).Length > 5;
            var handler = PredicateAndHandler.CreateAction(action,predicate, handlerFirstArgumentShouldBeBaseType: true);
            stringHandler.Handlers.Add(handler);
            typeHandlers.Add(stringHandler);
            var builder = new MatchExpressionBuilder<object>();

            //Build the expression
            var result = builder.BuildLambdaExpression(typeHandlers);
            //Verify returned arguments
            XAssert.Equal(2, result.Arguments.Length, "Should only contain the action delegate");
            Assert.Same(action, result.Arguments[0]);
            Assert.Same(predicate, result.Arguments[1]);

            //Compile the expression and test it
            var lambda = (Func<object, Action<object>, Predicate<Object>, bool>)result.LambdaExpression.Compile();
            lambda(4711, action, predicate);
            Assert.Null(updatedValue);
            lambda("short", action, predicate);
            Assert.Null(updatedValue);
            lambda("the value", action, predicate);
            Assert.Equal("the value", updatedValue);
        }

        [Fact]
        public void Build_one_func_that_expects_base_type()
        {
            string updatedValue = null;

            //Handle a string with one action
            var typeHandlers = new List<TypeHandler>();
            var stringHandler = new TypeHandler(typeof(string));
            Func<object,bool> func = o =>
            {
                var s = (string) o;
                if(s.Length > 5)
                {
                    updatedValue = s;
                    return true;
                }
                return false;
            };
            var handler = PredicateAndHandler.CreateAction(func, handlerFirstArgumentShouldBeBaseType: true);
            stringHandler.Handlers.Add(handler);
            typeHandlers.Add(stringHandler);
            var builder = new MatchExpressionBuilder<object>();

            //Build the expression
            var result = builder.BuildLambdaExpression(typeHandlers);
            //Verify returned arguments
            XAssert.Equal(1, result.Arguments.Length, "Should only contain the action delegate");
            Assert.Same(func, result.Arguments[0]);

            //Compile the expression and test it
            var lambda = (Func<object, Func<object, bool>, bool>)result.LambdaExpression.Compile();
            lambda(4711, func);
            Assert.Null(updatedValue);
            lambda("short", func);
            Assert.Null(updatedValue);
            lambda("the value", func);
            Assert.Equal("the value", updatedValue);
        }

    }
}

