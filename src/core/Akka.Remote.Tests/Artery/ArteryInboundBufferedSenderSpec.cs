//-----------------------------------------------------------------------
// <copyright file="ArteryInboundBufferedSenderSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Reflection;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Artery;
using Akka.TestKit;
using Akka.TestKit.Configs;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// A message buffered for a remote-deployed recipient that does not exist yet must have its sender
    /// resolved when the message is delivered, not when it is buffered: the sender can be a remote-deployed
    /// actor whose creation is processed in the meantime. The scheduler is virtual so the retry that
    /// flushes the buffer is driven explicitly; it lives in its own spec class for that reason.
    /// </summary>
    public sealed class ArteryInboundBufferedSenderSpec : AkkaSpec
    {
        private static Config ArteryConfig() => ConfigurationFactory.ParseString("""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            akka.remote.artery.advanced.inbound-lanes = 1
            """);

        public ArteryInboundBufferedSenderSpec(ITestOutputHelper output)
            : base(TestConfigs.TestSchedulerConfig.WithFallback(ArteryConfig()), output)
        {
        }

        [Fact(DisplayName =
            "Should_ResolveBufferedSender_When_RemoteDeploymentAppearsBeforeFlush")]
        public async Task Should_ResolveBufferedSender_When_RemoteDeploymentAppearsBeforeFlush()
        {
            var scheduler = Assert.IsType<TestScheduler>(Sys.Scheduler);
            var provider = (RemoteActorRefProvider)((ExtendedActorSystem)Sys).Provider;
            var daemon = (RemoteSystemDaemon)provider.RemoteDaemon;
            var senderProbe = CreateTestProbe();
            var recipientProbe = CreateTestProbe();

            const string senderKey = "akka/origin@127.0.0.1:2552/user/a";
            const string recipientKey = "akka/origin@127.0.0.1:2552/user/b";
            var senderPath = $"{provider.DefaultAddress}/remote/{senderKey}";
            var recipientPath = $"{provider.DefaultAddress}/remote/{recipientKey}";

            var transport = provider.Transport;
            var method = transport.GetType().GetMethod(
                "DispatchInbound",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(IInboundEnvelope) },
                modifiers: null)!;
            var dispatch = (Action<IInboundEnvelope>)Delegate.CreateDelegate(
                typeof(Action<IInboundEnvelope>), transport, method);

            try
            {
                // Ordinary traffic wins the race against both creates.
                using var frame = ArteryEnvelopeCodec.Encode(Sys.Serialization, 42L, senderPath, recipientPath, "ping");
                var decoded = ArteryEnvelopeCodec.Decode(new ReadOnlySequence<byte>(frame.WrittenMemory.Slice(4).ToArray()));
                dispatch(new InboundEnvelope("ping", null, null, 42L, 1, "S") { Frame = decoded });

                // Model processing both creates before the first recipient retry.
                daemon.AddChild(senderKey, (IInternalActorRef)senderProbe.Ref);
                daemon.AddChild(recipientKey, (IInternalActorRef)recipientProbe.Ref);
                scheduler.Advance(TimeSpan.FromMilliseconds(50));

                await recipientProbe.ExpectMsgAsync("ping", TimeSpan.FromSeconds(3));
                recipientProbe.LastSender.Tell("reply", recipientProbe.Ref);
                await senderProbe.ExpectMsgAsync("reply", TimeSpan.FromSeconds(3));
            }
            finally
            {
                daemon.RemoveChild(senderKey);
                daemon.RemoveChild(recipientKey);
                Sys.Stop(senderProbe.Ref);
                Sys.Stop(recipientProbe.Ref);
            }
        }
    }
}
