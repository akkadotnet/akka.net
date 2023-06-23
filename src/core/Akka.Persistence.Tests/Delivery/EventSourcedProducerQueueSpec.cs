// //-----------------------------------------------------------------------
// // <copyright file="EventSourcedProducerQueueSpec.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Delivery;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Akka.Delivery.DurableProducerQueue;

namespace Akka.Persistence.Tests.Delivery;

public sealed class EventSourcedProducerQueueSpec : AkkaSpec
{
    private static readonly Config Config = @"
        akka.reliable-delivery.consumer-controller.flow-control-window = 20
        akka.persistence.journal.plugin = ""akka.persistence.journal.inmem""
        akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.inmem""
        akka.persistence.journal.inmem.test-serialization = on # NOT IMPLEMENTED
        akka.persistence.publish-plugin-commands = on
    ";

    public EventSourcedProducerQueueSpec(ITestOutputHelper output) : base(Config, output)
    {
        JournalOperationsProbe = CreateTestProbe();

        // TODO: this doesn't really work and isn't implemented consistently anywhere in Akka.Persistence
        // As of 5-4-2023 only DeleteMessages and RecoverMessages commands are published to the EventStream
        Sys.EventStream.Subscribe(JournalOperationsProbe, typeof(IPersistenceMessage));
        StateProbe = CreateTestProbe();
    }

    private static readonly AtomicCounter _pidCounter = new(0);
    private string NextPersistenceId() => $"p-{_pidCounter.IncrementAndGet()}";
    public TestProbe JournalOperationsProbe { get; private set; }
    public TestProbe StateProbe { get; private set; }

    [Fact]
    public async Task EventSourcedDurableProducerQueue_must_persist_MessageSent()
    {
        var pid = NextPersistenceId();
        var ackProbe = CreateTestProbe();
        var queue = Sys.ActorOf(EventSourcedProducerQueue.Create<string>(pid, Sys));
        var timestamp = DateTime.UtcNow.Ticks;

        var msg1 = new MessageSent<string>(1, "a", true, NoQualifier, timestamp);
        queue.Tell(new StoreMessageSent<string>(msg1, ackProbe));
        await ackProbe.ExpectMsgAsync(new StoreMessageSentAck(1));

        var msg2 = new MessageSent<string>(2, "b", true, NoQualifier, timestamp);
        queue.Tell(new StoreMessageSent<string>(msg2, ackProbe));
        await ackProbe.ExpectMsgAsync(new StoreMessageSentAck(2));

        queue.Tell(new LoadState(StateProbe));
        var expectedState = new State<string>(CurrentSeqNr: 3, HighestConfirmedSeqNr: 0,
            ConfirmedSeqNr: ImmutableDictionary<string, (long, long)>.Empty,
            Unconfirmed: ImmutableList.Create(msg1, msg2));
        await StateProbe.ExpectMsgAsync(expectedState);

        // replay and recover
        await queue.GracefulStop(RemainingOrDefault);
        var queue2 = Sys.ActorOf(EventSourcedProducerQueue.Create<string>(pid, Sys));
        queue2.Tell(new LoadState(StateProbe));
        await StateProbe.ExpectMsgAsync(expectedState);
    }

