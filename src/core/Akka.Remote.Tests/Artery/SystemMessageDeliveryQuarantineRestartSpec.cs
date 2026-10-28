//-----------------------------------------------------------------------
// <copyright file="SystemMessageDeliveryQuarantineRestartSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.IO;
using System.Threading;
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
using SysAck = Akka.Remote.Artery.Ack;
using SysNack = Akka.Remote.Artery.Nack;

namespace Akka.Remote.Tests.Artery
{
    public sealed class SystemMessageDeliveryQuarantineRestartSpec : AkkaSpec
    {
        public SystemMessageDeliveryQuarantineRestartSpec(ITestOutputHelper output) : base(output)
        {
        }

        private static readonly UniqueAddress Local = new(new Address("akka", "local", "127.0.0.1", 2551), 11L);
        private static readonly Address Remote = new("akka", "remote", "127.0.0.1", 2552);

        // Observe a real control callback reading association state, not a timing delay.
        // The callback runs after PreStart and before any subsequent inlet event.
        private sealed class ObservingContext(IOutboundContext inner) : IOutboundContext
        {
            private TaskCompletionSource<bool>? _nextRead;
            public TaskCompletionSource<IControlMessageSubscriber> Subscriber { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<bool> Stopped { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task ObserveNextRead()
            {
                var signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Interlocked.Exchange(ref _nextRead, signal);
                return signal.Task;
            }

            public AssociationState AssociationState
            {
                get
                {
                    var state = inner.AssociationState;
                    Interlocked.Exchange(ref _nextRead, null)?.TrySetResult(true);
                    return state;
                }
            }

            public UniqueAddress LocalAddress => inner.LocalAddress;
            public Address RemoteAddress => inner.RemoteAddress;
            public long HandshakeGeneration => inner.HandshakeGeneration;
            public AssociationState CompleteHandshake(UniqueAddress peer) => inner.CompleteHandshake(peer);
            public void SubscribeHandshakeStateChanged(object subscriber, Action listener) => inner.SubscribeHandshakeStateChanged(subscriber, listener);
            public void UnsubscribeHandshakeStateChanged(object subscriber) => inner.UnsubscribeHandshakeStateChanged(subscriber);
            public void SendControl(object message) => inner.SendControl(message);
            public void SubscribeControl(IControlMessageSubscriber subscriber) => Subscriber.TrySetResult(subscriber);
            public void UnsubscribeControl(IControlMessageSubscriber subscriber) => Stopped.TrySetResult(true);
            public void Quarantine() => inner.Quarantine();
            public void ReturnUndelivered(IOutboundEnvelope envelope) => inner.ReturnUndelivered(envelope);
        }

        private (AssociationRegistry Registry, Association Association, UniqueAddress Peer, ObservingContext Context) SeedUnacknowledgedWatch()
        {
            var registry = new AssociationRegistry();
            var peer = new UniqueAddress(Remote, 42L);
            registry.CompleteHandshake(Remote, peer);
            var association = registry.AssociationFor(Remote);
            var watch = new Watch((IInternalActorRef)CreateTestProbe().Ref, (IInternalActorRef)TestActor);
            var reliable = new SystemMessageEnvelope(watch, 1L, Local, "/user/target");
            var state = association.SystemMessageDeliveryState;
            state.Buffer.Enqueue((1L, new OutboundEnvelope(reliable, null, null), DateTime.UtcNow));
            state.NextSeq = 2L;
            state.CurrentIncarnation = association.CurrentState.Incarnation;
            var context = new ObservingContext(new AssociationRegistryOutboundContext(registry, Local, Remote, _ => { }));
            return (registry, association, peer, context);
        }

        private static SystemMessageDeliveryStage Stage(ObservingContext context, SystemMessageDeliveryState state) =>
            new(context, state, 20_000, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1));

