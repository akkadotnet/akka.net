//-----------------------------------------------------------------------
// <copyright file="AwaitAssertTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Xunit.Sdk;
using static FluentAssertions.FluentActions;

namespace Akka.TestKit.Tests.TestKitBaseTests
{
    public class AwaitAssertTests : TestKit.Xunit2.TestKit
    {
        public AwaitAssertTests() : base("akka.test.timefactor=2")
        {
        }

        [Fact]
        public async Task AwaitAssertAsync_must_not_throw_any_exception_when_assertion_is_valid()
        {
            await AwaitAssertAsync(() => Assert.Equal("foo", "foo"));
        }

        [Fact]
        public async Task AwaitAssertAsync_with_async_delegate_must_not_throw_any_exception_when_assertion_is_valid()
        {
            await AwaitAssertAsync(() =>
            {
                Assert.Equal("foo", "foo");
                return Task.CompletedTask;
            });
        }

        [Fact]
        public async Task AwaitAssertAsync_must_throw_exception_when_assertion_is_invalid()
        {
            await WithinAsync(TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(1), async () =>
            {
                await Awaiting(async () =>
                        await AwaitAssertAsync(() => Assert.Equal("foo", "bar"), TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(300)))
                    .Should().ThrowAsync<EqualException>();
            });
        }
        
        [Fact]
        public async Task AwaitAssertAsync_with_async_delegate_must_throw_exception_when_assertion_is_invalid()
        {
            await WithinAsync(TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(1), async () =>
            {
                await Awaiting(async () => await AwaitAssertAsync(() =>
                    {
                        Assert.Equal("foo", "bar");
                        return Task.CompletedTask;
                    }, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(300)))
                    .Should().ThrowAsync<EqualException>();
            });
        }
    }
}

