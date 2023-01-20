//-----------------------------------------------------------------------
// <copyright file="WithinTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using FluentAssertions;
using FluentAssertions.Execution;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Sdk;
using static FluentAssertions.FluentActions;

namespace Akka.TestKit.Tests.TestKitBaseTests
{
    public class WithinTests : AkkaSpec
    {
        [Fact]
        public void Within_should_increase_max_timeout_by_the_provided_epsilon_value()
        {
            Within(TimeSpan.FromSeconds(1), () => ExpectNoMsg(), TimeSpan.FromMilliseconds(50));
        }

        [Fact]
        public void Within_should_respect_minimum_time()
        {
            Within(0.3.Seconds(), 1.Seconds(), () => ExpectNoMsg(0.4.Seconds()), "", 0.1.Seconds());
        }
        
        [Fact]
        public async Task WithinAsync_should_respect_minimum_time()
        {
            await WithinAsync(
                0.3.Seconds(),
                1.Seconds(),
                async () => await ExpectNoMsgAsync(0.4.Seconds()), 
                "", 
                0.1.Seconds());
        }
        
        [Fact]
        public void Within_should_throw_if_execution_is_shorter_than_minimum_time()
        {
            Invoking(() =>
            {
                Within(0.5.Seconds(), 1.Seconds(), () => ExpectNoMsg(0.1.Seconds()), null, 0.1.Seconds());
            }).Should().Throw<XunitException>();
        }
        
        [Fact]
        public async Task WithinAsync_should_throw_if_execution_is_shorter_than_minimum_time()
        {
            await Awaiting(async () =>
            {
                await WithinAsync(
                    0.5.Seconds(),
                    1.Seconds(),
                    async () => await ExpectNoMsgAsync(0.1.Seconds()),
                    null,
                    0.1.Seconds());
            }).Should().ThrowAsync<XunitException>();
        }
    }
}
