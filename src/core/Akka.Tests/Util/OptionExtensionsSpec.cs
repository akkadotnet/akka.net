//-----------------------------------------------------------------------
// <copyright file="OptionExtensionsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Util;
using Akka.Util.Extensions;
using Xunit;

namespace Akka.Tests.Util
{
    public class OptionExtensionsSpec
    {
        [Fact]
        public void Match_with_result_should_invoke_some_when_option_has_value()
        {
            var option = Option<int>.Create(42);

            var result = option.Match(
                some: value => value.ToString(),
                none: () => "none");

            Assert.Equal("42", result);
        }

        [Fact]
        public void Match_with_result_should_invoke_none_when_option_is_empty()
        {
            var option = Option<int>.None;

            var result = option.Match(
                some: value => value.ToString(),
                none: () => "none");

            Assert.Equal("none", result);
        }

        [Fact]
        public void Match_with_actions_should_invoke_some_when_option_has_value()
        {
            var option = Option<int>.Create(42);
            var someInvoked = false;
            var noneInvoked = false;

            option.Match(
                some: _ => someInvoked = true,
                none: () => noneInvoked = true);

            Assert.True(someInvoked);
            Assert.False(noneInvoked);
        }

        [Fact]
        public void Match_with_actions_should_invoke_none_when_option_is_empty()
        {
            var option = Option<int>.None;
            var someInvoked = false;
            var noneInvoked = false;

            option.Match(
                some: _ => someInvoked = true,
                none: () => noneInvoked = true);

            Assert.False(someInvoked);
            Assert.True(noneInvoked);
        }
    }
}
