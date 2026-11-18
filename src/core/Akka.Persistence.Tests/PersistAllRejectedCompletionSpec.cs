//-----------------------------------------------------------------------
// <copyright file="PersistAllRejectedCompletionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Persistence.Journal;
using FluentAssertions;
using Xunit;

namespace Akka.Persistence.Tests
{
    /// <summary>
    /// The completion callback of PersistAll/PersistAllAsync runs only after all events of the batch
    /// have been persisted. It must not run when the journal rejects the batch or fails to write it.
    /// </summary>
    public class PersistAllRejectedCompletionSpec : PersistenceSpec
    {
        public PersistAllRejectedCompletionSpec(ITestOutputHelper output)
            : base(Configuration("PersistAllRejectedCompletionSpec", serialization: "off",
                extraConfig: @"akka.persistence.journal.inmem.class = ""Akka.Persistence.Tests.PersistAllRejectedCompletionSpec+RejectingJournal, Akka.Persistence.Tests"""),
                output)
        {
        }

        public enum Shape
        {
            PersistAllSyncHandlerSyncCompletion,
            PersistAllSyncHandlerAsyncCompletion,
            PersistAllAsyncHandlerSyncCompletion,
            PersistAllAsyncHandlerAsyncCompletion,
            PersistAllAsyncSyncHandlerSyncCompletion,
            PersistAllAsyncSyncHandlerAsyncCompletion,
            PersistAllAsyncAsyncHandlerSyncCompletion,
            PersistAllAsyncAsyncHandlerAsyncCompletion,
        }

        private sealed record Evt(string Data);

        private sealed class SimulatedJournalException : Exception
        {
            public SimulatedJournalException(string message) : base(message)
            {
            }
        }

        /// <summary>
        /// Rejects every <see cref="AtomicWrite"/> that contains a "reject" event and fails the whole
        /// write when any event contains "fail". Everything else is stored as usual.
        /// </summary>
        internal sealed class RejectingJournal : MemoryJournal
        {
            protected override async Task<IImmutableList<Exception>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages, CancellationToken cancellationToken)
            {
                var writes = messages.ToList();
                if (writes.Any(w => Contains(w, "fail")))
                    throw new SimulatedJournalException("simulated journal failure");

                var results = ImmutableList.CreateBuilder<Exception>();
                foreach (var write in writes)
                {
                    if (Contains(write, "reject"))
                    {
                        results.Add(new SimulatedJournalException("simulated rejection"));
                    }
                    else
                    {
                        await base.WriteMessagesAsync(new[] { write }, cancellationToken);
                        results.Add(null);
                    }
                }

                return results.ToImmutable();
            }

            private static bool Contains(AtomicWrite write, string marker) =>
                ((IEnumerable<IPersistentRepresentation>)write.Payload).Any(p => ((Evt)p.Payload).Data.Contains(marker));
        }

        private sealed class BatchActor : ReceivePersistentActor
        {
            private readonly IActorRef _probe;
            private readonly List<string> _handled = new();

            public override string PersistenceId { get; }

            public BatchActor(string persistenceId, Shape shape, IActorRef probe)
            {
                PersistenceId = persistenceId;
                _probe = probe;

                Recover<Evt>(_ => { });

                Command<string>(data =>
                {
                    if (data == "state")
                    {
                        _probe.Tell("state:" + string.Join(",", _handled));
                        return;
                    }

                    var events = new[] { new Evt(data + "-1"), new Evt(data + "-2") };

                    void Handle(Evt e)
                    {
                        _handled.Add(e.Data);
                        _probe.Tell("handled:" + e.Data);
                    }

                    async Task HandleAsync(Evt e)
                    {
                        await Task.Delay(10);
                        Handle(e);
                    }

                    void Complete() => _probe.Tell("completed:" + data);

                    async Task CompleteAsync()
                    {
                        await Task.Delay(10);
                        Complete();
                    }

                    switch (shape)
                    {
                        case Shape.PersistAllSyncHandlerSyncCompletion:
                            PersistAll(events, Handle, Complete);
                            break;
                        case Shape.PersistAllSyncHandlerAsyncCompletion:
                            PersistAll(events, Handle, CompleteAsync);
                            break;
                        case Shape.PersistAllAsyncHandlerSyncCompletion:
                            PersistAll(events, HandleAsync, Complete);
                            break;
                        case Shape.PersistAllAsyncHandlerAsyncCompletion:
                            PersistAll(events, HandleAsync, CompleteAsync);
                            break;
                        case Shape.PersistAllAsyncSyncHandlerSyncCompletion:
                            PersistAllAsync(events, Handle, Complete);
                            break;
                        case Shape.PersistAllAsyncSyncHandlerAsyncCompletion:
                            PersistAllAsync(events, Handle, CompleteAsync);
                            break;
                        case Shape.PersistAllAsyncAsyncHandlerSyncCompletion:
                            PersistAllAsync(events, HandleAsync, Complete);
                            break;
                        case Shape.PersistAllAsyncAsyncHandlerAsyncCompletion:
                            PersistAllAsync(events, HandleAsync, CompleteAsync);
                            break;
                    }
                });
            }

