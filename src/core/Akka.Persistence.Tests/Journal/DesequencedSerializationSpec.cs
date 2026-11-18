//-----------------------------------------------------------------------
// <copyright file="DesequencedSerializationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Persistence.Tests.Journal
{
    /// <summary>
    /// Regression tests for https://github.com/akkadotnet/akka.net/issues/8910.
    ///
    /// <see cref="AsyncWriteJournal"/> hands every write reply to its internal resequencer wrapped in
    /// <c>AsyncWriteJournal.Desequenced</c>. That wrapper is node-local, but it used to be subject to
    /// <c>akka.actor.serialize-messages</c> verification, which round-tripped the whole reply - including
    /// the event payload - through the <c>System.Object</c> JSON fallback serializer. The persist callback
    /// then received a deserialized copy of the event instead of the instance that was persisted.
    /// </summary>
    public class DesequencedSerializationSpec : PersistenceSpec
    {
        public DesequencedSerializationSpec(ITestOutputHelper output)
            : base(Configuration("DesequencedSerializationSpec", serialization: "on"), output)
        {
        }

        private sealed class ProbeEvent
        {
            public ProbeEvent(string data) => Data = data;

            public string Data { get; }
        }

        private sealed class PersistEvent
        {
            public static readonly PersistEvent Instance = new();

            private PersistEvent() { }
        }

        private sealed class PersistAsyncEvent
        {
            public static readonly PersistAsyncEvent Instance = new();

            private PersistAsyncEvent() { }
        }

        /// <summary>
        /// Persists a single event and reports whether the persist callback received the very same
        /// payload instance that was handed to <c>Persist</c>.
        /// </summary>
        private sealed class ProbeActor : ReceivePersistentActor
        {
            private readonly IActorRef _probe;

            public ProbeActor(string persistenceId, IActorRef probe)
            {
                _probe = probe;
                PersistenceId = persistenceId;

                Command<PersistEvent>(_ =>
                {
                    var evt = new ProbeEvent("persist");
                    Persist(evt, e => _probe.Tell(Describe(evt, e)));
                });

                Command<PersistAsyncEvent>(_ =>
                {
                    var evt = new ProbeEvent("persist-async");
                    PersistAsync(evt, e => _probe.Tell(Describe(evt, e)));
                });
            }

            public override string PersistenceId { get; }

            private static string Describe(ProbeEvent original, ProbeEvent received)
            {
                if (ReferenceEquals(original, received))
                    return "same-instance";

                return received is null
                    ? "null-payload"
                    : $"different-instance:{received.Data ?? "<null-data>"}";
            }
        }

        [Fact]
        public void Desequenced_should_not_serialize_the_persist_callback_payload()
        {
            var probe = CreateTestProbe();
            var actor = Sys.ActorOf(Props.Create(() => new ProbeActor(Name, probe.Ref)));

            actor.Tell(PersistEvent.Instance);

            probe.ExpectMsg("same-instance");
        }

        [Fact]
        public void Desequenced_should_not_serialize_the_persist_async_callback_payload()
        {
            var probe = CreateTestProbe();
            var actor = Sys.ActorOf(Props.Create(() => new ProbeActor(Name, probe.Ref)));

            actor.Tell(PersistAsyncEvent.Instance);

            probe.ExpectMsg("same-instance");
        }
    }
}
