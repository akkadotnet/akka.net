using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Configuration;
using Akka.Hosting;
using Akka.Persistence.Journal;
using Akka.Util;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Akka.Persistence.Hosting.Tests;

/// <summary>
/// Shared test resources for unified API specs
/// </summary>
public static class UnifiedApiTestResources
{
    // Mock journal options for testing
    public sealed class TestJournalOptions : JournalOptions
    {
        public TestJournalOptions(bool isDefault = true) : base(isDefault)
        {
            Identifier = "test-journal";
        }

        public override string Identifier { get; set; }

        protected override Config InternalDefaultConfig =>
            ConfigurationFactory.ParseString("""

                                                             class = "Akka.Persistence.Journal.MemoryJournal, Akka.Persistence"
                                                             plugin-dispatcher = "akka.actor.default-dispatcher"

                                             """);
    }

    // Mock snapshot options for testing
    public sealed class TestSnapshotOptions : SnapshotOptions
    {
        public TestSnapshotOptions(bool isDefault = true) : base(isDefault)
        {
            Identifier = "test-snapshot";
        }

        public override string Identifier { get; set; }

        protected override Config InternalDefaultConfig =>
            ConfigurationFactory.ParseString(@"
                class = ""Akka.Persistence.Snapshot.MemorySnapshotStore, Akka.Persistence""
                plugin-dispatcher = ""akka.actor.default-dispatcher""
            ");
    }

    // Test event adapters
    public sealed class TestEvent
    {
    }

    public sealed class TestWriteAdapter : IWriteEventAdapter
    {
        public string Manifest(object evt) => string.Empty;
        public object ToJournal(object evt) => evt;
    }

    public sealed class TestReadAdapter : IReadEventAdapter
    {
        public IEventSequence FromJournal(object evt, string manifest)
            => new SingleEventSequence(evt);
    }
}

/// <summary>
/// Test WithJournal(JournalOptions) without builder
/// </summary>
public sealed class JournalOptionsOnlySpec : Akka.Hosting.TestKit.TestKit
{
    public JournalOptionsOnlySpec(ITestOutputHelper output) : base(output: output)
    {
    }

    protected override void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        base.ConfigureServices(context, services);
        services.AddHealthChecks();
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        var options = new UnifiedApiTestResources.TestJournalOptions { IsDefaultPlugin = true };
        builder.WithJournal(options);
    }

    [Fact]
    public void WithJournal_should_configure_journal_options_only()
    {
        var config = Sys.Settings.Config;
        config.GetString("akka.persistence.journal.plugin")
            .Should().Be("akka.persistence.journal.test-journal");
        // The plugin should be configured even if class property isn't set in the minimal config
        config.HasPath("akka.persistence.journal.test-journal")
            .Should().BeTrue();
    }
}

/// <summary>
/// Test WithJournal(JournalOptions, builder) with event adapters
/// </summary>
public sealed class JournalWithAdaptersSpec : Akka.Hosting.TestKit.TestKit
{
    public JournalWithAdaptersSpec(ITestOutputHelper output) : base(output: output)
    {
    }

    protected override void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        base.ConfigureServices(context, services);
        services.AddHealthChecks();
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        var options = new UnifiedApiTestResources.TestJournalOptions { IsDefaultPlugin = true };
        builder.WithJournal(options, journal => journal
            .AddWriteEventAdapter<UnifiedApiTestResources.TestWriteAdapter>("test-adapter",
                [typeof(UnifiedApiTestResources.TestEvent)])
            .AddReadEventAdapter<UnifiedApiTestResources.TestReadAdapter>("test-reader",
                [typeof(UnifiedApiTestResources.TestEvent)]));
    }

    [Fact]
    public void WithJournal_with_builder_should_configure_options_and_adapters()
    {
        var config = Sys.Settings.Config;

        // assert - journal is configured (checking that at least the path exists)
        config.HasPath("akka.persistence.journal.test-journal")
            .Should().BeTrue();

        // assert - adapters are configured
        var journalConfig = config.GetConfig("akka.persistence.journal.test-journal");
        journalConfig.GetString("event-adapters.test-adapter")
            .Should().Be(typeof(UnifiedApiTestResources.TestWriteAdapter).TypeQualifiedName());
        journalConfig.GetString("event-adapters.test-reader")
            .Should().Be(typeof(UnifiedApiTestResources.TestReadAdapter).TypeQualifiedName());

        var bindings = journalConfig.GetStringList(
            $"event-adapter-bindings.\"{typeof(UnifiedApiTestResources.TestEvent).TypeQualifiedName()}\"");
        bindings.Should().BeEquivalentTo("test-adapter", "test-reader");
    }
}