    [Fact]
    public async Task EventSourcedDurableProducerQueue_must_persist_MessageSent_if_lowerSeqNr_than_alreadyPersisted()
    {
        var pid = NextPersistenceId();
        var ackProbe = CreateTestProbe();
        var queue = Sys.ActorOf(EventSourcedProducerQueue.Create<string>(pid, Sys));
        var timestamp = DateTime.UtcNow.Ticks;

        var msg1 = new MessageSent<string>(1, "a", true, NoQualifier, timestamp);
        queue.Tell(new StoreMessageSent<string>(msg1, ackProbe));
        await ackProbe.ExpectMsgAsync(new StoreMessageSentAck(1));

        var msg2 = new MessageSent<string>(2, "b", true, NoQualifier, timestamp);
        queue.Tell(new StoreMessageSent<string>(msg2, ackProbe));
        await ackProbe.ExpectMsgAsync(new StoreMessageSentAck(2));

        // duplicate msg-2
        queue.Tell(new StoreMessageSent<string>(msg2, ackProbe));
        await ackProbe.ExpectMsgAsync(new StoreMessageSentAck(2));
        // TODO: if message events were working correctly, should get nothing from the journal here

        // duplicate msg-1 - should be ignored
        queue.Tell(new StoreMessageSent<string>(msg1, ackProbe));
        await ackProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public async Task EventSourcedDurableProducerQueue_must_persist_Confirmed()
    {
        var pid = NextPersistenceId();
        var ackProbe = CreateTestProbe();
        var queue = Sys.ActorOf(EventSourcedProducerQueue.Create<string>(pid, Sys));
        var timestamp = DateTime.UtcNow.Ticks;

        var msg1 = new MessageSent<string>(1, "a", true, NoQualifier, timestamp);
        queue.Tell(new StoreMessageSent<string>(msg1, ackProbe));
        await ackProbe.ExpectMsgAsync(new StoreMessageSentAck(1));

        var msg2 = new MessageSent<string>(2, "b", true, NoQualifier, timestamp);
        queue.Tell(new StoreMessageSent<string>(msg2, ackProbe));
        await ackProbe.ExpectMsgAsync(new StoreMessageSentAck(2));

        var msg3 = new MessageSent<string>(3, "c", true, NoQualifier, timestamp);
        queue.Tell(new StoreMessageSent<string>(msg3, ackProbe));
        await ackProbe.ExpectMsgAsync(new StoreMessageSentAck(3));

        var timestamp2 = DateTime.UtcNow.Ticks;
        queue.Tell(new StoreMessageConfirmed(2L, NoQualifier, timestamp2));
        
        queue.Tell(new LoadState(StateProbe.Ref));
        // note that msg1 is also confirmed (removed) by the confirmation of msg2
        var expectedState = new State<string>(CurrentSeqNr: 4, HighestConfirmedSeqNr: 2,
            ImmutableDictionary<string, (long, long)>.Empty.Add(NoQualifier, (2L, timestamp2)),
            ImmutableList<MessageSent<string>>.Empty.Add(msg3));
        await StateProbe.ExpectMsgAsync(expectedState);
        
        // replay
        await queue.GracefulStop(RemainingOrDefault);
        var queue2 = Sys.ActorOf(EventSourcedProducerQueue.Create<string>(pid, Sys));
        queue2.Tell(new LoadState(StateProbe.Ref));
        await StateProbe.ExpectMsgAsync(expectedState);
    }

    [Fact(Skip = "Test won't work until journal event publishing is implemented")]
    public async Task
        EventSourcedDurableProducerQueue_must_not_persist_Confirmed_with_lower_seqNr_than_already_confirmed()
    {
        var pid = NextPersistenceId();
        var ackProbe = CreateTestProbe();
        var queue = Sys.ActorOf(EventSourcedProducerQueue.Create<string>(pid, Sys));
        var timestamp = DateTime.UtcNow.Ticks;
        
        var msg1 = new MessageSent<string>(1, "a", true, NoQualifier, timestamp);
        queue.Tell(new StoreMessageSent<string>(msg1, ackProbe));
        await ackProbe.ExpectMsgAsync(new StoreMessageSentAck(1));
        
        var msg2 = new MessageSent<string>(2, "b", true, NoQualifier, timestamp);
        queue.Tell(new StoreMessageSent<string>(msg2, ackProbe));
        await ackProbe.ExpectMsgAsync(new StoreMessageSentAck(2));

        var timestamp2 = DateTime.UtcNow.Ticks;
        queue.Tell(new StoreMessageConfirmed(2L, NoQualifier, timestamp2));
        
        // lower
        queue.Tell(new StoreMessageConfirmed(1L, NoQualifier, timestamp2));
        await JournalOperationsProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
        
        // duplicate
        queue.Tell(new StoreMessageConfirmed(2L, NoQualifier, timestamp2));
        await JournalOperationsProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public async Task EventSourcedDurableProducerQueue_must_keep_track_of_confirmations_per_ConfirmationQualifier()
    {
        var pid = NextPersistenceId();
        var ackProbe = CreateTestProbe();
        var queue = Sys.ActorOf(EventSourcedProducerQueue.Create<string>(pid, Sys));
        var timestamp = DateTime.UtcNow.Ticks;
        
        var msg1 = new MessageSent<string>(1, "a", true, "q1", timestamp);
        queue.Tell(new StoreMessageSent<string>(msg1, ackProbe));
        await ackProbe.ExpectMsgAsync(new StoreMessageSentAck(1));
        
        var msg2 = new MessageSent<string>(2, "b", true, "q1", timestamp);
        queue.Tell(new StoreMessageSent<string>(msg2, ackProbe));
        await ackProbe.ExpectMsgAsync(new StoreMessageSentAck(2));
        
        var msg3 = new MessageSent<string>(3, "c", true, "q2", timestamp);
        queue.Tell(new StoreMessageSent<string>(msg3, ackProbe));
        await ackProbe.ExpectMsgAsync(new StoreMessageSentAck(3));
        
        var msg4 = new MessageSent<string>(4, "d", true, "q2", timestamp);
        queue.Tell(new StoreMessageSent<string>(msg4, ackProbe));
        await ackProbe.ExpectMsgAsync(new StoreMessageSentAck(4));
        
        var msg5 = new MessageSent<string>(5, "e", true, "q2", timestamp);
        queue.Tell(new StoreMessageSent<string>(msg5, ackProbe));
        await ackProbe.ExpectMsgAsync(new StoreMessageSentAck(5));
        
        var timestamp2 = DateTime.UtcNow.Ticks;
        queue.Tell(new StoreMessageConfirmed(4L, "q2", timestamp2));
        
        queue.Tell(new LoadState(StateProbe.Ref));
        // note that msg3 is also confirmed (removed) by the confirmation of msg4, same qualifier
        // but msg1 and msg2 are still unconfirmed
        var expectedState = new State<string>(CurrentSeqNr: 6, HighestConfirmedSeqNr: 4,
            ImmutableDictionary<string, (long, long)>.Empty.Add("q2", (4L, timestamp2)),
            ImmutableList<MessageSent<string>>.Empty.Add(msg1).Add(msg2).Add(msg5));
        
        await StateProbe.ExpectMsgAsync(expectedState);
        
        // replay
        await queue.GracefulStop(RemainingOrDefault);
        var queue2 = Sys.ActorOf(EventSourcedProducerQueue.Create<string>(pid, Sys));
        queue2.Tell(new LoadState(StateProbe.Ref));
        await StateProbe.ExpectMsgAsync(expectedState);
    }

    [Fact]
    public async Task EventSourcedDurableProducerQueue_must_cleanup_old_ConfirmationQualifier_entries()
    {
        var pid = NextPersistenceId();
        var ackProbe = CreateTestProbe();
        var settings = EventSourcedProducerQueue.Settings.Create(Sys) with
        {
            CleanupUnusedAfter = TimeSpan.FromMilliseconds(100)
        };
        var queue = Sys.ActorOf(EventSourcedProducerQueue.Create<string>(pid, settings));
        var now = DateTime.UtcNow.Ticks;
        var timestamp0 = now - 70000 * TimeSpan.TicksPerMillisecond;
        
        var msg1 = new MessageSent<string>(1, "a", true, "q1", timestamp0);
        queue.Tell(new StoreMessageSent<string>(msg1, ackProbe));
        await ackProbe.ExpectMsgAsync(new StoreMessageSentAck(1));
        
        var msg2 = new MessageSent<string>(2, "b", true, "q1", timestamp0);
        queue.Tell(new StoreMessageSent<string>(msg2, ackProbe));
        await ackProbe.ExpectMsgAsync(new StoreMessageSentAck(2));
        
        var msg3 = new MessageSent<string>(3, "c", true, "q2", timestamp0);
        queue.Tell(new StoreMessageSent<string>(msg3, ackProbe));
        await ackProbe.ExpectMsgAsync(new StoreMessageSentAck(3));
        
        var msg4 = new MessageSent<string>(4, "d", true, "q2", timestamp0);
        queue.Tell(new StoreMessageSent<string>(msg4, ackProbe));
        await ackProbe.ExpectMsgAsync(new StoreMessageSentAck(4));

        var timestamp1 = now - 60000 * TimeSpan.TicksPerMillisecond;
        queue.Tell(new StoreMessageConfirmed(1L, "q1", timestamp1));

        // cleanup tick
        await Task.Delay(TimeSpan.FromMilliseconds(1000));
        
        // q1, seqNr 2 is not confirmed yet, so q1 entries shouldn't be cleaned yet
        queue.Tell(new LoadState(StateProbe.Ref));
        
        var expectedState = new State<string>(CurrentSeqNr: 5, HighestConfirmedSeqNr: 1,
            ImmutableDictionary<string, (long, long)>.Empty.Add("q1", (1L, timestamp1)),
            ImmutableList<MessageSent<string>>.Empty.Add(msg2).Add(msg3).Add(msg4));
        await StateProbe.ExpectMsgAsync(expectedState);
        
        var timestamp2 = now - 50000 * TimeSpan.TicksPerMillisecond;
        queue.Tell(new StoreMessageConfirmed(2L, "q1", timestamp2));
        
        var timestamp3 = now + 10000 * TimeSpan.TicksPerMillisecond; // not old
        queue.Tell(new StoreMessageConfirmed(4L, "q2", timestamp3));
        
        // cleanup tick
        await Task.Delay(TimeSpan.FromMilliseconds(1000));
        
        // q1, seqNr 2 is now confirmed, so q1 entries should be cleaned
        queue.Tell(new LoadState(StateProbe.Ref));
        
        var expectedState2 = new State<string>(CurrentSeqNr: 5, HighestConfirmedSeqNr: 4,
            ImmutableDictionary<string, (long, long)>.Empty.Add("q2", (4L, timestamp3)),
            ImmutableList<MessageSent<string>>.Empty);
        
        var actualState2 = await StateProbe.ExpectMsgAsync<State<string>>();
        actualState2.Should().BeEquivalentTo(expectedState2);
    }
    
}