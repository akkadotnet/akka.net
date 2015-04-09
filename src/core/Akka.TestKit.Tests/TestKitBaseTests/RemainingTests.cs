//-----------------------------------------------------------------------
// <copyright file="RemainingTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Xunit;

namespace Akka.Testkit.Tests.TestKitBaseTests
{
    public class RemainingTests : Akka.TestKit.Xunit.TestKit
    {
        [Fact]
        public void Throw_if_remaining_is_called_outside_Within()
        {
            Assert.Throws<InvalidOperationException>(() => Remaining);
        }
    }
}

