//-----------------------------------------------------------------------
// <copyright file="CanaryTypes.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Text;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Hosting;
using Akka.Persistence.Query.InMemory;
using Akka.Persistence.Journal;
using Akka.Persistence.Snapshot;
using Akka.Serialization;

namespace Akka.Persistence.AOT.App;

/// <summary>The event the canary persists.</summary>
public sealed record CanaryEvent(string Value);

/// <summary>The snapshot the canary saves.</summary>
public sealed record CanarySnapshot(string[] Values);

/// <summary>Asks the canary actor for what it knows.</summary>
public sealed record GetState;

public sealed record SaveNow;

public sealed record CanaryState(string[] Values, long SnapshotSequenceNr);

/// <summary>
/// Wraps every <see cref="CanaryEvent"/> in <see cref="Tagged"/>. Added through
/// <c>AkkaPersistenceJournalBuilder.AddWriteEventAdapter&lt;TAdapter&gt;</c>, which names the type in code.
/// </summary>
public sealed class CanaryTagger : IWriteEventAdapter
{
    private static readonly string[] Tags = ["canary"];

    public string Manifest(object evt) => string.Empty;

    public object ToJournal(object evt) => evt is CanaryEvent ? new Tagged(evt, Tags) : evt;
}

/// <summary>
/// A journal that is not built in. Only the type <see cref="CanaryJournalOptions"/> names lets core
/// build it with <c>Akka.DynamicTypeLoading</c> off, which is the path a third-party journal takes.
/// </summary>
public sealed class CanaryJournal : MemoryJournal
{
    private static int _instances;

    /// <summary>How many times core has built this journal.</summary>
    public static int Instances => Volatile.Read(ref _instances);

    /// <summary>The <c>marker</c> setting of the plugin section the journal was built with.</summary>
    public static string? Marker { get; private set; }

    public CanaryJournal(Config config)
    {
        Marker = config.GetString("marker", null);
        Interlocked.Increment(ref _instances);
    }
}

/// <summary>
/// A snapshot store that is not built in; see <see cref="CanaryJournal"/>.
/// </summary>
public sealed class CanarySnapshotStore : MemorySnapshotStore
{
    private static int _instances;

    /// <summary>How many times core has built this snapshot store.</summary>
    public static int Instances => Volatile.Read(ref _instances);

    public CanarySnapshotStore()
    {
        Interlocked.Increment(ref _instances);
    }
}

/// <summary>
/// The options a plugin author writes: an identifier, the plugin's default config, and the base class that names
/// the journal and its default read journal. Nothing else is needed to run the plugin with
/// <c>Akka.DynamicTypeLoading</c> off.
/// </summary>
public sealed class CanaryJournalOptions : JournalOptions<CanaryJournal, InMemoryReadJournalProvider>
{
    public CanaryJournalOptions() : base(isDefault: true)
    {
    }

    public override string Identifier { get; set; } = "canary";

    protected override Config InternalDefaultConfig => ConfigurationFactory.ParseString("""
        plugin-dispatcher = "akka.actor.default-dispatcher"
        marker = from-default
        """);

    protected override string ReadJournalPluginId => InMemoryReadJournal.Identifier;
}

/// <summary>The snapshot store counterpart of <see cref="CanaryJournalOptions"/>.</summary>
public sealed class CanarySnapshotOptions : SnapshotOptions<CanarySnapshotStore>
{
    public CanarySnapshotOptions() : base(isDefault: true)
    {
    }

    public override string Identifier { get; set; } = "canary";

    protected override Config InternalDefaultConfig => ConfigurationFactory.ParseString("""
        plugin-dispatcher = "akka.actor.default-dispatcher"
        """);
}

/// <summary>
/// Options for a journal that names no type in code, only its class in HOCON, so it fails to start with the
/// switch off.
/// </summary>
public sealed class UnregisteredJournalOptions : JournalOptions
{
    public UnregisteredJournalOptions() : base(isDefault: false)
    {
    }

    public override string Identifier { get; set; } = "unregistered";

    protected override Config InternalDefaultConfig => ConfigurationFactory.ParseString($$"""
        class = "{{typeof(UnregisteredJournal).AssemblyQualifiedName}}"
        plugin-dispatcher = "akka.actor.default-dispatcher"
        """);
}