/// <summary>
/// Test WithSnapshot(SnapshotOptions) without builder
/// </summary>
public sealed class SnapshotOptionsOnlySpec : Akka.Hosting.TestKit.TestKit
{
    public SnapshotOptionsOnlySpec(ITestOutputHelper output) : base(output: output)
    {
    }

    protected override void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        base.ConfigureServices(context, services);
        services.AddHealthChecks();
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        var options = new UnifiedApiTestResources.TestSnapshotOptions { IsDefaultPlugin = true };
        builder.WithSnapshot(options);
    }

    [Fact]
    public void WithSnapshot_should_configure_snapshot_options_only()
    {
        var config = Sys.Settings.Config;
        config.GetString("akka.persistence.snapshot-store.plugin")
            .Should().Be("akka.persistence.snapshot-store.test-snapshot");
        config.HasPath("akka.persistence.snapshot-store.test-snapshot")
            .Should().BeTrue();
    }
}

/// <summary>
/// Test WithJournalAndSnapshot without builders
/// </summary>
public sealed class JournalAndSnapshotWithoutBuildersSpec : Akka.Hosting.TestKit.TestKit
{
    public JournalAndSnapshotWithoutBuildersSpec(ITestOutputHelper output) : base(output: output)
    {
    }

    protected override void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        base.ConfigureServices(context, services);
        services.AddHealthChecks();
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        var journalOptions = new UnifiedApiTestResources.TestJournalOptions { IsDefaultPlugin = true };
        var snapshotOptions = new UnifiedApiTestResources.TestSnapshotOptions { IsDefaultPlugin = true };
        builder.WithJournalAndSnapshot(journalOptions, snapshotOptions);
    }

    [Fact]
    public void WithJournalAndSnapshot_should_configure_both_without_builders()
    {
        var config = Sys.Settings.Config;
        config.GetString("akka.persistence.journal.plugin")
            .Should().Be("akka.persistence.journal.test-journal");
        config.GetString("akka.persistence.snapshot-store.plugin")
            .Should().Be("akka.persistence.snapshot-store.test-snapshot");
    }
}

/// <summary>
/// Test WithJournalAndSnapshot with builder actions
/// </summary>
public sealed class JournalAndSnapshotWithBuildersSpec : Akka.Hosting.TestKit.TestKit
{
    public JournalAndSnapshotWithBuildersSpec(ITestOutputHelper output) : base(output: output)
    {
    }

    protected override void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        base.ConfigureServices(context, services);
        services.AddHealthChecks();
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        var journalOptions = new UnifiedApiTestResources.TestJournalOptions { IsDefaultPlugin = true };
        var snapshotOptions = new UnifiedApiTestResources.TestSnapshotOptions { IsDefaultPlugin = true };

