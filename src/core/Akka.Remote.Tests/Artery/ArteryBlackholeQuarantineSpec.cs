//-----------------------------------------------------------------------
// <copyright file="ArteryBlackholeQuarantineSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Artery;
using Akka.Remote.Transport;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// In-process replica of the failure-injection scenario
    /// <c>SurviveNetworkInstabilitySpec.must_mark_quarantined_node_with_reachability_status_Terminated</c>
    /// exercises at MNTR scale: DeathWatch of remote actors across a BLACKHOLED (test-mode) link
    /// floods undeliverable <c>Watch</c> system messages, which must overflow the (deliberately
    /// tiny here) unacknowledged system-message buffer and QUARANTINE the association, publishing
    /// <see cref="QuarantinedEvent"/>. Covers the full chain: RemoteWatcher's own
    /// <c>Context.Watch</c> -&gt; wire <c>Watch</c> system message -&gt; control queue -&gt;
    /// <c>SystemMessageDeliveryStage</c> buffer -&gt; overflow -&gt; give-up -&gt; quarantine.
    /// </summary>
    public class ArteryBlackholeQuarantineSpec : AkkaSpec
    {
        private const int BufferSize = 4;

        private static readonly Config QuarantineConfig = ConfigurationFactory.ParseString($"""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            akka.remote.artery.advanced.test-mode = on
            akka.remote.artery.advanced.system-message-buffer-size = {BufferSize}
            """);

        public ArteryBlackholeQuarantineSpec(ITestOutputHelper output) : base(QuarantineConfig, output)
        {
        }

        private sealed class Target : ReceiveActor
        {
            public Target()
            {
                ReceiveAny(msg => Sender.Tell(msg));
            }
        }

        private sealed class Watcher : ReceiveActor
        {
            public Watcher()
            {
                Receive<IReadOnlyList<IActorRef>>(targets =>
                {
                    foreach (var target in targets)
                        Context.Watch(target);
                    Sender.Tell("watching");
                });
            }
        }

        [Fact(DisplayName = "DeathWatch flood across a blackholed link overflows the system-message buffer and quarantines the association (QuarantinedEvent published)")]
        public async Task Should_Quarantine_On_SystemMessage_Overflow_Under_Blackhole()
        {
            var remoteSys = ActorSystem.Create("quarantine-remote", QuarantineConfig);
            try
            {
                var remoteAddress = RARP.For(remoteSys).Provider.DefaultAddress;

                // BufferSize + 2 distinct remote targets -> BufferSize + 2 distinct wire Watch
                // system messages once the watcher watches them all.
                var targetCount = BufferSize + 2;
                for (var i = 0; i < targetCount; i++)
                    remoteSys.ActorOf(Props.Create(() => new Target()), $"target-{i}");

                // Resolve real RemoteActorRefs for every target BEFORE the blackhole.
                var targets = new List<IActorRef>();
                for (var i = 0; i < targetCount; i++)
                {
                    var selection = Sys.ActorSelection($"akka://{remoteSys.Name}@127.0.0.1:{remoteAddress.Port}/user/target-{i}");
                    var identity = await selection.Ask<ActorIdentity>(new Identify(i), TimeSpan.FromSeconds(10));
                    identity.Subject.Should().NotBeNull();
                    targets.Add(identity.Subject!);
                }

                Sys.EventStream.Subscribe(TestActor, typeof(QuarantinedEvent));

                // Sever the link (both directions at this node), exactly as the TestConductor would.
                var transport = (ArteryRemoting)RARP.For(Sys).Provider.Transport;
                (await transport.ManagementCommand(new SetThrottle(remoteAddress, ThrottleTransportAdapter.Direction.Both, Blackhole.Instance)))
                    .Should().BeTrue();

                // Watch them all: undeliverable Watch system messages must overflow the
                // (BufferSize-capacity) unacknowledged buffer and trigger give-up -> quarantine.
                var watcher = Sys.ActorOf(Props.Create(() => new Watcher()), "watcher");
                watcher.Tell(targets);
                await ExpectMsgAsync("watching", TimeSpan.FromSeconds(5));

                var quarantined = await ExpectMsgAsync<QuarantinedEvent>(TimeSpan.FromSeconds(15));
                quarantined.Address.Should().Be(remoteAddress);
            }
            finally
            {
                await remoteSys.Terminate().AwaitWithTimeout(TimeSpan.FromSeconds(10));
            }
        }
    }
}
