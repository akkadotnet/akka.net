//-----------------------------------------------------------------------
// <copyright file="ExpressionExtensionsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Linq.Expressions;
using Akka.Actor;
using Akka.Util.Reflection;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Util
{
    /// <summary>
    /// The compiled-lambda fallback in <see cref="ExpressionExtensions.GetArguments"/> - the branch this PR
    /// moves off the non-generic <see cref="Expression.Lambda(Expression,ParameterExpression[])"/> overload.
    /// The primary path is already exercised by every other <c>Props.Create(() =&gt; new T(args))</c> call in
    /// the suite, so only the fallback is specced here.
    /// </summary>
    public class ExpressionExtensionsSpec
    {
        private sealed class FlakyArgumentActor : ActorBase
        {
            public FlakyArgumentActor(string text)
            {
                Text = text;
            }

            public string Text { get; }

            protected override bool Receive(object message) => false;
        }

        /// <summary>
        /// A property that throws the first time it is read and succeeds afterwards. The first read is the
        /// one the parser makes on its happy path, which is what pushes it onto the compiled-lambda fallback.
        /// </summary>
        private sealed class FlakyArgumentSource
        {
            public int Reads { get; private set; }

            public string FirstReadThrows
            {
                get
                {
                    Reads++;
                    if (Reads == 1)
                        throw new InvalidOperationException("the first read always fails");

                    return "recovered";
                }
            }
        }

        private static string AlwaysThrows() => throw new InvalidOperationException("this one never works");

        [Fact(DisplayName = "GetArguments should fall back to a compiled lambda when the first read of an argument throws")]
        public void Should_use_the_compiled_lambda_fallback_When_the_first_read_of_an_argument_throws()
        {
            var source = new FlakyArgumentSource();

            Expression<Func<FlakyArgumentActor>> factory =
                () => new FlakyArgumentActor(source.FirstReadThrows);

            var args = ((NewExpression)factory.Body).GetArguments();

            args.Should().Equal("recovered");
            source.Reads.Should().Be(2, "the parser's own read throws, and the fallback lambda reads it again");
        }

        [Fact(DisplayName = "GetArguments should report the original failure when even the fallback cannot read the argument")]
        public void Should_throw_ArgumentException_When_the_argument_cannot_be_read_at_all()
        {
            Expression<Func<FlakyArgumentActor>> factory =
                () => new FlakyArgumentActor(AlwaysThrows());

            var exception = Assert.Throws<ArgumentException>(() => ((NewExpression)factory.Body).GetArguments());

            // invoking the compiled Func<object> directly means the real failure is the inner exception,
            // instead of the TargetInvocationException that DynamicInvoke wrapped it in
            exception.InnerException.Should().BeOfType<InvalidOperationException>();
            exception.InnerException!.Message.Should().Be("this one never works");
        }
    }
}
