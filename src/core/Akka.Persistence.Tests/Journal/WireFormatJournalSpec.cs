//-----------------------------------------------------------------------
// <copyright file="WireFormatJournalSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Journal;
using Akka.Serialization;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Persistence.Tests.Journal
{
    /// <summary>
    /// <see cref="WireFormatJournal"/> stores bytes, not objects: events recover as copies built from what a serializer
    /// wrote, a serializer that fails rejects the write, and the journal says which serializer wrote what.
    /// </summary>
    public sealed class WireFormatJournalSpec : AkkaSpec
    {
        public interface IWireEvent
        {
        }

        public sealed record Placed(string OrderId, int Quantity) : IWireEvent;

        public sealed record Poisoned(string OrderId) : IWireEvent;

        public sealed record OrderState(string OrderId, int Total);

        public sealed record GetState
        {
            public static readonly GetState Instance = new();
        }

        public sealed record TakeSnapshot
        {
            public static readonly TakeSnapshot Instance = new();
        }

        public sealed record Rejected(object Event, string Reason);

        /// <summary>A serializer that writes events as text, with its own id, so a stored event shows who wrote it.</summary>
        public sealed class TextEventSerializer : SerializerWithStringManifest
        {
            public const int SerializerId = 9_201;

            public TextEventSerializer(ExtendedActorSystem system) : base(system)
            {
            }

            public override int Identifier => SerializerId;

            public override string Manifest(object o) => o switch
            {
                Placed => "placed",
                Poisoned => "poisoned",
                _ => throw new ArgumentException(o.GetType().Name)
            };

            public override byte[] ToBinary(object obj) => obj switch
            {
                Placed p => System.Text.Encoding.UTF8.GetBytes($"{p.OrderId}|{p.Quantity}"),
                Poisoned => throw new InvalidOperationException("cannot serialize this event"),
                _ => throw new ArgumentException(obj.GetType().Name)
            };

            public override object FromBinary(byte[] bytes, string manifest)
            {
                var parts = System.Text.Encoding.UTF8.GetString(bytes).Split('|');
                return new Placed(parts[0], int.Parse(parts[1]));
            }
        }

        private sealed class OrderActor : ReceivePersistentActor
        {
            private OrderState _state;
            private readonly IActorRef _probe;

            public OrderActor(string persistenceId, IActorRef probe)
            {
                PersistenceId = persistenceId;
                _probe = probe;
                _state = new OrderState(persistenceId, 0);

                Command<IWireEvent>(cmd => Persist(cmd, evt =>
                {
                    if (evt is Placed placed)
                        _state = _state with { Total = _state.Total + placed.Quantity };
                    _probe.Tell(evt);
                }));
                Command<TakeSnapshot>(_ => SaveSnapshot(_state));
                Command<SaveSnapshotSuccess>(msg => _probe.Tell(msg));
                Command<GetState>(_ => _probe.Tell(_state));

                Recover<Placed>(evt => _state = _state with { Total = _state.Total + evt.Quantity });
                Recover<SnapshotOffer>(offer => _state = (OrderState)offer.Snapshot);
            }

            public override string PersistenceId { get; }

            protected override void OnPersistRejected(Exception cause, object @event, long sequenceNr)
                => _probe.Tell(new Rejected(@event, cause.Message));
        }

        private readonly string _snapshotDirectory = Path.Combine(Path.GetTempPath(), "akka-wire-journal-" + Guid.NewGuid().ToString("N"));

        public WireFormatJournalSpec(ITestOutputHelper output) : base(output)
        {
        }

        private ActorSystem NewSystem(string name) => ActorSystem.Create(name, WireFormatJournal.Config
            .WithFallback(ConfigurationFactory.ParseString($$"""
                akka.persistence.snapshot-store.plugin = "akka.persistence.snapshot-store.local"
                akka.persistence.snapshot-store.local.dir = "{{_snapshotDirectory.Replace("\\", "/")}}"
                akka.actor {
                    serializers.text-event = "{{typeof(TextEventSerializer).AssemblyQualifiedName}}"
                    serialization-bindings."{{typeof(IWireEvent).AssemblyQualifiedName}}" = text-event
                }
                """))
            .WithFallback(AkkaSpecConfig));

        [Fact(DisplayName = "Should_RecoverEventsFromBytes_When_ActorRestartsOnTheWireFormatJournal")]
        public async Task Should_RecoverEventsFromBytes_When_ActorRestartsOnTheWireFormatJournal()
        {
            var system = NewSystem("wire-journal-events");
            try
            {
                var probe = CreateTestProbe(system);
                var first = system.ActorOf(Props.Create(() => new OrderActor("order-1", probe.Ref)));
                first.Tell(new Placed("order-1", 5));
                await probe.ExpectMsgAsync<Placed>(TimeSpan.FromSeconds(5));
                first.Tell(new Placed("order-1", 3));
                await probe.ExpectMsgAsync<Placed>(TimeSpan.FromSeconds(5));
                probe.Watch(first);
                first.Tell(PoisonPill.Instance);
                await probe.ExpectTerminatedAsync(first, TimeSpan.FromSeconds(5));

                var second = system.ActorOf(Props.Create(() => new OrderActor("order-1", probe.Ref)));
                second.Tell(GetState.Instance);

                (await probe.ExpectMsgAsync<OrderState>(TimeSpan.FromSeconds(5))).Should().Be(new OrderState("order-1", 8));
            }
            finally
            {
                await system.Terminate();
            }
        }

        [Fact(DisplayName = "Should_StoreSerializerIdManifestAndBytes_When_EventIsPersisted")]
        public async Task Should_StoreSerializerIdManifestAndBytes_When_EventIsPersisted()
        {
            var system = NewSystem("wire-journal-stored");
            try
            {
                var probe = CreateTestProbe(system);
                var actor = system.ActorOf(Props.Create(() => new OrderActor("order-2", probe.Ref)));
                actor.Tell(new Placed("order-2", 4));
                await probe.ExpectMsgAsync<Placed>(TimeSpan.FromSeconds(5));

                var stored = (await WireFormatJournal.GetStoredAsync(system, "order-2")).Single();

                stored.SequenceNr.Should().Be(1);
                stored.SerializerId.Should().Be(TextEventSerializer.SerializerId);
                stored.Manifest.Should().Be("placed");
                System.Text.Encoding.UTF8.GetString(stored.Bytes).Should().Be("order-2|4");
                stored.PayloadType.Should().Be(typeof(Placed).FullName);
            }
            finally
            {
                await system.Terminate();
            }
        }

        [Fact(DisplayName = "Should_RejectTheWrite_When_TheSerializerFails")]
        public async Task Should_RejectTheWrite_When_TheSerializerFails()
        {
            var system = NewSystem("wire-journal-rejected");
            try
            {
                var probe = CreateTestProbe(system);
                var actor = system.ActorOf(Props.Create(() => new OrderActor("order-3", probe.Ref)));

                actor.Tell(new Poisoned("order-3"));

                var rejected = await probe.ExpectMsgAsync<Rejected>(TimeSpan.FromSeconds(5));
                rejected.Event.Should().Be(new Poisoned("order-3"));
                rejected.Reason.Should().Contain("cannot serialize this event");
                (await WireFormatJournal.GetStoredAsync(system, "order-3")).Should().BeEmpty("a rejected write is not stored");
            }
            finally
            {
                await system.Terminate();
            }
        }

        [Fact(DisplayName = "Should_RecoverFromSnapshotAndEvents_When_SnapshotStoreIsTheLocalOne")]
        public async Task Should_RecoverFromSnapshotAndEvents_When_SnapshotStoreIsTheLocalOne()
        {
            var system = NewSystem("wire-journal-snapshot");
            try
            {
                var probe = CreateTestProbe(system);
                var first = system.ActorOf(Props.Create(() => new OrderActor("order-4", probe.Ref)));
                first.Tell(new Placed("order-4", 7));
                await probe.ExpectMsgAsync<Placed>(TimeSpan.FromSeconds(5));
                first.Tell(TakeSnapshot.Instance);
                await probe.ExpectMsgAsync<SaveSnapshotSuccess>(TimeSpan.FromSeconds(5));
                first.Tell(new Placed("order-4", 2));
                await probe.ExpectMsgAsync<Placed>(TimeSpan.FromSeconds(5));
                probe.Watch(first);
                first.Tell(PoisonPill.Instance);
                await probe.ExpectTerminatedAsync(first, TimeSpan.FromSeconds(5));

                var second = system.ActorOf(Props.Create(() => new OrderActor("order-4", probe.Ref)));
                second.Tell(GetState.Instance);

                (await probe.ExpectMsgAsync<OrderState>(TimeSpan.FromSeconds(5))).Should().Be(new OrderState("order-4", 9));
            }
            finally
            {
                await system.Terminate();
            }
        }

        [Fact(DisplayName = "Should_HideDeletedEvents_When_ReplayingAfterDeleteMessagesTo")]
        public async Task Should_HideDeletedEvents_When_ReplayingAfterDeleteMessagesTo()
        {
            var system = NewSystem("wire-journal-delete");
            try
            {
                var probe = CreateTestProbe(system);
                var journal = Persistence.Instance.Apply(system).JournalFor(null);
                var actor = system.ActorOf(Props.Create(() => new OrderActor("order-5", probe.Ref)));
                actor.Tell(new Placed("order-5", 1));
                await probe.ExpectMsgAsync<Placed>(TimeSpan.FromSeconds(5));
                actor.Tell(new Placed("order-5", 2));
                await probe.ExpectMsgAsync<Placed>(TimeSpan.FromSeconds(5));

                journal.Tell(new DeleteMessagesTo("order-5", 1, probe.Ref));
                await probe.ExpectMsgAsync<DeleteMessagesSuccess>(TimeSpan.FromSeconds(5));

                var stored = await WireFormatJournal.GetStoredAsync(system, "order-5");
                stored.Select(e => (e.SequenceNr, e.IsDeleted)).Should().Equal((1L, true), (2L, false));
            }
            finally
            {
                await system.Terminate();
            }
        }

        protected override void AfterAll()
        {
            base.AfterAll();
            if (Directory.Exists(_snapshotDirectory))
                Directory.Delete(_snapshotDirectory, recursive: true);
        }
    }
}
