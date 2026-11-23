// -----------------------------------------------------------------------
// <copyright file="ArteryInboundMalformedSelectionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch.SysMsg;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// The remote daemon handles inbound messages synchronously, which with one inbound lane is the connection's
    /// own sink: a throw there would fail the stream. A malformed child name is treated as an unknown child.
    /// </summary>
    public abstract class ArteryInboundMalformedSelectionSpecBase : AkkaSpec
    {
        private readonly int _lanes;

        private static Config ArteryConfig(int lanes) => ConfigurationFactory.ParseString($$"""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            akka.remote.artery.advanced.inbound-lanes = {{lanes}}
            """);

        protected ArteryInboundMalformedSelectionSpecBase(int lanes, ITestOutputHelper output)
            : base(ArteryConfig(lanes), output)
        {
            _lanes = lanes;
        }

        private sealed class Forward : ReceiveActor
        {
            public Forward(IActorRef target) => ReceiveAny(msg => target.Tell(msg));
        }

        [Fact(DisplayName = "Should_KeepConnectionAndDeliverNextMessage_When_DaemonSelectionNameIsMalformed")]
        public async Task Should_KeepConnectionAndDeliverNextMessage_When_DaemonSelectionNameIsMalformed()
        {
            var sender = ActorSystem.Create("MalformedSelectionSender", ArteryConfig(_lanes));
            try
            {
                var address = RARP.For(Sys).Provider.DefaultAddress;
                Sys.ActorOf(Props.Create(() => new Forward(TestActor)), "forward");
                var forward = await sender.ActorSelection($"{address}/user/forward").ResolveOne(10.Seconds());
                forward.Tell("warmup");
                await ExpectMsgAsync("warmup", 10.Seconds());

                // Anchored at the receiver's /remote, so the selection resolves to the daemon.
                var provider = (RemoteActorRefProvider)((ExtendedActorSystem)sender).Provider;
                var daemon = new RemoteActorRef(provider.Transport, RARP.For(sender).Provider.DefaultAddress,
                    new RootActorPath(address) / "remote", ActorRefs.Nobody, Props.None, Deploy.None);
                new ActorSelection(daemon, new SelectionPathElement[] { new SelectChildName("bad#uid") }).Tell("bad");

                forward.Tell("after");
                await ExpectMsgAsync("after", 5.Seconds());
            }
            finally
            {
                await sender.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }

        [Fact(DisplayName = "Should_NotThrowFromDaemon_When_DaemonMsgCreatePathIsMalformed")]
        public void Should_NotThrowFromDaemon_When_DaemonMsgCreatePathIsMalformed()
        {
            var daemon = ((RemoteActorRefProvider)((ExtendedActorSystem)Sys).Provider).RemoteDaemon;
            var address = RARP.For(Sys).Provider.DefaultAddress;
            var create = new DaemonMsgCreate(Props.Create<Forward>(TestActor), Deploy.None,
                $"{address}/remote/akka/bad#uid/user/target", daemon);

            Record.Exception(() => daemon.Tell(create, TestActor)).Should().BeNull();
        }
    }

    /// <summary>One inbound lane (the default): dispatch runs inline in the connection's sink.</summary>
    public sealed class ArteryInboundMalformedSelectionInlineSpec : ArteryInboundMalformedSelectionSpecBase
    {
        public ArteryInboundMalformedSelectionInlineSpec(ITestOutputHelper output) : base(1, output) { }
    }

    /// <summary>Several inbound lanes: dispatch runs on a lane consumer loop.</summary>
    public sealed class ArteryInboundMalformedSelectionLanesSpec : ArteryInboundMalformedSelectionSpecBase
    {
        public ArteryInboundMalformedSelectionLanesSpec(ITestOutputHelper output) : base(2, output) { }
    }
}