            protected override void OnPersistRejected(Exception cause, object @event, long sequenceNr)
            {
                _probe.Tell("rejected:" + ((Evt)@event).Data);
            }

            protected override void OnPersistFailure(Exception cause, object @event, long sequenceNr)
            {
                _probe.Tell("failed:" + ((Evt)@event).Data);
            }
        }

        [Theory(DisplayName = "Should_NotRunCompletion_When_JournalRejectsBatch")]
        [InlineData(Shape.PersistAllSyncHandlerSyncCompletion)]
        [InlineData(Shape.PersistAllSyncHandlerAsyncCompletion)]
        [InlineData(Shape.PersistAllAsyncHandlerSyncCompletion)]
        [InlineData(Shape.PersistAllAsyncHandlerAsyncCompletion)]
        [InlineData(Shape.PersistAllAsyncSyncHandlerSyncCompletion)]
        [InlineData(Shape.PersistAllAsyncSyncHandlerAsyncCompletion)]
        [InlineData(Shape.PersistAllAsyncAsyncHandlerSyncCompletion)]
        [InlineData(Shape.PersistAllAsyncAsyncHandlerAsyncCompletion)]
        public async Task Should_NotRunCompletion_When_JournalRejectsBatch(Shape shape)
        {
            var actor = Sys.ActorOf(Props.Create(() => new BatchActor(Name, shape, TestActor)));

            // "ok" is sent while the rejected batch is in flight: the stashing shapes stash it
            actor.Tell("reject");
            actor.Tell("ok");

            var received = new List<string>();
            for (var i = 0; i < 5; i++)
                received.Add(await ExpectMsgAsync<string>());

            received.Should().Equal(
                "rejected:reject-1",
                "rejected:reject-2",
                "handled:ok-1",
                "handled:ok-2",
                "completed:ok");

            // the actor continues with the next command exactly once
            actor.Tell("state");
            await ExpectMsgAsync("state:ok-1,ok-2");
        }

        [Theory(DisplayName = "Should_NotRunCompletion_When_JournalFailsBatch")]
        [InlineData(Shape.PersistAllSyncHandlerSyncCompletion)]
        [InlineData(Shape.PersistAllSyncHandlerAsyncCompletion)]
        [InlineData(Shape.PersistAllAsyncHandlerSyncCompletion)]
        [InlineData(Shape.PersistAllAsyncHandlerAsyncCompletion)]
        [InlineData(Shape.PersistAllAsyncSyncHandlerSyncCompletion)]
        [InlineData(Shape.PersistAllAsyncSyncHandlerAsyncCompletion)]
        [InlineData(Shape.PersistAllAsyncAsyncHandlerSyncCompletion)]
        [InlineData(Shape.PersistAllAsyncAsyncHandlerAsyncCompletion)]
        public async Task Should_NotRunCompletion_When_JournalFailsBatch(Shape shape)
        {
            var actor = Sys.ActorOf(Props.Create(() => new BatchActor(Name, shape, TestActor)));
            await WatchAsync(actor);

            actor.Tell("fail");

            // the actor stops after the first failure; a completion would arrive before Terminated
            await ExpectMsgAsync("failed:fail-1");
            await ExpectTerminatedAsync(actor);
        }
    }
}
