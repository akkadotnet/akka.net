//-----------------------------------------------------------------------
// <copyright file="AwaitAssertTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Sdk;

namespace Akka.TestKit.Tests.Xunit2.TestKitBaseTests
{
    public class AwaitAssertTests : TestKit.Xunit2.TestKit
    {
        public AwaitAssertTests() : base("akka.test.timefactor=2")
        {
        }

        [Fact]
        public async Task AwaitAssert_must_not_throw_any_exception_when_assertion_is_valid()
        {
            await AwaitAssertAsync(() => Assert.Equal("foo", "foo"));
        }

        [Fact]
        public async Task AwaitAssert_must_throw_exception_when_assertion_is_invalid()
        {
            await WithinAsync(TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(1), async () =>
            {
                await Assert.ThrowsAsync<EqualException>(async () =>
                {
                    await AwaitAssertAsync(() => Assert.Equal("foo", "bar"), TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(300));
                });
            });
        }
    }
}