        builder.WithJournalAndSnapshot(
            journalOptions,
            snapshotOptions,
            journal => journal
                .AddWriteEventAdapter<UnifiedApiTestResources.TestWriteAdapter>("adapter",
                    [typeof(UnifiedApiTestResources.TestEvent)])
                .WithHealthCheck(),
            snapshot => snapshot
                .WithHealthCheck());
    }

    [Fact]
    public async Task WithJournalAndSnapshot_with_builders_should_configure_everything()
    {
        var config = Sys.Settings.Config;

        // assert - both plugins configured
        config.HasPath("akka.persistence.journal.test-journal")
            .Should().BeTrue();
        config.HasPath("akka.persistence.snapshot-store.test-snapshot")
            .Should().BeTrue();

        // assert - adapters configured
        var journalConfig = config.GetConfig("akka.persistence.journal.test-journal");
        journalConfig.GetString("event-adapters.adapter")
            .Should().Be(typeof(UnifiedApiTestResources.TestWriteAdapter).TypeQualifiedName());

        // assert - health checks are registered and return real results
        var healthCheckService = Host.Services.GetRequiredService<HealthCheckService>();
        var result = await healthCheckService.CheckHealthAsync();

        result.Status.Should().Be(HealthStatus.Healthy,
            "both journal and snapshot health checks should be healthy");

        // Verify both health checks are present
        var journalCheck = result.Entries.FirstOrDefault(e => e.Key.Contains("test-journal"));
        journalCheck.Should().NotBeNull("journal health check should be registered");
        journalCheck.Value.Status.Should().Be(HealthStatus.Healthy,
            "journal health check should return healthy status");

        var snapshotCheck = result.Entries.FirstOrDefault(e => e.Key.Contains("test-snapshot"));
        snapshotCheck.Should().NotBeNull("snapshot health check should be registered");
        snapshotCheck.Value.Status.Should().Be(HealthStatus.Healthy,
            "snapshot health check should return healthy status");
    }
}

/// <summary>
/// Regression test for https://github.com/akkadotnet/Akka.Hosting/issues/666
/// Ensures journal health checks are registered even without event adapters
/// </summary>
public sealed class JournalHealthCheckWithoutAdaptersSpec : Akka.Hosting.TestKit.TestKit
{
    public JournalHealthCheckWithoutAdaptersSpec(ITestOutputHelper output) : base(output: output)
    {
    }

    protected override void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        base.ConfigureServices(context, services);
        services.AddHealthChecks();
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        var journalOptions = new UnifiedApiTestResources.TestJournalOptions { IsDefaultPlugin = true };

        // Configure journal with health check but WITHOUT any event adapters
        // This is the regression case from issue #666
        builder.WithJournal(
            journalOptions,
            journal => journal.WithHealthCheck());
    }

    [Fact]
    public async Task Journal_health_check_should_be_registered_without_event_adapters()
    {
        // assert - journal plugin configured
        var config = Sys.Settings.Config;
        config.HasPath("akka.persistence.journal.test-journal")
            .Should().BeTrue();

        // assert - health check is registered even without event adapters
        var healthCheckService = Host.Services.GetRequiredService<HealthCheckService>();
        var result = await healthCheckService.CheckHealthAsync();

        result.Status.Should().Be(HealthStatus.Healthy,
            "journal health check should be healthy");

        // Verify journal health check is present
        var journalCheck = result.Entries.FirstOrDefault(e => e.Key.Contains("test-journal"));
        journalCheck.Should().NotBeNull("journal health check should be registered even without event adapters");
        journalCheck.Value.Status.Should().Be(HealthStatus.Healthy,
            "journal health check should return healthy status");
    }
}

/// <summary>
/// Test null builder actions work correctly
/// </summary>
public sealed class NullBuilderActionsSpec : Akka.Hosting.TestKit.TestKit
{
    public NullBuilderActionsSpec(ITestOutputHelper output) : base(output: output)
    {
    }

    protected override void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        base.ConfigureServices(context, services);
        services.AddHealthChecks();
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        var journalOptions = new UnifiedApiTestResources.TestJournalOptions { IsDefaultPlugin = true };
        var snapshotOptions = new UnifiedApiTestResources.TestSnapshotOptions { IsDefaultPlugin = false };

        // Test that passing null for builder actions works
        builder.WithJournal(journalOptions, configureBuilder: null);
        builder.WithSnapshot(snapshotOptions, configureBuilder: null);
    }

    [Fact]
    public void Null_builder_actions_should_work()
    {
        var config = Sys.Settings.Config;
        config.GetString("akka.persistence.journal.plugin")
            .Should().Be("akka.persistence.journal.test-journal");
        config.HasPath("akka.persistence.snapshot-store.test-snapshot")
            .Should().BeTrue();
    }
}