        [Theory(DisplayName = "Restarting after quarantine never replays an unacknowledged Watch ahead of retained control traffic or a delayed Clear")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_Clear_Quarantined_Reliable_State_Before_Restart_Replay(bool earlierControl)
        {
            var cancellation = TestContext.Current.CancellationToken;
            var (registry, association, peer, context) = SeedUnacknowledgedWatch();
            var materializer = ActorMaterializer.Create(Sys);
            var (oldPub, oldSub) = this.SourceProbe<IOutboundEnvelope>()
                .Via(Flow.FromGraph(Stage(context, association.SystemMessageDeliveryState)))
                .ToMaterialized(this.SinkProbe<IOutboundEnvelope>(), Keep.Both)
                .Run(materializer);
            await oldSub.RequestAsync(1);
            (await oldSub.ExpectNextAsync(TimeSpan.FromSeconds(3), cancellation)).Message.Should().BeOfType<SystemMessageEnvelope>();
            var retained = new OutboundEnvelope(new DaemonMsgCreate(Props.Empty, Deploy.None, "/remote/target", TestActor), null, null);
            var notice = new OutboundEnvelope(new ArteryQuarantined(Local, peer.Uid), null, null);
            if (earlierControl) association.TryEnqueueControl(retained).Should().BeTrue();
            association.TryEnqueueControl(notice).Should().BeTrue();
            association.TryEnqueueControl(new OutboundEnvelope(
                new ClearSystemMessageDelivery(association.CurrentState.Incarnation), null, null)).Should().BeTrue();
            association.Quarantine(peer.Uid).Should().BeTrue();

            // Fail the actual old stream after its Watch was emitted but never acked.
            // Await PostStop before touching shared delivery state from restart cleanup.
            var failure = new IOException("Injected control-stream failure.");
            await oldPub.SendErrorAsync(failure, cancellation);
            (await oldSub.ExpectErrorAsync(cancellation)).Should().BeSameAs(failure);
            await context.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellation);
            association.SystemMessageDeliveryState.Buffer.Should().HaveCount(1);
            association.PrepareControlRestart();
            association.CompleteControlOutbound();
            context = new ObservingContext(new AssociationRegistryOutboundContext(registry, Local, Remote, _ => { }));
            var sink = ChannelSource.FromReader(association.ControlReader)
                .Via(Flow.FromGraph(Stage(context, association.SystemMessageDeliveryState)))
                .RunWith(this.SinkProbe<IOutboundEnvelope>(), materializer);
            await sink.RequestAsync(4);
            if (earlierControl)
                (await sink.ExpectNextAsync(TimeSpan.FromSeconds(3), cancellation)).Should().BeSameAs(retained);
            (await sink.ExpectNextAsync(TimeSpan.FromSeconds(3), cancellation)).Should().BeSameAs(notice);
            await sink.ExpectCompleteAsync(cancellationToken: cancellation);
            association.SystemMessageDeliveryState.Buffer.Should().BeEmpty();
            association.SystemMessageDeliveryState.NextSeq.Should().Be(1L);
        }

        [Fact(DisplayName = "A quarantine observed after PreStart removes already-staged Watch replay before downstream demand")]
        public async Task Should_Remove_Staged_Replay_When_Current_Peer_Is_Quarantined_Before_OnPull()
        {
            var cancellation = TestContext.Current.CancellationToken;
            var (_, association, peer, context) = SeedUnacknowledgedWatch();
            var (pub, sub) = this.SourceProbe<IOutboundEnvelope>()
                .Via(Flow.FromGraph(Stage(context, association.SystemMessageDeliveryState)))
                .ToMaterialized(this.SinkProbe<IOutboundEnvelope>(), Keep.Both)
                .Run(ActorMaterializer.Create(Sys));
            var subscriber = await context.Subscriber.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellation);
            var started = context.ObserveNextRead();
            subscriber.ControlMessageReceived(peer.Uid, new SysAck(0L, peer));
            await started.WaitAsync(TimeSpan.FromSeconds(3), cancellation);

            // PreStart has queued the old Watch, but no demand has allowed it out.
            association.Quarantine(peer.Uid).Should().BeTrue();
            var barrier = new OutboundEnvelope("after-quarantine", null, null);
            await sub.RequestAsync(2);
            var rawWatch = new OutboundEnvelope(new Watch(
                (IInternalActorRef)CreateTestProbe().Ref, (IInternalActorRef)TestActor), null, "/user/target");
            await pub.SendNextAsync(rawWatch);
            await pub.SendNextAsync(barrier);
            (await sub.ExpectNextAsync(TimeSpan.FromSeconds(3), cancellation)).Should().BeSameAs(barrier);
            await pub.SendCompleteAsync();
            await sub.ExpectCompleteAsync(cancellationToken: cancellation);
            association.SystemMessageDeliveryState.Buffer.Should().BeEmpty("raw queued Watch must not refill the reset buffer");
        }

        [Fact(DisplayName = "A demanded Nack callback cannot refill or emit the quarantined reliable buffer")]
        public async Task Should_Not_Replay_Quarantined_Watch_From_Nack_Or_TryDeliver()
        {
            var cancellation = TestContext.Current.CancellationToken;
            var (_, association, peer, context) = SeedUnacknowledgedWatch();
            var (pub, sub) = this.SourceProbe<IOutboundEnvelope>()
                .Via(Flow.FromGraph(Stage(context, association.SystemMessageDeliveryState)))
                .ToMaterialized(this.SinkProbe<IOutboundEnvelope>(), Keep.Both)
                .Run(ActorMaterializer.Create(Sys));
            var subscriber = await context.Subscriber.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellation);
            await sub.RequestAsync(1);
            (await sub.ExpectNextAsync(TimeSpan.FromSeconds(3), cancellation)).Message.Should().BeOfType<SystemMessageEnvelope>();
            await sub.RequestAsync(2);
            await pub.ExpectRequestAsync(cancellation);

