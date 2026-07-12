//-----------------------------------------------------------------------
// <copyright file="OutboundTestStageSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Remote.Artery;
using Akka.Remote.Transport;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using AssociationRegistry = Akka.Remote.Artery.AssociationRegistry;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Stage-level tests for <see cref="OutboundTestStage"/> (artery <c>advanced.test-mode</c>
    /// failure injection, port of Pekko's <c>OutboundTestStage</c>). Drives the stage directly
    /// with <c>TestSource</c>/<c>TestSink</c> probes -- no TCP, no real transport (mirrors
    /// <c>SystemMessageAckerStageSpec</c>'s harness).
    /// </summary>
    public class OutboundTestStageSpec : AkkaSpec
    {
        public OutboundTestStageSpec(ITestOutputHelper output) : base(output)
        {
        }

        private static readonly UniqueAddress Local = new(new Address("akka", "local-sys", "10.0.0.1", 2551), 111L);
        private static readonly Address Remote = new("akka", "remote-sys", "10.0.0.2", 2552);

        private static IOutboundEnvelope Envelope(string message) => new OutboundEnvelope(message, null, "akka://remote-sys@10.0.0.2:2552/user/target");

        private (SharedTestState State, TestPublisher.Probe<IOutboundEnvelope> Pub, TestSubscriber.Probe<IOutboundEnvelope> Sub) BuildHarness()
        {
            var state = new SharedTestState();
            var registry = new AssociationRegistry();
            var context = new AssociationRegistryOutboundContext(registry, Local, Remote, sendControl: static _ => { });
            var stage = new OutboundTestStage(context, state);

            var materializer = ActorMaterializer.Create(Sys);
            var (pub, sub) = this.SourceProbe<IOutboundEnvelope>()
                .ViaMaterialized(Flow.FromGraph(stage), Keep.Left)
                .ToMaterialized(this.SinkProbe<IOutboundEnvelope>(), Keep.Both)
                .Run(materializer);

            return (state, pub, sub);
        }

        [Fact(DisplayName = "no blackhole: outbound envelopes pass through unchanged")]
        public async Task Should_Pass_Through_Without_Blackhole()
        {
            var (_, pub, sub) = BuildHarness();

            await sub.RequestAsync(1);
            await pub.SendNextAsync(Envelope("m1"));

            var delivered = await sub.ExpectNextAsync(TimeSpan.FromSeconds(3));
            delivered.Message.Should().Be("m1");
        }

        [Fact(DisplayName = "blackhole (Send from local): outbound envelopes are dropped; PassThrough heals and later envelopes flow")]
        public async Task Should_Drop_While_Blackholed_And_Recover_On_PassThrough()
        {
            var (state, pub, sub) = BuildHarness();

            state.Blackhole(Local.Address, Remote, ThrottleTransportAdapter.Direction.Send);

            await sub.RequestAsync(2);
            await pub.SendNextAsync(Envelope("dropped"));
            await sub.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300));

            state.PassThrough(Local.Address, Remote, ThrottleTransportAdapter.Direction.Send);
            await pub.SendNextAsync(Envelope("after-heal"));

            // Stage processing is strictly sequential: seeing ONLY "after-heal" arrive proves the
            // blackholed envelope was dropped (not buffered/reordered).
            var delivered = await sub.ExpectNextAsync(TimeSpan.FromSeconds(3));
            delivered.Message.Should().Be("after-heal");
        }

        [Fact(DisplayName = "blackhole (Both): outbound envelopes are dropped at this node")]
        public async Task Should_Drop_For_Both_Direction()
        {
            var (state, pub, sub) = BuildHarness();

            state.Blackhole(Local.Address, Remote, ThrottleTransportAdapter.Direction.Both);

            await sub.RequestAsync(2);
            await pub.SendNextAsync(Envelope("dropped"));
            await sub.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300));

            state.PassThrough(Local.Address, Remote, ThrottleTransportAdapter.Direction.Both);
            await pub.SendNextAsync(Envelope("after-heal"));
            (await sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Message.Should().Be("after-heal");
        }

        [Fact(DisplayName = "blackhole (Receive-only at this node): outbound envelopes still pass -- documents the verbatim Pekko direction semantics")]
        public async Task Should_Not_Drop_For_Receive_Only()
        {
            var (state, pub, sub) = BuildHarness();

            // Receive on (local, remote) adds only (remote -> local), which the outbound stage's
            // (local, remote) check never matches -- verbatim Pekko TestStage semantics; do not
            // "fix" without diverging from the reference implementation.
            state.Blackhole(Local.Address, Remote, ThrottleTransportAdapter.Direction.Receive);

            await sub.RequestAsync(1);
            await pub.SendNextAsync(Envelope("passes"));
            (await sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Message.Should().Be("passes");
        }

        [Fact(DisplayName = "blackhole keyed to a DIFFERENT remote: envelopes to this stage's remote still pass")]
        public async Task Should_Scope_Drop_To_Blackholed_Remote_Only()
        {
            var (state, pub, sub) = BuildHarness();

            var otherRemote = new Address("akka", "other-sys", "10.0.0.3", 2553);
            state.Blackhole(Local.Address, otherRemote, ThrottleTransportAdapter.Direction.Both);

            await sub.RequestAsync(1);
            await pub.SendNextAsync(Envelope("passes"));
            (await sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Message.Should().Be("passes");
        }
    }
}
