//-----------------------------------------------------------------------
// <copyright file="ThrottlerManagerLifecycleSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote.Transport;
using Akka.TestKit;
using FluentAssertions;
using Google.Protobuf;
using Xunit;

namespace Akka.Remote.Tests.Transport
{
    /// <summary>
    /// Specs for how <see cref="ThrottlerManager"/> manages the lifecycle of its
    /// <see cref="ThrottledAssociation"/> children.
    ///
    /// Uses a stub <see cref="Transport"/> so the throttler can be driven directly, without any
    /// real network I/O in the picture.
    /// </summary>
    public class ThrottlerManagerLifecycleSpec : AkkaSpec
    {
        private static readonly Config Config = ConfigurationFactory.ParseString(@"
            akka {
              loglevel = DEBUG
              actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
              remote.dot-netty.tcp.hostname = ""localhost""
              remote.dot-netty.tcp.port = 0
            }");

        public ThrottlerManagerLifecycleSpec(ITestOutputHelper output) : base(Config, output)
        {
        }

        private static readonly Address LocalAddress = new("akka.test", "local", "localhost", 1234);
        private static readonly Address RemoteAddress = new("akka.test", "remote", "localhost", 4321);

        #region Test transport

        private sealed class StubHandle : AssociationHandle
        {
            public StubHandle(Address localAddress, Address remoteAddress) : base(localAddress, remoteAddress)
            {
            }

            public bool Disassociated { get; private set; }

            public override bool Write(ByteString payload) => true;

#pragma warning disable CS0672 // the base member is obsolete, but it is the abstract one we have to implement
            public override void Disassociate()
#pragma warning restore CS0672
            {
                Disassociated = true;
            }
        }

        /// <summary>
        /// Blows the throttler up the way the real thing does when the layer above it faults.
        /// </summary>
        private sealed class ThrowingAssociationEventListener : IAssociationEventListener
        {
            public void Notify(IAssociationEvent ev) => throw new ApplicationException("boom");
        }

        private sealed class StubTransport : Akka.Remote.Transport.Transport
        {
            public StubTransport(ActorSystem system)
            {
                System = system;
                Config = ConfigurationFactory.Empty;
                SchemeIdentifier = "test";
                MaximumPayloadBytes = 32000;
            }

            public override Task<(Address, TaskCompletionSource<IAssociationEventListener>)> Listen()
                => Task.FromResult((LocalAddress, new TaskCompletionSource<IAssociationEventListener>()));

            public override bool IsResponsibleFor(Address remote) => true;

            public override Task<AssociationHandle> Associate(Address remoteAddress)
                => Task.FromResult<AssociationHandle>(new StubHandle(LocalAddress, remoteAddress));

            public override Task<bool> Shutdown() => Task.FromResult(true);
        }

        #endregion

        private IActorRef StartManager(IAssociationEventListener? associationListener = null)
        {
            var manager = Sys.ActorOf(Props.Create(() => new ThrottlerManager(new StubTransport(Sys))));

            // move the manager into its `Ready` behavior
            manager.Tell(new ListenUnderlying(LocalAddress,
                Task.FromResult(associationListener ?? new ActorAssociationEventListener(TestActor))));
            return manager;
        }

        private async Task<ThrottlerHandle> AssociateAsync(IActorRef manager)
        {
            var promise = new TaskCompletionSource<AssociationHandle>(TaskCreationOptions.RunContinuationsAsynchronously);
            manager.Tell(new AssociateUnderlying(RemoteAddress, promise));
            var handle = await promise.Task.WaitAsync(RemainingOrDefault);
            return (ThrottlerHandle)handle;
        }

        [Fact(DisplayName = "ThrottlerManager should stop, not restart, an inbound throttler child that fails")]
        public async Task Should_stop_failed_inbound_throttler_child()
        {
            var manager = StartManager(new ThrowingAssociationEventListener());

            var inboundHandle = new StubHandle(LocalAddress, RemoteAddress);
            manager.Tell(new InboundAssociation(inboundHandle));

            // the throttler registers itself as the read handler as soon as it gets its `Handle` message
            var listener = (ActorHandleEventListener)await inboundHandle.ReadHandlerSource.Task.WaitAsync(RemainingOrDefault);
            var throttler = listener.Actor;
            await WatchAsync(throttler);

            // an ASSOCIATE PDU carries the origin address, so the throttler checks in with the manager,
            // gets its throttle mode back, and hands the association up to the (throwing) listener
            var associate = new AkkaPduProtobuffCodec(Sys).ConstructAssociate(new HandshakeInfo(RemoteAddress, 1));

            // the default decider Restarts on this, which is what makes the association a black hole:
            // the restarted FSM re-enters `WaitExposedHandle`, and the `Handle` message that gets it out
            // of there is only ever sent once, when the child is created
            await EventFilter.Exception<ApplicationException>().ExpectOneAsync(async () =>
            {
                listener.Notify(new InboundPayload(associate));
                await ExpectTerminatedAsync(throttler);
            });
        }

        [Fact(DisplayName = "ThrottlerManager should purge terminated throttlers from its handle table")]
        public async Task Should_purge_terminated_throttler_from_handle_table()
        {
            var manager = StartManager();
            var handle = await AssociateAsync(manager);
            var throttler = handle.ThrottlerActor;

            await WatchAsync(throttler);
            throttler.Tell(PoisonPill.Instance);
            await ExpectTerminatedAsync(throttler);

            // the death watch notification is a system message enqueued before anything we send from
            // here, so the manager has already processed it by the time it handles `SetThrottle`
            var deadLetters = CreateTestProbe("dead-letters");
            Sys.EventStream.Subscribe(deadLetters.Ref, typeof(DeadLetter));

            manager.Tell(new SetThrottle(RemoteAddress, ThrottleTransportAdapter.Direction.Both, Blackhole.Instance),
                TestActor);

            await ExpectMsgAsync<SetThrottleAck>();

            // a stale handle table entry shows up as the throttle mode dead-lettering to the dead child
            await deadLetters.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300));
        }

        [Fact(DisplayName = "ThrottledAssociation should disassociate rather than black hole payloads received before initialization")]
        public async Task Should_disassociate_when_payload_arrives_before_handle()
        {
            // reproduces the state a restarted inbound throttler is left in: back in `WaitExposedHandle`,
            // with the read handler of the previous incarnation still pointed at this actor
            var originalHandle = new StubHandle(LocalAddress, RemoteAddress);
            var association = Sys.ActorOf(Props.Create(() => new ThrottledAssociation(
                TestActor,
                new ActorAssociationEventListener(TestActor),
                originalHandle,
                true)));

            await WatchAsync(association);

            await EventFilter.Warning(contains: "received an InboundPayload before it was initialized").ExpectOneAsync(
                async () =>
                {
                    association.Tell(new InboundPayload(ByteString.CopyFromUtf8("ping")));
                    await ExpectTerminatedAsync(association);
                });

            originalHandle.Disassociated.Should().BeTrue("the association has to be torn down so remoting can re-establish it");
        }
    }
}