/// <summary>
/// A journal that no setup registers, so a system that names it must fail at start with the switch off.
/// </summary>
public sealed class UnregisteredJournal : MemoryJournal
{
}

/// <summary>
/// Hand-written, reflection-free serializer for <see cref="CanaryEvent"/> and <see cref="CanarySnapshot"/>.
/// With <c>Akka.DynamicTypeLoading</c> off core registers no fallback serializer, so an AOT application
/// binds one for each of its own event and snapshot types through <see cref="SerializationSetup"/>.
/// </summary>
public sealed class CanarySerializer : SerializerWithStringManifest
{
    private const string EventManifest = "E";
    private const string SnapshotManifest = "S";

    public CanarySerializer(ExtendedActorSystem system) : base(system)
    {
    }

    // set here rather than read from akka.actor.serialization-identifiers, which is keyed by type name
    public override int Identifier => 91001;

    public override string Manifest(object o) => o switch
    {
        CanaryEvent => EventManifest,
        CanarySnapshot => SnapshotManifest,
        _ => throw new ArgumentException($"{nameof(CanarySerializer)} cannot serialize [{o.GetType()}]")
    };

    public override byte[] ToBinary(object obj) => obj switch
    {
        CanaryEvent e => Encoding.UTF8.GetBytes(e.Value),
        CanarySnapshot s => Encoding.UTF8.GetBytes(string.Join('\n', s.Values)),
        _ => throw new ArgumentException($"{nameof(CanarySerializer)} cannot serialize [{obj.GetType()}]")
    };

    public override object FromBinary(byte[] bytes, string manifest) => manifest switch
    {
        EventManifest => new CanaryEvent(Encoding.UTF8.GetString(bytes)),
        SnapshotManifest => new CanarySnapshot(Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries)),
        _ => throw new System.Runtime.Serialization.SerializationException($"Unknown manifest [{manifest}]")
    };
}

/// <summary>
/// Persists <see cref="CanaryEvent"/>s one at a time, so a command that arrives while a write is in
/// flight is stashed, and saves a snapshot on request.
/// </summary>
public sealed class CanaryPersistentActor : UntypedPersistentActor
{
    private readonly List<string> _values = [];
    private long _snapshotSequenceNr;
    private IActorRef _snapshotRequester = ActorRefs.Nobody;

    // uses the default journal and snapshot store from akka.persistence.journal.plugin / snapshot-store.plugin
    public CanaryPersistentActor(string persistenceId)
    {
        PersistenceId = persistenceId;
    }

    public CanaryPersistentActor(string persistenceId, string journalPluginId, string snapshotPluginId) : this(persistenceId)
    {
        JournalPluginId = journalPluginId;
        SnapshotPluginId = snapshotPluginId;
    }

    public override string PersistenceId { get; }

    protected override void OnRecover(object message)
    {
        switch (message)
        {
            case SnapshotOffer { Snapshot: CanarySnapshot snapshot } offer:
                _values.Clear();
                _values.AddRange(snapshot.Values);
                _snapshotSequenceNr = offer.Metadata.SequenceNr;
                break;
            case CanaryEvent e:
                _values.Add(e.Value);
                break;
            // the in-memory journal stores the Tagged wrapper the adapter added, and read adapters pick by the stored type
            case Tagged { Payload: CanaryEvent tagged }:
                _values.Add(tagged.Value);
                break;
        }
    }

    protected override void OnCommand(object message)
    {
        switch (message)
        {
            case string value:
                var requester = Sender;
                Persist(new CanaryEvent(value), e =>
                {
                    _values.Add(e.Value);
                    requester.Tell(_values.Count);
                });
                break;
            case SaveNow:
                _snapshotRequester = Sender;
                SaveSnapshot(new CanarySnapshot(_values.ToArray()));
                break;
            case SaveSnapshotSuccess success:
                _snapshotRequester.Tell(success.Metadata.SequenceNr);
                break;
            case SaveSnapshotFailure failure:
                _snapshotRequester.Tell(new Status.Failure(failure.Cause));
                break;
            case GetState:
                Sender.Tell(new CanaryState(_values.ToArray(), _snapshotSequenceNr));
                break;
        }
    }
}
