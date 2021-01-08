//-----------------------------------------------------------------------
// <copyright file="WithinTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Xunit;

namespace Akka.TestKit.Tests.TestKitBaseTests
{
    public class WithinTests : AkkaSpec
    {
        [Fact]
        public void Within_should_increase_max_timeout_by_the_provided_epsilon_value()
        {
            Within(TimeSpan.FromSeconds(1), () => ExpectNoMsg(), TimeSpan.FromMilliseconds(50));
        }
    }
}
