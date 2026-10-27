//-----------------------------------------------------------------------
// <copyright file="Support.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Persistence.Embedded.Query;
using Akka.Persistence.Journal;
using Akka.Persistence.Query;
using Akka.Serialization;
using Akka.Streams;
using Akka.Streams.Dsl;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace Akka.Persistence.Embedded.Hosting.Tests
{
    public sealed record HostingEvent(string Value);

    public sealed record HostingSnapshot(string[] Values);

    public sealed class HostingSerializer : SerializerWithStringManifest
    {
        public HostingSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        public override int Identifier => 7401;

        public override string Manifest(object o) => o switch
        {
            HostingEvent => "E",
            HostingSnapshot => "S",
            _ => throw new ArgumentException($"Cannot serialize {o.GetType()}")
        };

        public override byte[] ToBinary(object obj) => obj switch
        {
            HostingEvent e => Encoding.UTF8.GetBytes(e.Value),
            HostingSnapshot s => Encoding.UTF8.GetBytes(string.Join('\n', s.Values)),
            _ => throw new ArgumentException($"Cannot serialize {obj.GetType()}")
        };

        public override object FromBinary(byte[] bytes, string manifest) => manifest switch
        {
            "E" => new HostingEvent(Encoding.UTF8.GetString(bytes)),
            "S" => new HostingSnapshot(Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries)),
            _ => throw new ArgumentException($"Unknown manifest {manifest}")
        };
    }

    /// <summary>Tags every event whose value starts with "red-".</summary>
    public sealed class RedTagger : IWriteEventAdapter
    {
        private static readonly string[] Tags = ["red"];

        public string Manifest(object evt) => string.Empty;

        public object ToJournal(object evt)
            => evt is HostingEvent { Value: { } v } && v.StartsWith("red-", StringComparison.Ordinal) ? new Tagged(evt, Tags) : evt;
    }

    public sealed record Persist(string Value);

    public sealed record Snapshot;

    public sealed record GetState;

    public sealed record State(string[] Values, long SnapshotSequenceNr);

    /// <summary>Persists events one at a time, snapshots on request and reports what it recovered.</summary>
    public sealed class HostingActor : UntypedPersistentActor
    {
        private readonly List<string> _values = [];
        private long _snapshotSequenceNr;
        private IActorRef _requester = ActorRefs.Nobody;

        public HostingActor(string persistenceId, string? journalId = null, string? snapshotId = null)
        {
            PersistenceId = persistenceId;
            JournalPluginId = journalId ?? "";
            SnapshotPluginId = snapshotId ?? "";
        }

        public override string PersistenceId { get; }

        protected override void OnRecover(object message)
        {
            switch (message)
            {
                case SnapshotOffer { Snapshot: HostingSnapshot snapshot } offer:
                    _values.Clear();
                    _values.AddRange(snapshot.Values);
                    _snapshotSequenceNr = offer.Metadata.SequenceNr;
                    break;
                case HostingEvent e:
                    _values.Add(e.Value);
                    break;
            }
        }

        protected override void OnCommand(object message)
        {
            switch (message)
            {
                case Persist p:
                    var sender = Sender;
                    Persist(new HostingEvent(p.Value), e =>
                    {
                        _values.Add(e.Value);
                        sender.Tell(_values.Count);
                    });
                    break;
                case Snapshot:
                    _requester = Sender;
                    SaveSnapshot(new HostingSnapshot(_values.ToArray()));
                    break;
                case SaveSnapshotSuccess success:
                    _requester.Tell(success.Metadata.SequenceNr);
                    break;
                case SaveSnapshotFailure failure:
                    _requester.Tell(new Status.Failure(failure.Cause));
                    break;
                case GetState:
                    Sender.Tell(new State(_values.ToArray(), _snapshotSequenceNr));
                    break;
            }
        }
    }

    /// <summary>One temp SQLite file per test class.</summary>
    public sealed class TempDb : IDisposable
    {
        public TempDb()
        {
            var directory = Path.Combine(Path.GetTempPath(), "akka-embedded-hosting-tests");
            Directory.CreateDirectory(directory);
            FilePath = Path.Combine(directory, $"{Guid.NewGuid():N}.db");
        }

        public string FilePath { get; }

        public string ConnectionString => $"Data Source={FilePath}";

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            foreach (var file in new[] { FilePath, FilePath + "-journal" })
            {
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        File.Delete(file);
                        break;
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(100);
                    }
                }
            }
        }
    }

    public static class Scenario
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        /// <summary>Persists, snapshots, recovers and queries (by id, by tag) through whatever the Hosting extension registered.</summary>
        public static async Task RunAsync(ActorSystem system, string persistenceId, string? journalId = null, string? snapshotId = null, string readJournalId = SqliteReadJournal.Identifier)
        {
            var first = system.ActorOf(Props.Create(() => new HostingActor(persistenceId, journalId, snapshotId)));
            (await first.Ask<int>(new Persist("plain-1"), Timeout)).Should().Be(1);
            (await first.Ask<int>(new Persist("red-2"), Timeout)).Should().Be(2);
            (await first.Ask<long>(new Snapshot(), Timeout)).Should().Be(2);
            (await first.Ask<int>(new Persist("red-3"), Timeout)).Should().Be(3);
            await first.GracefulStop(Timeout);

            var second = system.ActorOf(Props.Create(() => new HostingActor(persistenceId, journalId, snapshotId)));
            var state = await second.Ask<State>(new GetState(), Timeout);
            state.Values.Should().Equal("plain-1", "red-2", "red-3");
            state.SnapshotSequenceNr.Should().Be(2, "recovery starts from the snapshot");

            var readJournal = system.ReadJournalFor<SqliteReadJournal>(readJournalId);
            var materializer = system.Materializer();

            var byId = await readJournal.CurrentEventsByPersistenceId(persistenceId, 0, long.MaxValue)
                .RunWith(Sink.Seq<EventEnvelope>(), materializer).WaitAsync(Timeout);
            byId.Select(e => ((HostingEvent)e.Event).Value).Should().Equal("plain-1", "red-2", "red-3");

            // the tags come from the event adapter that was added through the journal builder
            var byTag = await readJournal.CurrentEventsByTag("red", Offset.NoOffset())
                .RunWith(Sink.Seq<EventEnvelope>(), materializer).WaitAsync(Timeout);
            byTag.Where(e => e.PersistenceId == persistenceId).Select(e => e.SequenceNr).Should().Equal(2L, 3L);
            byTag.SelectMany(e => e.Tags).Should().OnlyContain(t => t == "red");
        }
    }
}
