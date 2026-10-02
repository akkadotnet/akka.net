// -----------------------------------------------------------------------
//  <copyright file="PersistenceSetupHostingSpecs.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Hosting;
using Akka.Persistence.Journal;
using Akka.Persistence.Snapshot;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Akka.Persistence.Hosting.Tests;

public class PersistenceSetupHostingSpecs : Akka.Hosting.TestKit.TestKit
{
    private const string JournalPath = "akka.persistence.journal.hosted";
    private const string SnapshotPath = "akka.persistence.snapshot-store.hosted";

    public sealed class HostedJournal : MemoryJournal
    {
    }

    public sealed class HostedSnapshotStore : MemorySnapshotStore
    {
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        // two calls, as two plugin packages would make them: the second builds on the first
        builder
            .WithPersistenceSetup(setup => setup.WithJournal(JournalPath, _ => new HostedJournal()))
            .WithPersistenceSetup(setup => setup.WithSnapshotStore(SnapshotPath, _ => new HostedSnapshotStore()));
    }

    [Fact(DisplayName = "WithPersistenceSetup should start the plugins of every call When the ActorSystem starts")]
    public void Should_start_the_plugins_of_every_call_When_the_ActorSystem_starts()
    {
        var persistence = Persistence.Instance.Apply(Sys);

        var journal = persistence.JournalFor(JournalPath);
        var snapshotStore = persistence.SnapshotStoreFor(SnapshotPath);

        journal.Should().NotBeNull();
        snapshotStore.Should().NotBeNull();
        ((ActorRefWithCell)journal).Underlying.Props.Type.Should().Be(typeof(HostedJournal));
        ((ActorRefWithCell)snapshotStore).Underlying.Props.Type.Should().Be(typeof(HostedSnapshotStore));
    }

    [Fact(DisplayName = "WithPersistenceSetup should keep one PersistenceSetup holding every registration When it is called twice")]
    public void Should_keep_one_PersistenceSetup_holding_every_registration_When_it_is_called_twice()
    {
        var builder = new AkkaConfigurationBuilder(new ServiceCollection(), "setup-merge");

        builder
            .WithPersistenceSetup(setup => setup.WithJournal(JournalPath, _ => new HostedJournal()))
            .WithPersistenceSetup(setup => setup.WithSnapshotStore(SnapshotPath, _ => new HostedSnapshotStore()));

        var setups = builder.Setups.OfType<PersistenceSetup>().ToList();

        setups.Should().ContainSingle("an ActorSystemSetup keeps one PersistenceSetup, so the calls must merge");
        setups[0].CreatePlugins(null!).Select(d => d.PluginId).Should().BeEquivalentTo(new[] { JournalPath, SnapshotPath });
    }

    [Fact(DisplayName = "WithPersistenceSetup should reject a null argument When called")]
    public void Should_reject_a_null_argument_When_called()
    {
        var builder = new AkkaConfigurationBuilder(new ServiceCollection(), "setup-null");

        Assert.Throws<ArgumentNullException>(() => builder.WithPersistenceSetup(null!));
        Assert.Throws<ArgumentNullException>(() => ((AkkaConfigurationBuilder)null!).WithPersistenceSetup(s => s));
    }
}
