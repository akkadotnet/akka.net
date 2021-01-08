//-----------------------------------------------------------------------
// <copyright file="PartialActionBuilderTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Reflection;
using Akka.TestKit;
using Akka.Tools.MatchHandler;
using Xunit;

namespace Akka.Tests.MatchHandler
{
    public class PartialActionBuilderTests : AkkaSpec
    {
        [Fact]
        public void Given_a_0_arguments_delegate_When_building_and_invoking_Then_the_supplied_function_is_called()
        {
            var builder = new PartialActionBuilder();
            object updatedValue = null;
            Func<object, bool> deleg = value => { updatedValue = value; return true; };

            var partialAction = builder.Build<object>(new CompiledMatchHandlerWithArguments(deleg, new object[0]));
            partialAction("value");
            Assert.Same(updatedValue, "value");
        }

        [Fact]
        public void Given_a_1_arguments_delegate_When_building_and_invoking_Then_the_supplied_function_is_called()
        {
            var builder = new PartialActionBuilder();
            object updatedValue = null;
            var delegateArguments = new object[] { "a1" };
            var updatedArgs = new object[delegateArguments.Length];
            Func<object, string, bool> deleg = (value, a1) =>
            {
                updatedValue = value;
                updatedArgs[0] = a1;
                return true;
            };

            var partialAction = builder.Build<object>(new CompiledMatchHandlerWithArguments(deleg, delegateArguments));
            partialAction("value");
            Assert.Same("value", updatedValue);
            AssertAreSame(delegateArguments, updatedArgs);
        }

        [Fact]
        public void Given_a_2_arguments_delegate_When_building_and_invoking_Then_the_supplied_function_is_called()
        {
            var builder = new PartialActionBuilder();
            object updatedValue = null;
            var delegateArguments = new object[] { "a1", 1 };
            var updatedArgs = new object[delegateArguments.Length];
            Func<object, string, int, bool> deleg = (value, a1, a2) =>
            {
                updatedValue = value;
                updatedArgs[0] = a1;
                updatedArgs[1] = a2;
                return true;
            };

            var partialAction = builder.Build<object>(new CompiledMatchHandlerWithArguments(deleg, delegateArguments));
            partialAction("value");
            Assert.Same("value", updatedValue);
            AssertAreSame(delegateArguments, updatedArgs);
        }


        [Fact]
        public void Given_a_3_arguments_delegate_When_building_and_invoking_Then_the_supplied_function_is_called()
        {
            var builder = new PartialActionBuilder();
            object updatedValue = null;
            var delegateArguments = new object[] { "a1", 1, 3.0f };
            var updatedArgs = new object[delegateArguments.Length];
            Func<object, string, int, float, bool> deleg = (value, a1, a2, a3) =>
            {
                updatedValue = value;
                updatedArgs[0] = a1;
                updatedArgs[1] = a2;
                updatedArgs[2] = a3;
                return true;
            };

            var partialAction = builder.Build<object>(new CompiledMatchHandlerWithArguments(deleg, delegateArguments));
            partialAction("value");
            Assert.Same("value", updatedValue);
            AssertAreSame(delegateArguments, updatedArgs);
        }

        [Fact]
        public void Given_a_4_arguments_delegate_When_building_and_invoking_Then_the_supplied_function_is_called()
        {
            var builder = new PartialActionBuilder();
            object updatedValue = null;
            var delegateArguments = new object[] { "a1", 1, 3.0f, "a4" };
            var updatedArgs = new object[delegateArguments.Length];
            Func<object, string, int, float, string, bool> deleg = (value, a1, a2, a3, a4) =>
            {
                updatedValue = value;
                updatedArgs[0] = a1;
                updatedArgs[1] = a2;
                updatedArgs[2] = a3;
                updatedArgs[3] = a4;
                return true;
            };

            var partialAction = builder.Build<object>(new CompiledMatchHandlerWithArguments(deleg, delegateArguments));
            partialAction("value");
            Assert.Same("value", updatedValue);
            AssertAreSame(delegateArguments, updatedArgs);
        }

        [Fact]
        public void Given_a_5_arguments_delegate_When_building_and_invoking_Then_the_supplied_function_is_called()
        {
            var builder = new PartialActionBuilder();
            object updatedValue = null;
            var delegateArguments = new object[] { "a1", 1, 3.0f, "a4", 5 };
            var updatedArgs = new object[delegateArguments.Length];
            Func<object, string, int, float, string, int, bool> deleg = (value, a1, a2, a3, a4, a5) =>
            {
                updatedValue = value;
                updatedArgs[0] = a1;
                updatedArgs[1] = a2;
                updatedArgs[2] = a3;
                updatedArgs[3] = a4;
                updatedArgs[4] = a5;
                return true;
            };

            var partialAction = builder.Build<object>(new CompiledMatchHandlerWithArguments(deleg, delegateArguments));
            partialAction("value");
            Assert.Same("value", updatedValue);
            AssertAreSame(delegateArguments, updatedArgs);
        }

