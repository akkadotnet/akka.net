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
using Akka.Delivery;
using Akka.Persistence.Delivery;
using Akka.TestKit;
using Akka.Util.Internal;
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

    private static readonly AtomicCounter _pidCounter = new AtomicCounter(0);
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
        var expectedState = new State<string>(CurrentSeqNr:3, HighestConfirmedSeqNr:0, 
            ConfirmedSeqNr:ImmutableDictionary<string, (long, long)>.Empty, Unconfirmed:ImmutableList.Create(msg1, msg2));
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
}