//-----------------------------------------------------------------------
// <copyright file="RemainingTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Xunit;

namespace Akka.Hosting.TestKit.Tests.TestKitBaseTests;

public class RemainingTests : TestKit
{
    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        
    }

    [Fact]
    public void Throw_if_remaining_is_called_outside_Within()
    {
        Assert.Throws<InvalidOperationException>(() => Remaining);
    }
}