            association.Quarantine(peer.Uid).Should().BeTrue();
            var nackObserved = context.ObserveNextRead();
            subscriber.ControlMessageReceived(peer.Uid, new SysNack(0L, peer));
            await nackObserved.WaitAsync(TimeSpan.FromSeconds(3), cancellation);
            var barrier = new OutboundEnvelope("after-nack", null, null);
            await pub.SendNextAsync(barrier);
            (await sub.ExpectNextAsync(TimeSpan.FromSeconds(3), cancellation)).Should().BeSameAs(barrier);
            await pub.SendCompleteAsync();
            await sub.ExpectCompleteAsync(cancellationToken: cancellation);
            association.SystemMessageDeliveryState.Buffer.Should().BeEmpty();
        }

        [Fact(DisplayName = "Reliable replay already held by handshake cannot reach encoding after quarantine")]
        public async Task Should_Filter_Reliable_Replay_Already_Held_Downstream_Of_Delivery_Stage()
        {
            var cancellation = TestContext.Current.CancellationToken;
            var (registry, association, peer, context) = SeedUnacknowledgedWatch();
            var passedDelivery = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Separate contexts share association state, but only the delivery context
            // observes callbacks. Handshake reads cannot satisfy the ordering barrier.
            var handshakeContext = new AssociationRegistryOutboundContext(registry, Local, Remote, _ => { });
            var handshake = new OutboundHandshakeStage(handshakeContext,
                TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), forceReqOnStart: true);
            var (pub, sub) = this.SourceProbe<IOutboundEnvelope>()
                .Via(Flow.FromGraph(Stage(context, association.SystemMessageDeliveryState)))
                .Select(envelope =>
                {
                    if (envelope.Message is SystemMessageEnvelope) passedDelivery.TrySetResult(true);
                    return envelope;
                })
                .Via(Flow.FromGraph(handshake))
                .Where(association.ShouldSendControl) // The production guard immediately before encode.
                .ToMaterialized(this.SinkProbe<IOutboundEnvelope>(), Keep.Both)
                .Run(ActorMaterializer.Create(Sys));
            await sub.RequestAsync(2);
            (await sub.ExpectNextAsync(TimeSpan.FromSeconds(3), cancellation)).Message.Should().BeOfType<HandshakeReq>();
            await passedDelivery.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellation);
            var subscriber = await context.Subscriber.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellation);
            var held = context.ObserveNextRead();
            subscriber.ControlMessageReceived(peer.Uid, new SysAck(0L, peer));
            await held.WaitAsync(TimeSpan.FromSeconds(3), cancellation);

            // The callback runs after the fused handshake OnPush has parked the Watch.
            // Clearing delivery's own queue now cannot retract that downstream envelope.
            association.Quarantine(peer.Uid).Should().BeTrue();
            registry.CompleteOutboundHandshake(Remote, peer);
            var barrier = new OutboundEnvelope("after-handshake", null, null);
            await pub.SendNextAsync(barrier);
            (await sub.ExpectNextAsync(TimeSpan.FromSeconds(3), cancellation)).Should().BeSameAs(barrier);
            await pub.SendCompleteAsync();
            await sub.ExpectCompleteAsync(cancellationToken: cancellation);
            association.SystemMessageDeliveryState.Buffer.Should().BeEmpty();
        }

        [Fact(DisplayName = "A stale UID quarantine and delayed old-incarnation Clear preserve the replacement peer's reliable state")]
        public async Task Should_Preserve_New_Incarnation_State_When_Old_UID_Is_Quarantined()
        {
            var cancellation = TestContext.Current.CancellationToken;
            var (registry, association, oldPeer, context) = SeedUnacknowledgedWatch();
            var oldIncarnation = association.CurrentState.Incarnation;
            registry.CompleteHandshake(Remote, new UniqueAddress(Remote, oldPeer.Uid + 1));
            // This seeded state now belongs to the replacement incarnation; no stage owns it yet.
            association.SystemMessageDeliveryState.CurrentIncarnation = association.CurrentState.Incarnation;
            association.Quarantine(oldPeer.Uid).Should().BeFalse();
            association.TryEnqueueControl(new OutboundEnvelope(new ClearSystemMessageDelivery(oldIncarnation), null, null)).Should().BeTrue();
            var barrier = new OutboundEnvelope("new-incarnation", null, null);
            association.TryEnqueueControl(barrier).Should().BeTrue();
            association.PrepareControlRestart();
            association.CompleteControlOutbound();
            var sink = ChannelSource.FromReader(association.ControlReader)
                .Via(Flow.FromGraph(Stage(context, association.SystemMessageDeliveryState)))
                .RunWith(this.SinkProbe<IOutboundEnvelope>(), ActorMaterializer.Create(Sys));
            await sink.RequestAsync(3);
            (await sink.ExpectNextAsync(TimeSpan.FromSeconds(3), cancellation)).Message.Should().BeOfType<SystemMessageEnvelope>();
            (await sink.ExpectNextAsync(TimeSpan.FromSeconds(3), cancellation)).Should().BeSameAs(barrier);
            await sink.ExpectCompleteAsync(cancellationToken: cancellation);
            association.SystemMessageDeliveryState.Buffer.Should().HaveCount(1);
            association.SystemMessageDeliveryState.NextSeq.Should().Be(2L);
        }
    }
}
