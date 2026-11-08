// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Serialization;
using Akka.TestKit;
using Xunit;

namespace Akka.Remote.Tests.Serialization;

public sealed class MiscMessageMixedBindingSpec : AkkaSpec
{
    public MiscMessageMixedBindingSpec(ITestOutputHelper output) : base(output) { }

    [Theory(DisplayName = "Should_ExchangeFullBoundCorpusAndIdentifyReplies_When_ActualTcpWritersDiffer")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_ExchangeFullBoundCorpusAndIdentifyReplies_When_ActualTcpWritersDiffer(bool artery)
    {
        var transport = ConfigurationFactory.ParseString(artery ? """
            akka.actor.provider = remote
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            """ : """
            akka.actor.provider = remote
            akka.remote.dot-netty.tcp.hostname = "127.0.0.1"
            akka.remote.dot-netty.tcp.port = 0
            """);
        var enabled = ConfigurationFactory.ParseString("akka.actor.serialization-v2 = on").WithFallback(transport);
        var types = new RemoteSerializers().Create((ExtendedActorSystem)Sys).Single(row => row.Serializer.Identifier == 16).UseFor.ToArray();
        var pinned = MixedBindingPair.BindTo("akka-misc", types).WithFallback(enabled);
        await using var pair = MixedBindingPair.Create(artery ? "misc-artery" : "misc-classic", enabled, pinned);
        await Exchange(pair.V2Node, pair.LegacyNode, 56);
        await Exchange(pair.LegacyNode, pair.V2Node, 16);
    }

    private async Task Exchange(ActorSystem source, ActorSystem destination, int writerId)
    {
        var target = destination.ActorOf(Props.Create(() => new WrappedEcho()));
        var address = RARP.For(destination).Provider.DefaultAddress;
        var path = target.Path.ToSerializationFormatWithAddress(address);
        var probe = CreateTestProbe(source);

        // Exercise real Identify / ActorIdentity auto-receive behavior at the destination.
        source.ActorSelection(path).Tell(new Identify("wire-identify"), probe.Ref);
        var identity = await probe.ExpectMsgAsync<ActorIdentity>(TimeSpan.FromSeconds(10));
        Assert.Equal("wire-identify", identity.MessageId);
        Assert.NotNull(identity.Subject);
        Assert.Equal(target.Path.ToStringWithoutAddress(), identity.Subject.Path.ToStringWithoutAddress());
        Assert.Equal(address, identity.Subject.Path.Address);
        var remote = identity.Subject;

        foreach (var sample in MiscMessageMessagePackSerializerSpec.BuildCases((ExtendedActorSystem)source))
        {
            Assert.Equal(writerId, source.Serialization.FindSerializerFor(sample.Message).Identifier);
            // Harmful auto-receive controls (Kill / PoisonPill) stay inside a real Status.Success
            // envelope. Every nested protocol manifest crosses the actual socket both ways;
            // the target is not asked to execute a Kill while testing the other manifests.
            var wrapper = new Status.Success(sample.Message);
            Assert.Equal(writerId, source.Serialization.FindSerializerFor(wrapper).Identifier);
            remote.Tell(wrapper, probe.Ref);
            var returned = await probe.ExpectMsgAsync<Status.Success>(TimeSpan.FromSeconds(10));
            MiscMessageMessagePackSerializerSpec.AssertMessage(sample.Message, returned.Status);
        }
    }

    private sealed class WrappedEcho : ReceiveActor
    {
        public WrappedEcho()
        {
            Receive<Status.Success>(message => Sender.Tell(message));
        }
    }
}
