//-----------------------------------------------------------------------
// <copyright file="SystemMessageDeliveryStageSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch.SysMsg;
using Akka.Remote.Artery;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
// Akka.Remote.SysAck/Akka.Remote.SysNack (classic AckedDelivery.cs) are enclosing-namespace types of
// Akka.Remote.Tests.Artery and would otherwise shadow Akka.Remote.Artery.SysAck/SysNack (design.md gate
// G3) in unqualified lookups -- alias to the Artery ones explicitly.
using SysAck = Akka.Remote.Artery.Ack;
using SysNack = Akka.Remote.Artery.Nack;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Stage-level tests for <see cref="SystemMessageDeliveryStage"/>, the OUTBOUND half of
    /// reliable system-message delivery (design.md gate G3). Uses <c>TestSource</c>/<c>TestSink</c>
    /// probes to drive/observe the stage directly (mirrors <c>ArteryHandshakeSpec</c>'s patterns) --
    /// no TCP, no real association transport.
    /// </summary>
    public class SystemMessageDeliveryStageSpec : AkkaSpec
    {
        public SystemMessageDeliveryStageSpec(ITestOutputHelper output) : base(output)
        {
        }

        private static UniqueAddress NewLocal() => new(new Address("akka", "local-sys", "local-host", 2551), 111L);

        private static Address NewRemote() => new("akka", "remote-sys", "remote-host", 2552);

        private sealed record TestSystemMessage(string Name) : ISystemMessage;

        private static IOutboundEnvelope SystemMessageElement(string name, string recipientPath = "akka://remote-sys@remote-host:2552/user/target") =>
            new OutboundEnvelope(new TestSystemMessage(name), null, recipientPath);

        /// <summary>
        /// Test harness bundling a materialized <see cref="SystemMessageDeliveryStage"/> with hooks
        /// to observe/drive its <see cref="IOutboundContext"/> seam: captured control subscribers
        /// (so the test can simulate an inbound SysAck/SysNack arriving from "the peer"), and captured
        /// quarantine calls (so give-up can be asserted on directly).
        /// </summary>
        private sealed class Harness
        {
            public required AssociationRegistry Registry { get; init; }
            public required Address RemoteAddress { get; init; }
            public required TestPublisher.Probe<IOutboundEnvelope> Pub { get; init; }
            public required TestSubscriber.Probe<IOutboundEnvelope> Sub { get; init; }
            public required List<IControlMessageSubscriber> Subscribers { get; init; }
            public required List<(Address Address, long Uid)> QuarantineCalls { get; init; }

            /// <summary>
            /// The Association-owned unacked-buffer/seqNo/incarnation state this materialization
            /// attaches to (design.md group 9 invariant 3) -- exposed so a test can simulate a
            /// stream restart by materializing a SECOND stage against this SAME instance.
            /// </summary>
            public required SystemMessageDeliveryState State { get; init; }

            /// <summary>Simulates an inbound SysAck/SysNack "arriving from the peer" for every subscriber (mirrors ArteryRemoting.NotifyControlSubscribers).</summary>
            public void DeliverControlMessage(long originUid, object message)
            {
                foreach (var subscriber in Subscribers)
                    subscriber.ControlMessageReceived(originUid, message);
            }

            public UniqueAddress CompleteHandshake(long remoteUid)
            {
                var peer = new UniqueAddress(RemoteAddress, remoteUid);
                Registry.CompleteHandshake(RemoteAddress, peer);
                return peer;
            }
        }

        private Harness BuildHarness(
            ActorMaterializer materializer,
            int bufferCapacity = 20_000,
            TimeSpan? resendInterval = null,
            TimeSpan? giveUpAfter = null)
        {
            var registry = new AssociationRegistry();
            var remoteAddress = NewRemote();
            var subscribers = new List<IControlMessageSubscriber>();
            var quarantineCalls = new List<(Address, long)>();
            var state = new SystemMessageDeliveryState();

            var context = new AssociationRegistryOutboundContext(
                registry,
                NewLocal(),
                remoteAddress,
                sendControl: _ => { },
                subscribeControl: subscribers.Add,
                unsubscribeControl: s => subscribers.Remove(s),
                quarantine: (addr, uid) => quarantineCalls.Add((addr, uid)));

            var stage = new SystemMessageDeliveryStage(
                context,
                state,
                bufferCapacity,
                resendInterval ?? TimeSpan.FromMilliseconds(200),
                giveUpAfter ?? TimeSpan.FromSeconds(30));

            var (pub, sub) = this.SourceProbe<IOutboundEnvelope>()
                .ViaMaterialized(Flow.FromGraph(stage), Keep.Left)
                .ToMaterialized(this.SinkProbe<IOutboundEnvelope>(), Keep.Both)
                .Run(materializer);

            return new Harness
            {
                Registry = registry,
                RemoteAddress = remoteAddress,
                Pub = pub,
                Sub = sub,
                Subscribers = subscribers,
                QuarantineCalls = quarantineCalls,
                State = state
            };
        }

        /// <summary>
        /// Simulates design.md group 9's outbound-stream reconnect: materializes a SECOND,
        /// independent <see cref="SystemMessageDeliveryStage"/> instance against <paramref name="original"/>'s
        /// SAME <see cref="Harness.State"/> (and registry/remote address) -- exactly what
        /// <c>ArteryRemoting.MaterializeOutboundStream</c> does when it re-materializes the control
        /// stream after a backoff, per <see cref="Association.SystemMessageDeliveryState"/>. The
        /// caller must first retire the ORIGINAL materialization (complete its upstream and wait
        /// for its subscriber to unregister) so the two <c>Logic</c> instances never run
        /// concurrently against the shared state -- mirroring production, where the old stream's
        /// completion Task always settles before a restart is scheduled.
        /// </summary>
        private (TestPublisher.Probe<IOutboundEnvelope> Pub, TestSubscriber.Probe<IOutboundEnvelope> Sub) RestartStage(
            ActorMaterializer materializer,
            Harness original,
            int bufferCapacity = 20_000,
            TimeSpan? resendInterval = null,
            TimeSpan? giveUpAfter = null)
        {
            var context = new AssociationRegistryOutboundContext(
                original.Registry,
                NewLocal(),
                original.RemoteAddress,
                sendControl: _ => { },
                subscribeControl: original.Subscribers.Add,
                unsubscribeControl: s => original.Subscribers.Remove(s),
                quarantine: (addr, uid) => original.QuarantineCalls.Add((addr, uid)));

            var stage = new SystemMessageDeliveryStage(
                context,
                original.State,
                bufferCapacity,
                resendInterval ?? TimeSpan.FromMilliseconds(200),
                giveUpAfter ?? TimeSpan.FromSeconds(30));

            return this.SourceProbe<IOutboundEnvelope>()
                .ViaMaterialized(Flow.FromGraph(stage), Keep.Left)
                .ToMaterialized(this.SinkProbe<IOutboundEnvelope>(), Keep.Both)
                .Run(materializer);
        }

        [Fact(DisplayName = "happy-path: the first system message is wrapped as SystemMessageEnvelope with seqNo 1 and ackReplyTo the local address")]
        public async Task Should_Wrap_First_System_Message_With_SeqNo_1()
        {
            var materializer = ActorMaterializer.Create(Sys);
            var h = BuildHarness(materializer);

            await h.Sub.RequestAsync(1);
            await h.Pub.SendNextAsync(SystemMessageElement("m1"));

            var delivered = await h.Sub.ExpectNextAsync(TimeSpan.FromSeconds(3));
            delivered.Message.Should().BeOfType<SystemMessageEnvelope>();
            var sme = (SystemMessageEnvelope)delivered.Message;
            sme.SeqNo.Should().Be(1L);
            sme.AckReplyTo.Should().Be(NewLocal());
            sme.RecipientPath.Should().Be("akka://remote-sys@remote-host:2552/user/target");
            sme.Message.Should().BeOfType<TestSystemMessage>();
            ((TestSystemMessage)sme.Message).Name.Should().Be("m1");
        }

        [Fact(DisplayName = "happy-path: successive system messages get monotonically increasing seq numbers")]
        public async Task Should_Assign_Monotonically_Increasing_SeqNo()
        {
            var materializer = ActorMaterializer.Create(Sys);
            var h = BuildHarness(materializer);

            await h.Sub.RequestAsync(3);
            await h.Pub.SendNextAsync(SystemMessageElement("m1"));
            await h.Pub.SendNextAsync(SystemMessageElement("m2"));
            await h.Pub.SendNextAsync(SystemMessageElement("m3"));

            ((SystemMessageEnvelope)(await h.Sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Message).SeqNo.Should().Be(1L);
            ((SystemMessageEnvelope)(await h.Sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Message).SeqNo.Should().Be(2L);
            ((SystemMessageEnvelope)(await h.Sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Message).SeqNo.Should().Be(3L);
        }

        [Fact(DisplayName = "ACK pops the buffer prefix: an acked entry is never resent again, even after the resend timer keeps ticking")]
        public async Task Should_Pop_Buffer_On_Ack_And_Stop_Resending()
        {
            var materializer = ActorMaterializer.Create(Sys);
            var h = BuildHarness(materializer, resendInterval: TimeSpan.FromMilliseconds(150));
            var peer = h.CompleteHandshake(remoteUid: 222L);

            await h.Sub.RequestAsync(1);
            await h.Pub.SendNextAsync(SystemMessageElement("m1"));
            var first = (SystemMessageEnvelope)(await h.Sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Message;
            first.SeqNo.Should().Be(1L);

            // SysAck it -- from the CURRENT remote uid, so it must be accepted.
            h.DeliverControlMessage(peer.Uid, new SysAck(1L, peer));

            // No resend should ever arrive for this acked entry -- generous wait, well past several
            // resend-timer ticks.
            await h.Sub.RequestAsync(1);
            await h.Sub.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(600));
        }

        [Fact(DisplayName = "NACK pops the acked prefix and IMMEDIATELY resends the remaining tail, without waiting for the next resend-timer tick")]
        public async Task Should_Pop_Prefix_And_Immediately_Resend_Tail_On_Nack()
        {
            var materializer = ActorMaterializer.Create(Sys);
            // A long resend interval -- if the resend arrives well before it elapses, it can only be
            // the immediate SysNack-triggered resend, not a coincidental timer tick.
            var h = BuildHarness(materializer, resendInterval: TimeSpan.FromSeconds(10));
            var peer = h.CompleteHandshake(remoteUid: 333L);

            await h.Sub.RequestAsync(2);
            await h.Pub.SendNextAsync(SystemMessageElement("m1"));
            await h.Pub.SendNextAsync(SystemMessageElement("m2"));
            (await h.Sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Should().NotBeNull(); // seq 1
            (await h.Sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Should().NotBeNull(); // seq 2

            // SysNack(1) -- seq 1 is acked-away (popped), seq 2 must be resent immediately.
            h.DeliverControlMessage(peer.Uid, new SysNack(1L, peer));

            await h.Sub.RequestAsync(1);
            var resent = (SystemMessageEnvelope)(await h.Sub.ExpectNextAsync(TimeSpan.FromSeconds(2))).Message;
            resent.SeqNo.Should().Be(2L, "the SysNack'd gap's tail (seq 2) must be resent immediately, well before the 10s resend timer would ever fire");
        }

        [Fact(DisplayName = "the resend timer periodically resends the whole unacknowledged window while any entry remains unacked")]
        public async Task Should_Resend_Unacked_Window_Periodically()
        {
            var materializer = ActorMaterializer.Create(Sys);
            var h = BuildHarness(materializer, resendInterval: TimeSpan.FromMilliseconds(150));
            h.CompleteHandshake(remoteUid: 444L);

            await h.Sub.RequestAsync(1);
            await h.Pub.SendNextAsync(SystemMessageElement("m1"));
            (await h.Sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Should().NotBeNull(); // original send

            // Never acked -- the resend timer must eventually resend seq 1 again.
            await h.Sub.RequestAsync(1);
            var resent = (SystemMessageEnvelope)(await h.Sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Message;
            resent.SeqNo.Should().Be(1L);
        }

        [Fact(DisplayName = "#6414 regression: a stale SysAck from a PRIOR incarnation (uid mismatch) is ignored -- never pops the buffer, never quarantines")]
        public async Task Should_Ignore_Stale_Ack_From_Prior_Incarnation()
        {
            var materializer = ActorMaterializer.Create(Sys);
            var h = BuildHarness(materializer, resendInterval: TimeSpan.FromMilliseconds(150));
            var currentPeer = h.CompleteHandshake(remoteUid: 555L);

            await h.Sub.RequestAsync(1);
            await h.Pub.SendNextAsync(SystemMessageElement("m1"));
            (await h.Sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Should().NotBeNull();

            // A late SysAck claiming to be from a STALE prior uid (999L != the current 555L) -- must be
            // silently ignored: it must NOT pop the buffer (a resend must still occur) and must NOT
            // quarantine anything.
            var staleUid = 999L;
            var staleFrom = new UniqueAddress(h.RemoteAddress, staleUid);
            h.DeliverControlMessage(staleUid, new SysAck(1L, staleFrom));

            // The entry is still unacked -- the resend timer proves it wasn't popped.
            await h.Sub.RequestAsync(1);
            var resent = (SystemMessageEnvelope)(await h.Sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Message;
            resent.SeqNo.Should().Be(1L, "a stale SysAck from a prior incarnation must never pop the buffer (#6414)");

            h.QuarantineCalls.Should().BeEmpty("a stale SysAck must never trigger a quarantine");

            // A genuine SysAck from the CURRENT uid still works normally afterwards.
            h.DeliverControlMessage(currentPeer.Uid, new SysAck(1L, currentPeer));
            await h.Sub.RequestAsync(1);
            await h.Sub.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(400));
        }

        [Fact(DisplayName = "buffer-overflow: exceeding system-message-buffer-size gives up, quarantines, and resets local delivery state (seqNo restarts at 1)")]
        public async Task Should_Quarantine_On_Buffer_Overflow()
        {
            var materializer = ActorMaterializer.Create(Sys);
            // Never ack anything and use a tiny buffer -- the 3rd message overflows a capacity-2 buffer.
            var h = BuildHarness(materializer, bufferCapacity: 2, resendInterval: TimeSpan.FromSeconds(30));
            h.CompleteHandshake(remoteUid: 666L);

            await h.Sub.RequestAsync(3);
            await h.Pub.SendNextAsync(SystemMessageElement("m1"));
            await h.Pub.SendNextAsync(SystemMessageElement("m2"));
            (await h.Sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Should().NotBeNull(); // seq 1
            (await h.Sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Should().NotBeNull(); // seq 2

            // The 3rd message overflows the capacity-2 buffer -- dropped, never emitted.
            await h.Pub.SendNextAsync(SystemMessageElement("overflow"));
            await h.Sub.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300));

            h.QuarantineCalls.Should().ContainSingle("buffer overflow must give up and quarantine exactly once");

            // Local delivery state reset -- the NEXT system message restarts seqNo at 1 (idempotent
            // reset, same effect ClearSystemMessageDelivery produces).
            await h.Pub.SendNextAsync(SystemMessageElement("after-reset"));
            var afterReset = (SystemMessageEnvelope)(await h.Sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Message;
            afterReset.SeqNo.Should().Be(1L, "give-up must reset seqNo back to 1, exactly like ClearSystemMessageDelivery");
        }

        [Fact(DisplayName = "give-up-timeout: the oldest unacknowledged entry exceeding give-up-system-message-after quarantines and resets, without needing a buffer overflow")]
        public async Task Should_Quarantine_On_Give_Up_Timeout()
        {
            var materializer = ActorMaterializer.Create(Sys);
            var h = BuildHarness(
                materializer,
                bufferCapacity: 20_000,
                resendInterval: TimeSpan.FromMilliseconds(50),
                giveUpAfter: TimeSpan.FromMilliseconds(250));
            h.CompleteHandshake(remoteUid: 777L);

            await h.Sub.RequestAsync(1);
            await h.Pub.SendNextAsync(SystemMessageElement("m1"));
            (await h.Sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Should().NotBeNull();

            // Never ack it -- wait past give-up-system-message-after (250ms) plus a couple of resend
            // ticks (50ms each) for the timer to observe the timeout.
            await AwaitConditionAsync(() => Task.FromResult(h.QuarantineCalls.Count > 0), TimeSpan.FromSeconds(3));

            // Local delivery state reset -- proven the same way as the overflow test. A stale
            // resend of the pre-reset "m1" may still be sitting queued (give-up deliberately does
            // not clear already-queued-for-emission elements -- see ResetDeliveryState's remarks),
            // so fish past it rather than assume the very next element is the post-reset one.
            await h.Pub.SendNextAsync(SystemMessageElement("after-timeout"));

            SystemMessageEnvelope afterReset;
            var drainedStale = 0;
            while (true)
            {
                await h.Sub.RequestAsync(1);
                afterReset = (SystemMessageEnvelope)(await h.Sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Message;
                if (((TestSystemMessage)afterReset.Message).Name != "after-timeout")
                {
                    drainedStale++;
                    drainedStale.Should().BeLessThan(10, "only a handful of stale pre-reset resends can possibly be queued");
                    continue;
                }

                break;
            }

            afterReset.SeqNo.Should().Be(1L, "give-up-timeout must reset seqNo back to 1");
        }

        [Fact(DisplayName = "ClearSystemMessageDelivery is intercepted and consumed locally (never forwarded to Out) and idempotently resets seqNo/buffer")]
        public async Task Should_Consume_ClearSystemMessageDelivery_Locally_And_Idempotently()
        {
            var materializer = ActorMaterializer.Create(Sys);
            var h = BuildHarness(materializer, resendInterval: TimeSpan.FromSeconds(30));
            h.CompleteHandshake(remoteUid: 888L);

            await h.Sub.RequestAsync(1);
            await h.Pub.SendNextAsync(SystemMessageElement("m1"));
            (await h.Sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Should().NotBeNull(); // seq 1, now buffered unacked

            // Apply Clear TWICE (idempotent) -- neither call may ever reach Out.
            await h.Pub.SendNextAsync(new OutboundEnvelope(new ClearSystemMessageDelivery(1), null, null));
            await h.Pub.SendNextAsync(new OutboundEnvelope(new ClearSystemMessageDelivery(1), null, null));
            await h.Sub.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300));

            // seqNo restarts at 1 for the next message -- proves the buffer/seqNo were reset.
            await h.Pub.SendNextAsync(SystemMessageElement("after-clear"));
            await h.Sub.RequestAsync(1);
            var afterClear = (SystemMessageEnvelope)(await h.Sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Message;
            afterClear.SeqNo.Should().Be(1L);
        }

        [Fact(DisplayName = "new-incarnation reset: observing AssociationState.Incarnation advance (a genuinely new peer uid) automatically resets seqNo/buffer")]
        public async Task Should_Reset_Automatically_On_New_Incarnation()
        {
            var materializer = ActorMaterializer.Create(Sys);
            var h = BuildHarness(materializer, resendInterval: TimeSpan.FromMilliseconds(150));
            h.CompleteHandshake(remoteUid: 999L);

            await h.Sub.RequestAsync(2);
            await h.Pub.SendNextAsync(SystemMessageElement("m1"));
            await h.Pub.SendNextAsync(SystemMessageElement("m2"));
            ((SystemMessageEnvelope)(await h.Sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Message).SeqNo.Should().Be(1L);
            ((SystemMessageEnvelope)(await h.Sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Message).SeqNo.Should().Be(2L);

            // Remote restart under a NEW uid -- a genuinely new incarnation (Incarnation advances).
            h.CompleteHandshake(remoteUid: 1000L);

            await h.Pub.SendNextAsync(SystemMessageElement("m3-new-incarnation"));

            // A stale resend of a pre-reset entry could theoretically still be queued (the resend
            // timer's own incarnation check races the test's own CompleteHandshake call) -- fish
            // past it by name rather than assuming strict ordinal position.
            SystemMessageEnvelope afterNewIncarnation;
            var drainedStale = 0;
            while (true)
            {
                await h.Sub.RequestAsync(1);
                afterNewIncarnation = (SystemMessageEnvelope)(await h.Sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Message;
                if (((TestSystemMessage)afterNewIncarnation.Message).Name != "m3-new-incarnation")
                {
                    drainedStale++;
                    drainedStale.Should().BeLessThan(10, "only a handful of stale pre-reset resends can possibly be queued");
                    continue;
                }

                break;
            }

            afterNewIncarnation.SeqNo.Should().Be(1L, "a new incarnation must reset the outbound seqNo back to 1 (design.md invariant 2)");
        }

        [Fact(DisplayName = "design.md group 9 invariant 3: unacknowledged system messages survive a simulated outbound-stream restart -- the restarted materialization eagerly re-emits them in order, and the seqNo counter continues rather than resetting")]
        public async Task Should_Survive_Unacked_Buffer_Across_Simulated_Stream_Restart()
        {
            var materializer = ActorMaterializer.Create(Sys);
            var h = BuildHarness(materializer, resendInterval: TimeSpan.FromSeconds(30));
            var peer = h.CompleteHandshake(remoteUid: 2222L);

            await h.Sub.RequestAsync(2);
            await h.Pub.SendNextAsync(SystemMessageElement("watch-1"));
            await h.Pub.SendNextAsync(SystemMessageElement("watch-2"));
            ((SystemMessageEnvelope)(await h.Sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Message).SeqNo.Should().Be(1L);
            ((SystemMessageEnvelope)(await h.Sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Message).SeqNo.Should().Be(2L);

            // Both seq 1 and seq 2 are now sitting UNACKED in the shared buffer. Retire the
            // original materialization -- mirrors production, where the old outbound TCP
            // connection's completion Task always settles (here: upstream completes, which drives
            // PostStop -> UnsubscribeControl) BEFORE a restart is ever scheduled. Poll on the
            // subscriber list actually shrinking back to empty -- a deterministic progress signal,
            // not a wall-clock wait -- so the two Logic instances never run concurrently.
            await h.Pub.SendCompleteAsync();
            await AwaitConditionAsync(() => Task.FromResult(h.Subscribers.Count == 0), TimeSpan.FromSeconds(3));

            // Simulate the restart: a brand-new SystemMessageDeliveryStage/Logic materialization,
            // attached to the SAME Harness.State (SystemMessageDeliveryState) the first one used.
            // Long resend interval (matches BuildHarness's own default) -- this test is not about
            // resend timing, and a short interval would make the final "nothing left to resend"
            // assertion race the resend timer instead of proving anything about the Ack itself.
            var (pub2, sub2) = RestartStage(materializer, h, resendInterval: TimeSpan.FromSeconds(30));

            // The restarted materialization must eagerly re-emit BOTH still-unacked entries, in
            // their ORIGINAL seq order, without needing a new system message to arrive first (see
            // SystemMessageDeliveryStage.Logic.PreStart's eager-requeue remarks).
            await sub2.RequestAsync(2);
            var first = (SystemMessageEnvelope)(await sub2.ExpectNextAsync(TimeSpan.FromSeconds(3))).Message;
            var second = (SystemMessageEnvelope)(await sub2.ExpectNextAsync(TimeSpan.FromSeconds(3))).Message;
            first.SeqNo.Should().Be(1L, "the restart must not lose or reorder the still-unacknowledged seq-1 entry");
            second.SeqNo.Should().Be(2L, "the restart must not lose or reorder the still-unacknowledged seq-2 entry");

            // A freshly-created system message on the RESTARTED stage continues the SAME seq
            // sequence (3) -- proving NextSeq survived the restart too, not just the buffer's
            // contents.
            await pub2.SendNextAsync(SystemMessageElement("watch-3"));
            await sub2.RequestAsync(1);
            var third = (SystemMessageEnvelope)(await sub2.ExpectNextAsync(TimeSpan.FromSeconds(3))).Message;
            third.SeqNo.Should().Be(3L, "the seqNo counter must also survive the restart, continuing from where the old materialization left off");

            // An Ack delivered to the RESTARTED stage for the pre-restart seq-1/seq-2 entries pops
            // them from the SAME shared buffer -- proving Ack/Nack processing operates correctly
            // on state inherited from a prior materialization, not just freshly-created state.
            h.DeliverControlMessage(peer.Uid, new SysAck(2L, peer));
            await sub2.RequestAsync(1);
            await sub2.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(400));
        }
    }
}