        [Fact]
        public void Given_a_6_arguments_delegate_When_building_and_invoking_Then_the_supplied_function_is_called()
        {
            var builder = new PartialActionBuilder();
            object updatedValue = null;
            var delegateArguments = new object[] { "a1", 1, 3.0f, "a4", 5, 6.0f };
            var updatedArgs = new object[delegateArguments.Length];
            Func<object, string, int, float, string, int, float, bool> deleg = (value, a1, a2, a3, a4, a5, a6) =>
            {
                updatedValue = value;
                updatedArgs[0] = a1;
                updatedArgs[1] = a2;
                updatedArgs[2] = a3;
                updatedArgs[3] = a4;
                updatedArgs[4] = a5;
                updatedArgs[5] = a6;
                return true;
            };

            var partialAction = builder.Build<object>(new CompiledMatchHandlerWithArguments(deleg, delegateArguments));
            partialAction("value");
            Assert.Same("value", updatedValue);
            AssertAreSame(delegateArguments, updatedArgs);
        }

        [Fact]
        public void Given_a_7_arguments_delegate_When_building_and_invoking_Then_the_supplied_function_is_called()
        {
            var builder = new PartialActionBuilder();
            object updatedValue = null;
            var delegateArguments = new object[] { "a1", 1, 3.0f, "a4", 5, 6.0f, "a7" };
            var updatedArgs = new object[delegateArguments.Length];
            Func<object, string, int, float, string, int, float, string, bool> deleg = (value, a1, a2, a3, a4, a5, a6, a7) =>
            {
                updatedValue = value;
                updatedArgs[0] = a1;
                updatedArgs[1] = a2;
                updatedArgs[2] = a3;
                updatedArgs[3] = a4;
                updatedArgs[4] = a5;
                updatedArgs[5] = a6;
                updatedArgs[6] = a7;
                return true;
            };

            var partialAction = builder.Build<object>(new CompiledMatchHandlerWithArguments(deleg, delegateArguments));
            partialAction("value");
            Assert.Same("value", updatedValue);
            AssertAreSame(delegateArguments, updatedArgs);
        }

        [Fact]
        public void Given_a_8_arguments_delegate_When_building_and_invoking_Then_the_supplied_function_is_called()
        {
            var builder = new PartialActionBuilder();
            object updatedValue = null;
            var delegateArguments = new object[] { "a1", 1, 3.0f, "a4", 5, 6.0f, "a7", 8 };
            var updatedArgs = new object[delegateArguments.Length];
            Func<object, string, int, float, string, int, float, string, int, bool> deleg = (value, a1, a2, a3, a4, a5, a6, a7, a8) =>
            {
                updatedValue = value;
                updatedArgs[0] = a1;
                updatedArgs[1] = a2;
                updatedArgs[2] = a3;
                updatedArgs[3] = a4;
                updatedArgs[4] = a5;
                updatedArgs[5] = a6;
                updatedArgs[6] = a7;
                updatedArgs[7] = a8;
                return true;
            };

            var partialAction = builder.Build<object>(new CompiledMatchHandlerWithArguments(deleg, delegateArguments));
            partialAction("value");
            Assert.Same("value", updatedValue);
            AssertAreSame(delegateArguments, updatedArgs);
        }

        [Fact]
        public void Given_a_9_arguments_delegate_When_building_and_invoking_Then_the_supplied_function_is_called()
        {
            var builder = new PartialActionBuilder();
            object updatedValue = null;
            var delegateArguments = new object[] { "a1", 1, 3.0f, "a4", 5, 6.0f, "a7", 8, 9.0f };
            var updatedArgs = new object[delegateArguments.Length];
            Func<object, string, int, float, string, int, float, string, int, float, bool> deleg = (value, a1, a2, a3, a4, a5, a6, a7, a8, a9) =>
            {
                updatedValue = value;
                updatedArgs[0] = a1;
                updatedArgs[1] = a2;
                updatedArgs[2] = a3;
                updatedArgs[3] = a4;
                updatedArgs[4] = a5;
                updatedArgs[5] = a6;
                updatedArgs[6] = a7;
                updatedArgs[7] = a8;
                updatedArgs[8] = a9;
                return true;
            };

            var partialAction = builder.Build<object>(new CompiledMatchHandlerWithArguments(deleg, delegateArguments));
            partialAction("value");
            Assert.Same("value", updatedValue);
            AssertAreSame(delegateArguments, updatedArgs);
        }


