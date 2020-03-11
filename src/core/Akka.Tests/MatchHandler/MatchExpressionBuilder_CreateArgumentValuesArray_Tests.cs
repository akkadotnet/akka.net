//-----------------------------------------------------------------------
// <copyright file="MatchExpressionBuilder_CreateArgumentValuesArray_Tests.cs" company="Akka.NET Project">
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
    // ReSharper disable once InconsistentNaming
    public class MatchExpressionBuilder_CreateArgumentValuesArray_Tests
    {

        [Fact]
        public void Given_no_arguments_When_creating_Then_empty_array_is_returned()
        {
            var emptyArguments = new List<Argument>();
            var builder = new MatchExpressionBuilder<object>();

            var result = builder.CreateArgumentValuesArray(emptyArguments);
            Assert.NotNull(result);
            Assert.Empty(result);
        }


        [Fact]
        public void Given_one_argument_When_creating_Then_array_with_the_value_is_returned()
        {
            var argument = new Argument(null, (Action<int>)(_ => { }), true);
            var arguments = new List<Argument> { argument };
            var builder = new MatchExpressionBuilder<object>();

            var result = builder.CreateArgumentValuesArray(arguments);
            Assert.NotNull(result);
            Assert.Single(result);
            Assert.Same(argument.Value, result[0]);
        }

        [Fact]
        public void Given_15_arguments_When_creating_Then_array_with_the_values_are_returned()
        {
            var arguments = Enumerable.Range(1, 15).Select(i => new Argument(null, (Action<int>)(_ => { }), true)).ToList();
            var builder = new MatchExpressionBuilder<object>();

            var result = builder.CreateArgumentValuesArray(arguments);
            Assert.NotNull(result);
            Assert.Equal(15, result.Length);
            for(var i = 0; i < 15; i++)
            {

                XAssert.Same(arguments[i].Value, result[0], "Argument " + i + " does not have the correct value");
            }
        }


        [Fact]
        public void Given_16_arguments_When_creating_Then_last_value_is_an_object_array_with_argument_14_and_15()
        {
            var arguments = Enumerable.Range(1, 16).Select(i => new Argument(null, (Action<int>)(_ => { }), true)).ToList();
            var builder = new MatchExpressionBuilder<object>();

            var result = builder.CreateArgumentValuesArray(arguments);
            Assert.NotNull(result);
            Assert.Equal(15, result.Length);
            for(var i = 0; i < 14; i++)
            {
                XAssert.Same(arguments[i].Value, result[0], "Argument " + i + " does not have the correct value");
            }

            Assert.IsType<object[]>(result[14]);//Last value should be the extraArgs object[]
            var extraArgs = (object[])result[14];
            Assert.Equal(2, extraArgs.Length);// Extra args should contain 2 values (argument 14 and 15)
            XAssert.Same(arguments[14].Value, extraArgs[0], "Argument 14 did not match");
            XAssert.Same(arguments[15].Value, extraArgs[1], "Argument 15 did not match");
        }



        [Fact]
        public void Given_30_arguments_When_creating_Then_last_value_is_an_object_array_with_argument_14_to_29()
        {
            var arguments = Enumerable.Range(1, 30).Select(i => new Argument(null, (Action<int>)(_ => { }), true)).ToList();
            var builder = new MatchExpressionBuilder<object>();

            var result = builder.CreateArgumentValuesArray(arguments);
            Assert.NotNull(result);
            Assert.Equal(15, result.Length);
            for(var i = 0; i < 14; i++)
            {
                XAssert.Same(arguments[i].Value, result[0], "Argument " + i + " does not have the correct value");
            }

            Assert.IsType<object[]>(result[14]);//Last value should be the extraArgs object[]

            var extraArgs = (object[])result[14];
            Assert.Equal(16, extraArgs.Length);// Extra args should contain 16 values (argument 14 to 29)
            for(var i = 0; i < 16; i++)
            {
                XAssert.Same(arguments[i + 14].Value, extraArgs[i], "Argument " + (i + 14) + " did not match");
            }
        }
    }
}

