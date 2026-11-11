// -----------------------------------------------------------------------
// <copyright file="ArteryQuicStreamsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Remote.Artery;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using Xunit;
using static Akka.Remote.Tests.Artery.ArteryTlsTransportSpec;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>Real actor ordering across independently configured processing lanes and QUIC streams.</summary>
    public sealed class ArteryQuicStreamsSpec : AkkaSpec
    {
        public ArteryQuicStreamsSpec(ITestOutputHelper output) : base(output) { }

        private sealed class Echo : ReceiveActor
        {
            private IActorRef? _localTarget;
            public Echo()
            {
                Receive<int>(number => (_localTarget ?? Sender).Tell(number));
                Receive<IActorRef>(target =>
                {
                    _localTarget = target;
                    Sender.Tell("ready");
                });
            }
        }

        private static ActorSystemSetup Setup(int lanes, int streams, ArteryTlsSettings tls) => ActorSystemSetup.Create(
            BootstrapSetup.Create().WithConfig(ConfigurationFactory.ParseString($$"""
                akka.actor.provider = remote
                akka.loglevel = DEBUG
                akka.remote.artery {
                    enabled = on
                    transport = quic
                    canonical.hostname = "127.0.0.1"
                    canonical.port = 0
                    advanced {
                        inbound-lanes = 1
                        outbound-lanes = {{lanes}}
                        outbound-message-queue-size = 1024
                        flush-wait-on-shutdown = 10s
                        quic.ordinary-streams = {{streams}}
                    }
                }
                """)), new ArteryTlsSetup(tls));

        [Theory(DisplayName = "Independent QUIC streams preserve numbered actor traffic and reliable control across lane groups")]
        [InlineData(1, 1)]
        [InlineData(2, 1)]
        [InlineData(2, 2)]
        [InlineData(4, 1)]
        [InlineData(4, 2)]
        [InlineData(5, 3)]
        [InlineData(4, 4)]
        [InlineData(8, 1)]
        [InlineData(8, 2)]
        [InlineData(8, 4)]
        [InlineData(8, 8)]
        public async Task Should_PreserveOrderAndControl_When_UsingIndependentOrdinaryStreams(int lanes, int streams)
        {
            RequireTransportSupport("quic");
            using var certificate = CreateCertificate();
            var tls = ArteryTlsSettings.Mutual(certificate, TrustCertificate(certificate));
            ActorSystem? first = null;
            ActorSystem? second = null;
            try
            {
                first = ActorSystem.Create("QuicLanesA", Setup(lanes, streams, tls));
                second = ActorSystem.Create("QuicLanesB", Setup(lanes, streams, tls));
                InitializeLogger(first, "[A] ");
                InitializeLogger(second, "[B] ");
                var address = RARP.For(second).Provider.DefaultAddress;
                var recipients = new Dictionary<int, IActorRef>();
                var localRecipients = new Dictionary<int, IActorRef>();
                for (var i = 0; i < 256 && recipients.Count < lanes; i++)
                {
                    var name = $"echo-{i}";
                    var local = second.ActorOf(Props.Create(() => new Echo()), name);
                    var remote = await first.ActorSelection($"{address}/user/{name}")
                        .ResolveOne(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                    var lane = Association.SelectLane(remote.Path.Uid, lanes);
                    if (recipients.TryAdd(lane, remote))
                        localRecipients[lane] = local;
                }
                recipients.Should().HaveCount(lanes, "each processing lane must carry real traffic");
                var remoting = (ArteryRemoting)RARP.For(first).Provider.Transport;
                using var ready = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await remoting.QuicOrdinaryStreamsReadyAsync(ready.Token);
                remoting.QuicOrdinaryStreamCounts().Values.Should().ContainSingle().Which.Should().Be(streams);

                var probes = recipients.Keys.ToDictionary(lane => lane, _ => CreateTestProbe(first));
                const int messages = 500;
                for (var sequence = 0; sequence < messages; sequence++)
                    foreach (var (lane, recipient) in recipients)
                        recipient.Tell(sequence, probes[lane].Ref);
                await Task.WhenAll(probes.Values.Select(async probe =>
                {
                    for (var sequence = 0; sequence < messages; sequence++)
                        await probe.ExpectMsgAsync(sequence, TimeSpan.FromSeconds(10));
                    await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                }));

                var watch = CreateTestProbe(first);
                await watch.WatchAsync(recipients[0]);
                recipients[0].Tell(PoisonPill.Instance);
                await watch.ExpectTerminatedAsync(recipients[0], TimeSpan.FromSeconds(10));

                // Keep one group idle while the other groups still have queued data. Replies
                // stay local to the receiver, so they can witness the sender's transport drain.
                var drainRecipients = recipients.Where(pair => pair.Key != 0).ToDictionary();
                if (lanes == 1)
                {
                    // The only original recipient was stopped to check reliable control.
                    // Use a live recipient so the single-lane arm also checks ordinary drain.
                    localRecipients[0] = second.ActorOf(Props.Create(() => new Echo()), "drain-echo");
                    drainRecipients[0] = await first.ActorSelection($"{address}/user/drain-echo")
                        .ResolveOne(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                }
                var received = drainRecipients.Keys.ToDictionary(lane => lane, _ => CreateTestProbe(second));
                foreach (var (lane, probe) in received)
                {
                    localRecipients[lane].Tell(probe.Ref, probe.Ref);
                    await probe.ExpectMsgAsync("ready", TimeSpan.FromSeconds(5));
                }
                for (var sequence = 0; sequence < messages; sequence++)
                    foreach (var lane in received.Keys)
                        drainRecipients[lane].Tell(sequence, ActorRefs.NoSender);
                await remoting.Shutdown().AwaitWithTimeout(TimeSpan.FromSeconds(5));
                await Task.WhenAll(received.Values.Select(async probe =>
                {
                    for (var sequence = 0; sequence < messages; sequence++)
                        await probe.ExpectMsgAsync(sequence, TimeSpan.FromSeconds(10));
                    await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                }));
            }
            finally
            {
                await Task.WhenAll(new[] { first, second }.Where(system => system is not null)
                    .Select(system => system!.Terminate().AwaitWithTimeout(TimeSpan.FromSeconds(15))));
            }
        }
    }
}