        [Fact]
        public void Given_a_10_arguments_delegate_When_building_and_invoking_Then_the_supplied_function_is_called()
        {
            var builder = new PartialActionBuilder();
            object updatedValue = null;
            var delegateArguments = new object[] { "a1", 1, 3.0f, "a4", 5, 6.0f, "a7", 8, 9.0f, "a10" };
            var updatedArgs = new object[delegateArguments.Length];
            Func<object, string, int, float, string, int, float, string, int, float, string, bool> deleg = (value, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10) =>
            {
                updatedValue = value;
                updatedArgs[0] = a1;
                updatedArgs[1] = a2;
                updatedArgs[2] = a3;
                updatedArgs[3] = a4;
                updatedArgs[4] = a5;
                updatedArgs[5] = a6;
                updatedArgs[6] = a7;
                updatedArgs[7] = a8;
                updatedArgs[8] = a9;
                updatedArgs[9] = a10;
                return true;
            };

            var partialAction = builder.Build<object>(new CompiledMatchHandlerWithArguments(deleg, delegateArguments));
            partialAction("value");
            Assert.Same("value", updatedValue);
            AssertAreSame(delegateArguments, updatedArgs);
        }

        [Fact]
        public void Given_a_11_arguments_delegate_When_building_and_invoking_Then_the_supplied_function_is_called()
        {
            var builder = new PartialActionBuilder();
            object updatedValue = null;
            var delegateArguments = new object[] { "a1", 1, 3.0f, "a4", 5, 6.0f, "a7", 8, 9.0f, "a10", 11 };
            var updatedArgs = new object[delegateArguments.Length];
            Func<object, string, int, float, string, int, float, string, int, float, string, int, bool> deleg = (value, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11) =>
            {
                updatedValue = value;
                updatedArgs[0] = a1;
                updatedArgs[1] = a2;
                updatedArgs[2] = a3;
                updatedArgs[3] = a4;
                updatedArgs[4] = a5;
                updatedArgs[5] = a6;
                updatedArgs[6] = a7;
                updatedArgs[7] = a8;
                updatedArgs[8] = a9;
                updatedArgs[9] = a10;
                updatedArgs[10] = a11;
                return true;
            };

            var partialAction = builder.Build<object>(new CompiledMatchHandlerWithArguments(deleg, delegateArguments));
            partialAction("value");
            Assert.Same("value", updatedValue);
            AssertAreSame(delegateArguments, updatedArgs);
        }

        [Fact]
        public void Given_a_12_arguments_delegate_When_building_and_invoking_Then_the_supplied_function_is_called()
        {
            var builder = new PartialActionBuilder();
            object updatedValue = null;
            var delegateArguments = new object[] { "a1", 1, 3.0f, "a4", 5, 6.0f, "a7", 8, 9.0f, "a10", 11, 12f };
            var updatedArgs = new object[delegateArguments.Length];
            Func<object, string, int, float, string, int, float, string, int, float, string, int, float, bool> deleg = (value, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12) =>
            {
                updatedValue = value;
                updatedArgs[0] = a1;
                updatedArgs[1] = a2;
                updatedArgs[2] = a3;
                updatedArgs[3] = a4;
                updatedArgs[4] = a5;
                updatedArgs[5] = a6;
                updatedArgs[6] = a7;
                updatedArgs[7] = a8;
                updatedArgs[8] = a9;
                updatedArgs[9] = a10;
                updatedArgs[10] = a11;
                updatedArgs[11] = a12;
                return true;
            };

            var partialAction = builder.Build<object>(new CompiledMatchHandlerWithArguments(deleg, delegateArguments));
            partialAction("value");
            Assert.Same("value", updatedValue);
            AssertAreSame(delegateArguments, updatedArgs);
        }

