using System;
using Akka.Configuration;
using Akka.Hosting;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using static Akka.Persistence.Hosting.Tests.UnifiedApiTestResources;

namespace Akka.Persistence.Hosting.Tests;

/// <summary>
/// Tests that verify builders expose their options, eliminating the need
/// to pass options explicitly to extension methods like WithConnectivityCheck().
///
/// This resolves the API design issue identified in https://github.com/akkadotnet/Akka.Hosting/issues/690
/// </summary>
public sealed class BuilderOptionsAccessSpec
{
    private readonly ITestOutputHelper _output;

    public BuilderOptionsAccessSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(DisplayName = "Journal builder should expose JournalOptions via Options property")]
    public void JournalBuilderShouldExposeOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new AkkaConfigurationBuilder(services, "test-system");
        var journalOptions = new TestJournalOptions(isDefault: true)
        {
            Identifier = "test-journal"
        };

        AkkaPersistenceJournalBuilder? capturedBuilder = null;

        // Act
        builder.WithJournal(journalOptions, journal =>
        {
            capturedBuilder = journal;
        });

        // Assert
        capturedBuilder.Should().NotBeNull("builder should be passed to callback");
        capturedBuilder!.Options.Should().NotBeNull("Options should be set");
        capturedBuilder.Options!.Should().BeSameAs(journalOptions, "Options should reference the original options instance");
        capturedBuilder.Options!.Identifier.Should().Be("test-journal");

        _output.WriteLine($"✓ Journal builder correctly exposes options with identifier: {capturedBuilder.Options!.Identifier}");
    }

    [Fact(DisplayName = "Snapshot builder should expose SnapshotOptions via Options property")]
    public void SnapshotBuilderShouldExposeOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new AkkaConfigurationBuilder(services, "test-system");
        var snapshotOptions = new TestSnapshotOptions(isDefault: true)
        {
            Identifier = "test-snapshot"
        };

        AkkaPersistenceSnapshotBuilder? capturedBuilder = null;

        // Act
        builder.WithSnapshot(snapshotOptions, snapshot =>
        {
            capturedBuilder = snapshot;
        });

        // Assert
        capturedBuilder.Should().NotBeNull("builder should be passed to callback");
        capturedBuilder!.Options.Should().NotBeNull("Options should be set");
        capturedBuilder.Options!.Should().BeSameAs(snapshotOptions, "Options should reference the original options instance");
        capturedBuilder.Options!.Identifier.Should().Be("test-snapshot");

        _output.WriteLine($"✓ Snapshot builder correctly exposes options with identifier: {capturedBuilder.Options!.Identifier}");
    }

    [Fact(DisplayName = "WithJournalAndSnapshot should expose both journal and snapshot options")]
    public void WithJournalAndSnapshotShouldExposeBothOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new AkkaConfigurationBuilder(services, "test-system");
        var journalOptions = new TestJournalOptions(isDefault: true)
        {
            Identifier = "test-journal"
        };
        var snapshotOptions = new TestSnapshotOptions(isDefault: true)
        {
            Identifier = "test-snapshot"
        };

        AkkaPersistenceJournalBuilder? capturedJournalBuilder = null;
        AkkaPersistenceSnapshotBuilder? capturedSnapshotBuilder = null;

        // Act
        builder.WithJournalAndSnapshot(
            journalOptions,
            snapshotOptions,
            journal => { capturedJournalBuilder = journal; },
            snapshot => { capturedSnapshotBuilder = snapshot; });

        // Assert
        capturedJournalBuilder.Should().NotBeNull("journal builder should be passed to callback");
        capturedJournalBuilder!.Options.Should().NotBeNull("journal Options should be set");
        capturedJournalBuilder.Options.Should().BeSameAs(journalOptions);

        capturedSnapshotBuilder.Should().NotBeNull("snapshot builder should be passed to callback");
        capturedSnapshotBuilder!.Options.Should().NotBeNull("snapshot Options should be set");
        capturedSnapshotBuilder.Options.Should().BeSameAs(snapshotOptions);

        _output.WriteLine($"✓ Both builders correctly expose their options");
    }

    [Fact(DisplayName = "Legacy parameterless constructor should have null Options (backward compatibility)")]
    public void LegacyParameterlessConstructorShouldHaveNullOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        var akkaBuilder = new AkkaConfigurationBuilder(services, "test-system");

        // Act - using the parameterless constructor directly (legacy usage)
        var journalBuilder = new AkkaPersistenceJournalBuilder("legacy-journal", akkaBuilder);
        var snapshotBuilder = new AkkaPersistenceSnapshotBuilder("legacy-snapshot", akkaBuilder);

        // Assert
        journalBuilder.Options.Should().BeNull("parameterless constructor should result in null Options for backward compatibility");
        snapshotBuilder.Options.Should().BeNull("parameterless constructor should result in null Options for backward compatibility");

        _output.WriteLine($"✓ Legacy constructors correctly maintain backward compatibility with null Options");
    }

    [Fact(DisplayName = "WithInMemoryJournal should work without options (backward compatibility)")]
    public void WithInMemoryJournalShouldWorkWithoutOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new AkkaConfigurationBuilder(services, "test-system");

        AkkaPersistenceJournalBuilder? capturedBuilder = null;

        // Act
        builder.WithInMemoryJournal(journal =>
        {
            capturedBuilder = journal;
        });

        // Assert
        capturedBuilder.Should().NotBeNull("builder should be passed to callback");
        capturedBuilder!.Options.Should().BeNull("WithInMemoryJournal doesn't have options, so Options should be null");
        capturedBuilder.JournalId.Should().Be("inmem");

        _output.WriteLine($"✓ WithInMemoryJournal correctly works without options (backward compatibility maintained)");
    }

    /// <summary>
    /// Demonstrates the improved API ergonomics - extension methods can now access options
    /// from the builder without requiring them as explicit parameters.
    /// </summary>
    [Fact(DisplayName = "Extension methods can access connection details from builder options")]
    public void ExtensionMethodsCanAccessConnectionDetailsFromBuilderOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new AkkaConfigurationBuilder(services, "test-system");

        // Simulate a concrete options class with connection details
        var journalOptions = new TestJournalOptionsWithConnectionString(isDefault: true)
        {
            Identifier = "test-journal",
            ConnectionString = "Server=localhost;Database=test;User=sa;Password=pass123"
        };

        string? capturedConnectionString = null;

        // Act
        builder.WithJournal(journalOptions, journal =>
        {
            // Extension method can now access connection string from builder.Options
            // WITHOUT requiring it as an explicit parameter!
            if (journal.Options is TestJournalOptionsWithConnectionString options)
            {
                capturedConnectionString = options.ConnectionString;
            }
        });

        // Assert
        capturedConnectionString.Should().NotBeNull("extension method should be able to access connection string from builder options");
        capturedConnectionString.Should().Be("Server=localhost;Database=test;User=sa;Password=pass123");

        _output.WriteLine($"✓ Extension methods can access connection details from builder without explicit parameters");
        _output.WriteLine($"  Connection string: {capturedConnectionString}");
    }

    /// <summary>
    /// Test helper class simulating a real-world options class with connection string
    /// </summary>
    private sealed class TestJournalOptionsWithConnectionString : JournalOptions
    {
        public TestJournalOptionsWithConnectionString(bool isDefault = true) : base(isDefault)
        {
            Identifier = "test-journal-with-conn";
        }

        public override string Identifier { get; set; }

        protected override Config InternalDefaultConfig =>
            ConfigurationFactory.ParseString("""
                class = "Akka.Persistence.Journal.MemoryJournal, Akka.Persistence"
                plugin-dispatcher = "akka.actor.default-dispatcher"
            """);

        public string? ConnectionString { get; set; }
    }
}
