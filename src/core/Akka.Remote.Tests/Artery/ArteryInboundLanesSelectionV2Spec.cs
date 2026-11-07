// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Artery;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Artery;

public partial class ArteryInboundLanesSpec
{
    private static Config SelectionConfig(bool v2, bool artery, int lanes)
        => ConfigurationFactory.ParseString($$"""
            akka.actor.serialization-v2 = {{(v2 ? "on" : "off")}}
            akka.actor.provider = remote
            akka.remote.dot-netty.tcp.hostname = "127.0.0.1"
            akka.remote.dot-netty.tcp.port = 0
            """).WithFallback(artery ? ArteryConfig(lanes) : ConfigurationFactory.ParseString("akka.remote.artery.enabled = off"));

    private static string SelectionPath(ActorSystem system, string path)
        => $"{RARP.For(system).Provider.DefaultAddress}/user/{path}";

    [Theory(DisplayName = "Should_PreserveSelectionTrafficAndOrder_When_RealNodesUseOpposingBindings")]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(true, 4)]
    public async Task Should_PreserveSelectionTrafficAndOrder_When_RealNodesUseOpposingBindings(bool artery, int lanes)
    {
        var first = ActorSystem.Create("SelectionV2First", SelectionConfig(true, artery, lanes));
        var second = ActorSystem.Create("SelectionV2Second", SelectionConfig(false, artery, lanes));
        try
        {
            Assert.Equal(46, first.Serialization.FindSerializerForType(typeof(ActorSelectionMessage)).Identifier);
            Assert.Equal(6, second.Serialization.FindSerializerForType(typeof(ActorSelectionMessage)).Identifier);
            first.ActorOf(Props.Create(() => new Echo()), "echo-one");
            first.ActorOf(Props.Create(() => new Echo()), "folder");
            second.ActorOf(Props.Create(() => new Echo()), "echo-one");
            second.ActorOf(Props.Create(() => new Echo()), "folder");
            var firstProbe = CreateTestProbe(first);
            var secondProbe = CreateTestProbe(second);
            foreach (var path in new[] { "echo-one", "folder/../echo-one", "echo-*" })
            {
                var toSecond = first.ActorSelection(SelectionPath(second, path));
                var toFirst = second.ActorSelection(SelectionPath(first, path));
                for (var index = 0; index < 32; index++)
                {
                    toSecond.Tell(index, firstProbe.Ref);
                    toFirst.Tell(index, secondProbe.Ref);
                }
                for (var index = 0; index < 32; index++)
                {
                    await firstProbe.ExpectMsgAsync(index, TimeSpan.FromSeconds(10));
                    await secondProbe.ExpectMsgAsync(index, TimeSpan.FromSeconds(10));
                }
            }
        }
        finally
        {
            await Task.WhenAll(first.Terminate(), second.Terminate()).WaitAsync(TimeSpan.FromSeconds(20));
        }
    }

    [Theory(DisplayName = "Should_KeepAnotherSelectionTargetProgressing_When_AnOpposingFormatBlocksPayloadDeserialization")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_KeepAnotherSelectionTargetProgressing_When_AnOpposingFormatBlocksPayloadDeserialization(bool senderV2)
    {
        var gateKey = Guid.NewGuid().ToString("N");
        Config ConfigFor(bool enabled) => ConfigurationFactory.ParseString($"akka.actor.serialization-v2 = {(enabled ? "on" : "off")}")
            .WithFallback(GatedConfig(inboundLanes: 4, inboundLaneBufferSize: 32));
        var sender = ActorSystem.Create("SelectionLaneSender", ConfigFor(senderV2));
        var receiver = ActorSystem.Create("SelectionLaneReceiver", ConfigFor(!senderV2));
        try
        {
            Assert.Equal(senderV2 ? 46 : 6, sender.Serialization.FindSerializerForType(typeof(ActorSelectionMessage)).Identifier);
            Assert.Equal(senderV2 ? 6 : 46, receiver.Serialization.FindSerializerForType(typeof(ActorSelectionMessage)).Identifier);
            var gated = receiver.ActorOf(Props.Create(() => new OrderRecorder()), "gated");
            receiver.ActorOf(Props.Create(() => new Echo()), "plain");
            var reply = CreateTestProbe(sender);
            // The stable selection keys /user/gated and /user/plain hash to distinct lanes (3/2).
            // The wire recipient anchor is identical; routing by the anchor collapses both.
            sender.ActorSelection(SelectionPath(receiver, "gated")).Tell(new GatedInt(gateKey, 0), reply.Ref);
            await AwaitAssertAsync(() => GateBlockingSerializer.HasEntered(gateKey).Should().BeTrue(), TimeSpan.FromSeconds(10));
            sender.ActorSelection(SelectionPath(receiver, "plain")).Tell("other-target-progress", reply.Ref);
            await reply.ExpectMsgAsync("other-target-progress", TimeSpan.FromSeconds(10));
            GateBlockingSerializer.OpenGate(gateKey);
            var historyProbe = CreateTestProbe(receiver);
            await AwaitAssertAsync(async () =>
            {
                gated.Tell(new GetHistory(), historyProbe.Ref);
                var history = await historyProbe.ExpectMsgAsync<History>(TimeSpan.FromSeconds(5));
                history.Values.Should().Equal(Enumerable.Range(0, 1));
            }, TimeSpan.FromSeconds(10));
        }
        finally
        {
            GateBlockingSerializer.OpenGate(gateKey);
            await Task.WhenAll(sender.Terminate(), receiver.Terminate()).WaitAsync(TimeSpan.FromSeconds(20));
        }
    }
}
