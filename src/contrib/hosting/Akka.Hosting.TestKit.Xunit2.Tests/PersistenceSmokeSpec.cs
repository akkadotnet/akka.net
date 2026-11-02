// -----------------------------------------------------------------------
//  <copyright file="PersistenceSmokeSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Hosting.TestKit.Tests;

public sealed class PersistenceSmokeSpec : PersistenceTestKit
{
    public PersistenceSmokeSpec(ITestOutputHelper output) : base(nameof(PersistenceSmokeSpec), output)
    {
    }

    [Fact]
    public void should_initialize_persistence_testkit_services()
    {
        Journal.Should().NotBeNull();
        JournalActorRef.Should().NotBeNull();
        Snapshots.Should().NotBeNull();
        SnapshotsActorRef.Should().NotBeNull();
    }
}
