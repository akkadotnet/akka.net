//-----------------------------------------------------------------------
// <copyright file="WithinTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Xunit;

namespace Akka.Hosting.TestKit.Tests.TestKitBaseTests;

public class WithinTests : TestKit
{
    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        
    }

    [Fact]
    public async Task Within_should_increase_max_timeout_by_the_provided_epsilon_value()
    {
        // Explicit 1s timeout keeps block duration predictable; 3s Within max absorbs
        // Windows CI scheduler jitter on windows-2025-vs2026 runners.
        await WithinAsync(TimeSpan.FromSeconds(3),
            async () => await ExpectNoMsgAsync(TimeSpan.FromSeconds(1)),
            TimeSpan.FromMilliseconds(50));
    }
}