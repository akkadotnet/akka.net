// -----------------------------------------------------------------------
//  <copyright file="HoconSnapshotSpecs.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Configuration;
using Akka.Hosting;
using Akka.Persistence.Journal;
using Akka.Persistence.Snapshot;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Akka.Persistence.Hosting.Tests;

/// <summary>
/// The HOCON the Hosting builders emit is part of what Akka.Persistence.Hosting guarantees, so a representative
/// set of builder calls has to produce the same configuration it produced before the builders learned to
/// register plugins in code. The expected text was captured from the builders as they were before that change.
/// </summary>
public class HoconSnapshotSpecs
{
    public sealed class EvtA { }
    public sealed class EvtB { }
    public sealed class EvtC { }

    public sealed class AdapterOne : IEventAdapter
    {
        public string Manifest(object evt) => "one";
        public object ToJournal(object evt) => evt;
        public IEventSequence FromJournal(object evt, string manifest) => EventSequence.Single(evt);
    }

    public sealed class AdapterTwo : IWriteEventAdapter
    {
        public string Manifest(object evt) => "two";
        public object ToJournal(object evt) => evt;
    }

    public sealed class AdapterThree : IReadEventAdapter
    {
        public IEventSequence FromJournal(object evt, string manifest) => EventSequence.Single(evt);
    }

    public sealed class SnapshotJournal : MemoryJournal
    {
    }

    public sealed class SnapshotSnapshotStore : MemorySnapshotStore
    {
    }

    public sealed class SnapshotJournalOptions : JournalOptions
    {
        private readonly bool _withFactory;

        public SnapshotJournalOptions(string identifier, bool isDefault, bool withFactory) : base(isDefault)
        {
            Identifier = identifier;
            _withFactory = withFactory;
        }

        protected override PluginActorFactory? CreatePluginActorFactory() => _withFactory ? PluginActorFactory.For(_ => new SnapshotJournal()) : null;

        public override string Identifier { get; set; }

        protected override Config InternalDefaultConfig => ConfigurationFactory.ParseString(
            "class = \"Some.Journal, Some.Assembly\"\nplugin-dispatcher = \"akka.actor.default-dispatcher\"\nconnection-string = none");
    }

    public sealed class SnapshotSnapshotOptions : SnapshotOptions
    {
        private readonly bool _withFactory;

        public SnapshotSnapshotOptions(string identifier, bool isDefault, bool withFactory) : base(isDefault)
        {
            Identifier = identifier;
            _withFactory = withFactory;
        }

        protected override PluginActorFactory? CreatePluginActorFactory() => _withFactory ? PluginActorFactory.For(_ => new SnapshotSnapshotStore()) : null;

        public override string Identifier { get; set; }

        protected override Config InternalDefaultConfig => ConfigurationFactory.ParseString(
            "class = \"Some.SnapshotStore, Some.Assembly\"\nplugin-dispatcher = \"akka.actor.default-dispatcher\"");
    }

    private static string BuildConfig(bool withFactory)
    {
        var builder = new AkkaConfigurationBuilder(new ServiceCollection(), "hocon-snapshot");

        builder
            .WithJournalAndSnapshot(
                new SnapshotJournalOptions("alpha", true, withFactory), new SnapshotSnapshotOptions("alpha", true, withFactory),
                journal => journal
                    .AddEventAdapter<AdapterOne>("one", new[] { typeof(EvtA), typeof(EvtB) })
                    .AddWriteEventAdapter<AdapterTwo>("two", new[] { typeof(EvtB) })
                    .AddReadEventAdapter<AdapterThree>("three", new[] { typeof(EvtC) }),
                snapshot => snapshot.WithHealthCheck())
            .WithJournal(new SnapshotJournalOptions("beta", false, withFactory) { AutoInitialize = true, Serializer = "json" })
            .WithSnapshot(new SnapshotSnapshotOptions("beta", false, withFactory) { AutoInitialize = true })
            .WithInMemoryJournal(journal => journal.AddWriteEventAdapter<AdapterTwo>("mem-two", new[] { typeof(EvtA) }), "mem", false)
            .WithInMemoryJournal()
            .WithInMemorySnapshotStore("mem", false)
            .WithInMemorySnapshotStore()
            .WithClusterShardingJournalMigrationAdapter("akka.persistence.journal.alpha")
            .WithClusterShardingJournalMigrationAdapter(new SnapshotJournalOptions("beta", false, withFactory));
#pragma warning disable CS0618 // the string overload is still part of what Hosting guarantees
        builder.WithJournal("legacy", journal => journal.AddEventAdapter<AdapterOne>("legacy-one", new[] { typeof(EvtC) }));
#pragma warning restore CS0618

        var lines = new List<string>();
        Render(builder.Configuration.Value.Root.GetObject().Unwrapped, 0, lines);
        return string.Join("\n", lines);
    }

