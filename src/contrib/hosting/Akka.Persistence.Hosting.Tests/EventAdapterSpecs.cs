using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Hosting;
using Akka.Persistence.Journal;
using Akka.Util;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Akka.Persistence.Hosting.Tests;

public class EventAdapterSpecs: Akka.Hosting.TestKit.TestKit
{
    public static async Task<IHost> StartHost(Action<IServiceCollection> testSetup)
    {
        var host = new HostBuilder()
            .ConfigureServices(testSetup).Build();

        await host.StartAsync();
        return host;
    }

    // Mock SQL Server journal options for testing event adapters
    private sealed class MockSqlServerJournalOptions : JournalOptions
    {
        public MockSqlServerJournalOptions() : base(isDefault: false)
        {
            Identifier = "sql-server";
        }

        public override string Identifier { get; set; }

        protected override Config InternalDefaultConfig =>
            ConfigurationFactory.ParseString(@"
                class = ""Akka.Persistence.Journal.MemoryJournal, Akka.Persistence""
                plugin-dispatcher = ""akka.actor.default-dispatcher""
            ");
    }

    public sealed class Event1{ }
    public sealed class Event2{ }

    public sealed class EventMapper1 : IWriteEventAdapter
    {
        public string Manifest(object evt)
        {
            return string.Empty;
        }

        public object ToJournal(object evt)
        {
            return evt;
        }
    }

    public sealed class Tagger : IWriteEventAdapter
    {
        public string Manifest(object evt)
        {
            return string.Empty;
        }

        public object ToJournal(object evt)
        {
            if (evt is Tagged t)
                return t;
            return new Tagged(evt, new[] { "foo" });
        }
    }

    public sealed class ReadAdapter : IReadEventAdapter
    {
        public IEventSequence FromJournal(object evt, string manifest)
        {
            return new SingleEventSequence(evt);
        }
    }

    public sealed class ComboAdapter : IEventAdapter
    {
        public string Manifest(object evt)
        {
            return string.Empty;
        }

        public object ToJournal(object evt)
        {
            return evt;
        }

        public IEventSequence FromJournal(object evt, string manifest)
        {
            return new SingleEventSequence(evt);
        }
    }
    
    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        // Using the new unified API: WithJournal(options, builder)
        var journalOptions = new MockSqlServerJournalOptions();
        builder.WithJournal(journalOptions, journalBuilder =>
        {
            journalBuilder.AddWriteEventAdapter<EventMapper1>("mapper1", new Type[] { typeof(Event1) });
            journalBuilder.AddReadEventAdapter<ReadAdapter>("reader1", new Type[] { typeof(Event1) });
            journalBuilder.AddEventAdapter<ComboAdapter>("combo", boundTypes: new Type[] { typeof(Event2) });
            journalBuilder.AddWriteEventAdapter<Tagger>("tagger",
                boundTypes: new Type[] { typeof(Event1), typeof(Event2) });
        });
    }
    
    [Fact]
    public void Should_use_correct_EventAdapter_bindings()
    {
        // act
        var config = Sys.Settings.Config;
        var sqlPersistenceJournal = config.GetConfig("akka.persistence.journal.sql-server");
        
        // assert
        sqlPersistenceJournal.GetStringList($"event-adapter-bindings.\"{typeof(Event1).TypeQualifiedName()}\"").Should()
            .BeEquivalentTo("mapper1", "reader1", "tagger");
        sqlPersistenceJournal.GetStringList($"event-adapter-bindings.\"{typeof(Event2).TypeQualifiedName()}\"").Should()
            .BeEquivalentTo("combo", "tagger");
        
        sqlPersistenceJournal.GetString("event-adapters.mapper1").Should().Be(typeof(EventMapper1).TypeQualifiedName());
        sqlPersistenceJournal.GetString("event-adapters.reader1").Should().Be(typeof(ReadAdapter).TypeQualifiedName());
        sqlPersistenceJournal.GetString("event-adapters.combo").Should().Be(typeof(ComboAdapter).TypeQualifiedName());
        sqlPersistenceJournal.GetString("event-adapters.tagger").Should().Be(typeof(Tagger).TypeQualifiedName());
    }
}

/// <summary>
/// Regression test for https://github.com/akkadotnet/Akka.Hosting/issues/665
/// Verifies that the deprecated Adapters property is ignored and does not configure event adapters
/// </summary>
public class DeprecatedAdaptersPropertySpec : Akka.Hosting.TestKit.TestKit
{
    private sealed class TestJournalOptions : JournalOptions
    {
        public TestJournalOptions() : base(isDefault: true)
        {
            Identifier = "test-journal";
        }

        public override string Identifier { get; set; }

        protected override Config InternalDefaultConfig =>
            ConfigurationFactory.ParseString(@"
                class = ""Akka.Persistence.Journal.MemoryJournal, Akka.Persistence""
                plugin-dispatcher = ""akka.actor.default-dispatcher""
            ");
    }

    public sealed class DeprecatedAdapter : IWriteEventAdapter
    {
        public string Manifest(object evt) => string.Empty;
        public object ToJournal(object evt) => evt;
    }

    public sealed class CallbackAdapter : IWriteEventAdapter
    {
        public string Manifest(object evt) => string.Empty;
        public object ToJournal(object evt) => evt;
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        var journalOptions = new TestJournalOptions();

#pragma warning disable 618, 619
        // Attempt to use the deprecated Adapters property
        journalOptions.Adapters = new AkkaPersistenceJournalBuilder("test-journal", builder);
        journalOptions.Adapters.AddWriteEventAdapter<DeprecatedAdapter>("deprecated-adapter",
            new[] { typeof(string) });
#pragma warning restore 618, 619

        // Use the callback pattern (the correct way)
        builder.WithJournal(journalOptions, journal =>
            journal.AddWriteEventAdapter<CallbackAdapter>("callback-adapter",
                new[] { typeof(int) }));
    }

    [Fact]
    public void Deprecated_Adapters_property_should_be_ignored()
    {
        var config = Sys.Settings.Config;
        var journalConfig = config.GetConfig("akka.persistence.journal.test-journal");

        // The deprecated adapter should NOT be registered
        journalConfig.HasPath("event-adapters.deprecated-adapter").Should().BeFalse(
            "adapters configured via the deprecated Adapters property should be ignored");

        // The callback adapter SHOULD be registered
        journalConfig.HasPath("event-adapters.callback-adapter").Should().BeTrue(
            "adapters configured via the callback pattern should work");
        journalConfig.GetString("event-adapters.callback-adapter")
            .Should().Be(typeof(CallbackAdapter).TypeQualifiedName());

        // Verify bindings
        journalConfig.HasPath($"event-adapter-bindings.\"{typeof(string).TypeQualifiedName()}\"")
            .Should().BeFalse("deprecated adapter bindings should not exist");
        journalConfig.GetStringList($"event-adapter-bindings.\"{typeof(int).TypeQualifiedName()}\"")
            .Should().BeEquivalentTo(new[] { "callback-adapter" }, "callback adapter bindings should exist");
    }
}