        [Fact]
        public void Given_a_13_arguments_delegate_When_building_and_invoking_Then_the_supplied_function_is_called()
        {
            var builder = new PartialActionBuilder();
            object updatedValue = null;
            var delegateArguments = new object[] { "a1", 1, 3.0f, "a4", 5, 6.0f, "a7", 8, 9.0f, "a10", 11, 12f, "a13" };
            var updatedArgs = new object[delegateArguments.Length];
            Func<object, string, int, float, string, int, float, string, int, float, string, int, float, string, bool> deleg = (value, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13) =>
            {
                updatedValue = value;
                updatedArgs[0] = a1;
                updatedArgs[1] = a2;
                updatedArgs[2] = a3;
                updatedArgs[3] = a4;
                updatedArgs[4] = a5;
                updatedArgs[5] = a6;
                updatedArgs[6] = a7;
                updatedArgs[7] = a8;
                updatedArgs[8] = a9;
                updatedArgs[9] = a10;
                updatedArgs[10] = a11;
                updatedArgs[11] = a12;
                updatedArgs[12] = a13;
                return true;
            };

            var partialAction = builder.Build<object>(new CompiledMatchHandlerWithArguments(deleg, delegateArguments));
            partialAction("value");
            Assert.Same("value", updatedValue);
            AssertAreSame(delegateArguments, updatedArgs);
        }

        [Fact]
        public void Given_a_14_arguments_delegate_When_building_and_invoking_Then_the_supplied_function_is_called()
        {
            var builder = new PartialActionBuilder();
            object updatedValue = null;
            var delegateArguments = new object[] { "a1", 1, 3.0f, "a4", 5, 6.0f, "a7", 8, 9.0f, "a10", 11, 12f, "a13", 14 };
            var updatedArgs = new object[delegateArguments.Length];
            Func<object, string, int, float, string, int, float, string, int, float, string, int, float, string, int, bool> deleg = (value, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14) =>
            {
                updatedValue = value;
                updatedArgs[0] = a1;
                updatedArgs[1] = a2;
                updatedArgs[2] = a3;
                updatedArgs[3] = a4;
                updatedArgs[4] = a5;
                updatedArgs[5] = a6;
                updatedArgs[6] = a7;
                updatedArgs[7] = a8;
                updatedArgs[8] = a9;
                updatedArgs[9] = a10;
                updatedArgs[10] = a11;
                updatedArgs[11] = a12;
                updatedArgs[12] = a13;
                updatedArgs[13] = a14;
                return true;
            };

            var partialAction = builder.Build<object>(new CompiledMatchHandlerWithArguments(deleg, delegateArguments));
            partialAction("value");
            Assert.Same("value", updatedValue);
            AssertAreSame(delegateArguments, updatedArgs);
        }


        [Fact]
        public void Given_a_15_arguments_delegate_When_building_and_invoking_Then_the_supplied_function_is_called()
        {
            var builder = new PartialActionBuilder();
            object updatedValue = null;
            var delegateArguments = new object[] { "a1", 1, 3.0f, "a4", 5, 6.0f, "a7", 8, 9.0f, "a10", 11, 12f, "a13", 14,15f };
            var updatedArgs = new object[delegateArguments.Length];
            Func<object, string, int, float, string, int, float, string, int, float, string, int, float, string, int, float,bool> deleg = (value, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15) =>
            {
                updatedValue = value;
                updatedArgs[0] = a1;
                updatedArgs[1] = a2;
                updatedArgs[2] = a3;
                updatedArgs[3] = a4;
                updatedArgs[4] = a5;
                updatedArgs[5] = a6;
                updatedArgs[6] = a7;
                updatedArgs[7] = a8;
                updatedArgs[8] = a9;
                updatedArgs[9] = a10;
                updatedArgs[10] = a11;
                updatedArgs[11] = a12;
                updatedArgs[12] = a13;
                updatedArgs[13] = a14;
                updatedArgs[14] = a15;
                return true;
            };

            var partialAction = builder.Build<object>(new CompiledMatchHandlerWithArguments(deleg, delegateArguments));
            partialAction("value");
            Assert.Same("value", updatedValue);
            AssertAreSame(delegateArguments, updatedArgs);
        }


        [Fact]
        public void When_building_with_16_args_Then_it_fails()
        {
            var builder = new PartialActionBuilder();
            var delegateArguments = new object[] { "a1", 1, 3.0f, "a4", 5, 6.0f, "a7", 8, 9.0f, "a10", 11, 12f, "a13", 14, 15f,"a16" };
            Assert.Throws<ArgumentException>(() => ((Action) (() => builder.Build<object>(new CompiledMatchHandlerWithArguments(null, delegateArguments))))());
        }

        private static void AssertAreSame(object[] delegateArguments, object[] updatedArgs)
        {
            for(int i = 0; i < delegateArguments.Length; i++)
            {
                if(delegateArguments[i].GetType().GetTypeInfo().IsValueType)
                    Assert.Equal(delegateArguments[i], updatedArgs[i]);
                else
                    Assert.Same(delegateArguments[i], updatedArgs[i]);
            }
        }
    }
}

