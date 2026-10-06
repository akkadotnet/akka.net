//-----------------------------------------------------------------------
// <copyright file="CanaryTypes.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Text;
using Akka.Actor;
using Akka.Persistence.Journal;
using Akka.Serialization;

namespace Akka.Persistence.Embedded.AOT.App;

/// <summary>The event the canary persists.</summary>
public sealed record CanaryEvent(string Value);

/// <summary>The snapshot the canary saves.</summary>
public sealed record CanarySnapshot(string[] Values);

/// <summary>Persists one event, tagged "red" when <paramref name="Red"/> is set. Replies with the number of events held.</summary>
public sealed record PersistCmd(string Value, bool Red);

public sealed record SaveNow;

public sealed record DeleteTo(long SequenceNr);

public sealed record GetState;

public sealed record CanaryState(string[] Values, long SnapshotSequenceNr, long LastSequenceNr);

/// <summary>
/// Tags every event whose value starts with "adapted-". It is added with the typed <c>AddWriteEventAdapter&lt;T&gt;</c>, which
/// is what lets core build it with <c>Akka.DynamicTypeLoading</c> off.
/// </summary>
public sealed class CanaryTagger : IWriteEventAdapter
{
    private static readonly string[] Tags = ["adapted"];

    public string Manifest(object evt) => string.Empty;

    public object ToJournal(object evt) => evt is CanaryEvent { Value: { } value } && value.StartsWith("adapted-", StringComparison.Ordinal)
        ? new Tagged(evt, Tags)
        : evt;
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
    public override int Identifier => 91002;

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
/// Persists <see cref="CanaryEvent"/>s one at a time, saves snapshots, deletes events and reports
/// what it recovered. <paramref name="useSnapshots"/> = false recovers from events only.
/// </summary>
public sealed class CanaryPersistentActor : UntypedPersistentActor
{
    private static readonly string[] RedTag = ["red"];

    private readonly bool _useSnapshots;
    private readonly List<string> _values = [];
    private long _snapshotSequenceNr;
    private IActorRef _requester = ActorRefs.Nobody;

    public CanaryPersistentActor(string persistenceId, bool useSnapshots = true)
    {
        PersistenceId = persistenceId;
        _useSnapshots = useSnapshots;
    }

    public override string PersistenceId { get; }

    public override Recovery Recovery => _useSnapshots ? Recovery.Default : new Recovery(SnapshotSelectionCriteria.None);

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
        }
    }

    protected override void OnCommand(object message)
    {
        switch (message)
        {
            case PersistCmd cmd:
                var requester = Sender;
                var evt = new CanaryEvent(cmd.Value);
                if (cmd.Red)
                    Persist(new Tagged(evt, RedTag), _ => OnPersisted(evt, requester));
                else
                    Persist(evt, _ => OnPersisted(evt, requester));
                break;
            case SaveNow:
                _requester = Sender;
                SaveSnapshot(new CanarySnapshot(_values.ToArray()));
                break;
            case SaveSnapshotSuccess success:
                _requester.Tell(success.Metadata.SequenceNr);
                break;
            case SaveSnapshotFailure failure:
                _requester.Tell(new Status.Failure(failure.Cause));
                break;
            case DeleteTo delete:
                _requester = Sender;
                DeleteMessages(delete.SequenceNr);
                break;
            case DeleteMessagesSuccess success:
                _requester.Tell(success.ToSequenceNr);
                break;
            case DeleteMessagesFailure failure:
                _requester.Tell(new Status.Failure(failure.Cause));
                break;
            case GetState:
                Sender.Tell(new CanaryState(_values.ToArray(), _snapshotSequenceNr, LastSequenceNr));
                break;
        }
    }

    private void OnPersisted(CanaryEvent evt, IActorRef requester)
    {
        _values.Add(evt.Value);
        requester.Tell(_values.Count);
    }
}