    // Config.ToString only shows part of a config that was merged from several pieces, so walk the merged tree
    // instead, keys in ordinal order and list items in the order they were written.
    private static void Render(IDictionary<string, object> node, int depth, List<string> lines)
    {
        var indent = new string(' ', depth * 2);
        foreach (var key in new List<string>(node.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            switch (node[key])
            {
                case IDictionary<string, object> child:
                    lines.Add($"{indent}{key} {{");
                    Render(child, depth + 1, lines);
                    lines.Add($"{indent}}}");
                    break;
                case IEnumerable<object> list:
                    lines.Add($"{indent}{key} = [{string.Join(", ", list)}]");
                    break;
                default:
                    lines.Add($"{indent}{key} = {node[key]}");
                    break;
            }
        }
    }

    [Theory(DisplayName = "The Hosting builders should emit the HOCON they always emitted When a representative set of calls is made")]
    [InlineData(false)]
    [InlineData(true)]
    public void Should_emit_the_HOCON_they_always_emitted_When_a_representative_set_of_calls_is_made(bool withFactory)
    {
        var actual = BuildConfig(withFactory);

        // a plugin that supplies a factory registers it in code and leaves its HOCON as it was
        Normalize(actual).Should().Be(Normalize(Expected));
    }

    private static string Normalize(string text)
        => string.Join("\n", text.ReplaceLineEndings("\n").Split('\n').Select(line => line.TrimEnd()));

    private const string Expected = """
akka {
  persistence {
    journal {
      alpha {
        auto-initialize = off
        class = "Some.Journal, Some.Assembly"
        connection-string = none
        event-adapter-bindings {
          Akka.Cluster.Sharding.ShardCoordinator+IDomainEvent, Akka.Cluster.Sharding = coordinator-migration
          Akka.Persistence.Hosting.Tests.HoconSnapshotSpecs+EvtA, Akka.Persistence.Hosting.Tests = [one]
          Akka.Persistence.Hosting.Tests.HoconSnapshotSpecs+EvtB, Akka.Persistence.Hosting.Tests = [one,two]
          Akka.Persistence.Hosting.Tests.HoconSnapshotSpecs+EvtC, Akka.Persistence.Hosting.Tests = [three]
        }
        event-adapters {
          coordinator-migration = "Akka.Cluster.Sharding.OldCoordinatorStateMigrationEventAdapter, Akka.Cluster.Sharding"
          one = "Akka.Persistence.Hosting.Tests.HoconSnapshotSpecs+AdapterOne, Akka.Persistence.Hosting.Tests"
          three = "Akka.Persistence.Hosting.Tests.HoconSnapshotSpecs+AdapterThree, Akka.Persistence.Hosting.Tests"
          two = "Akka.Persistence.Hosting.Tests.HoconSnapshotSpecs+AdapterTwo, Akka.Persistence.Hosting.Tests"
        }
        plugin-dispatcher = akka.actor.default-dispatcher
        serializer =
      }
      beta {
        auto-initialize = on
        class = "Some.Journal, Some.Assembly"
        connection-string = none
        event-adapter-bindings {
          Akka.Cluster.Sharding.ShardCoordinator+IDomainEvent, Akka.Cluster.Sharding = coordinator-migration
        }
        event-adapters {
          coordinator-migration = "Akka.Cluster.Sharding.OldCoordinatorStateMigrationEventAdapter, Akka.Cluster.Sharding"
        }
        plugin-dispatcher = akka.actor.default-dispatcher
        serializer = json
      }
      inmem {
        class = "Akka.Persistence.Journal.MemoryJournal, Akka.Persistence"
        plugin-dispatcher = akka.actor.default-dispatcher
      }
      legacy {
        event-adapter-bindings {
          Akka.Persistence.Hosting.Tests.HoconSnapshotSpecs+EvtC, Akka.Persistence.Hosting.Tests = [legacy-one]
        }
        event-adapters {
          legacy-one = "Akka.Persistence.Hosting.Tests.HoconSnapshotSpecs+AdapterOne, Akka.Persistence.Hosting.Tests"
        }
      }
      mem {
        class = "Akka.Persistence.Journal.MemoryJournal, Akka.Persistence"
        event-adapter-bindings {
          Akka.Persistence.Hosting.Tests.HoconSnapshotSpecs+EvtA, Akka.Persistence.Hosting.Tests = [mem-two]
        }
        event-adapters {
          mem-two = "Akka.Persistence.Hosting.Tests.HoconSnapshotSpecs+AdapterTwo, Akka.Persistence.Hosting.Tests"
        }
        plugin-dispatcher = akka.actor.default-dispatcher
      }
      plugin = akka.persistence.journal.inmem
    }
    snapshot-store {
      alpha {
        auto-initialize = off
        class = "Some.SnapshotStore, Some.Assembly"
        plugin-dispatcher = akka.actor.default-dispatcher
        serializer =
      }
      beta {
        auto-initialize = on
        class = "Some.SnapshotStore, Some.Assembly"
        plugin-dispatcher = akka.actor.default-dispatcher
        serializer =
      }
      inmem {
        class = "Akka.Persistence.Snapshot.MemorySnapshotStore, Akka.Persistence"
        plugin-dispatcher = akka.actor.default-dispatcher
      }
      mem {
        class = "Akka.Persistence.Snapshot.MemorySnapshotStore, Akka.Persistence"
        plugin-dispatcher = akka.actor.default-dispatcher
      }
      plugin = akka.persistence.snapshot-store.inmem
    }
  }
}
""";
}
