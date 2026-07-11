//-----------------------------------------------------------------------
// <copyright file="ArteryLargeMessageStreamSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Artery;
using Akka.Remote.Configuration;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Covers Artery TCP remoting task 10.2, "Large Message Stream"
    /// (<c>openspec/changes/artery-tcp-remoting/tasks.md</c>): parsing/validation of the new
    /// <c>large-message-destinations</c>/<c>maximum-large-frame-size</c>/<c>large-buffer-pool-size</c>/
    /// <c>outbound-large-message-queue-size</c> settings, the Pekko-faithful destination matcher
    /// (<see cref="Akka.Util.WildcardIndex{T}"/>, reused from <see cref="Akka.Actor.Deployer"/> --
    /// not reimplemented), the default-off gate (task 10.2's "gate L": no large channel/connection
    /// is ever materialized unless <c>large-message-destinations</c> is configured), and an
    /// end-to-end loopback proving delivery of a payload larger than the ordinary
    /// <c>maximum-frame-size</c>.
    ///
    /// <para>
    /// <b>Maintainer policy, honored throughout this file:</b> no wall-clock thresholds. Every
    /// assertion is progress/order/completion/bounded-state, matching the sibling
    /// <see cref="ArteryBackpressureSpec"/>/<see cref="ArteryUnwatchShutdownRaceSpec"/> files.
    /// </para>
    /// </summary>
    public class ArteryLargeMessageStreamSpec : AkkaSpec
    {
        public ArteryLargeMessageStreamSpec(ITestOutputHelper output) : base(output)
        {
        }

        private static Config ArteryConfig() => ConfigurationFactory.ParseString("""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            """);

        private static int BoundPort(ActorSystem system) => RARP.For(system).Provider.DefaultAddress.Port!.Value;

        private static string SelectionPath(ActorSystem system, string localName) =>
            $"akka://{system.Name}@127.0.0.1:{BoundPort(system)}/user/{localName}";

        private static Address RemoteAddressOf(ActorSystem system) =>
            new("akka", system.Name, "127.0.0.1", BoundPort(system));

        private sealed class Echo : ReceiveActor
        {
            public Echo()
            {
                ReceiveAny(msg => Sender.Tell(msg));
            }
        }

        /// <summary>
        /// Replies with a SMALL acknowledgement carrying the received payload's length, rather
        /// than echoing the (large) payload back -- deliberately keeps the reply on the ORDINARY
        /// stream without needing <c>large-message-destinations</c> configured for the reply's own
        /// direction (B -&gt; A), while still proving the full large payload arrived intact.
        /// </summary>
        private sealed class LargeMessageReceiver : ReceiveActor
        {
            public LargeMessageReceiver()
            {
                Receive<string>(s => Sender.Tell($"received:{s.Length}"));
            }
        }

        private sealed class PlainWatcher : ReceiveActor
        {
            private readonly IActorRef _replyTo;

            public PlainWatcher(IActorRef target, IActorRef replyTo)
            {
                _replyTo = replyTo;
                Context.Watch(target);
                Receive<Terminated>(_ => _replyTo.Tell("terminated"));
            }
        }

        // ----------------------------------------------------------------------------------
        // L.1 -- settings parsing + validation
        // ----------------------------------------------------------------------------------

        [Fact(DisplayName = "Should_ParseLargeMessageSettingsDefaults_When_ConfigNotOverridden")]
        public void Should_ParseLargeMessageSettingsDefaults_When_ConfigNotOverridden()
        {
            var arteryConfig = RemoteConfigFactory.Default().GetConfig("akka.remote.artery");
            var settings = new ArterySettings(arteryConfig);

            settings.LargeMessageDestinations.IsEmpty.Should().BeTrue();
            settings.LargeMessageChannelEnabled.Should().BeFalse();
            settings.MaximumLargeFrameSize.Should().Be(2 * 1024 * 1024);
            settings.LargeBufferPoolSize.Should().Be(32);
            settings.OutboundLargeMessageQueueSize.Should().Be(256);
        }

        [Fact(DisplayName = "Should_EnableLargeMessageChannel_When_DestinationsConfigured")]
        public void Should_EnableLargeMessageChannel_When_DestinationsConfigured()
        {
            var arteryConfig = ConfigurationFactory.ParseString("""
                akka.remote.artery.large-message-destinations = ["/user/big-actor"]
                """).WithFallback(RemoteConfigFactory.Default()).GetConfig("akka.remote.artery");

            var settings = new ArterySettings(arteryConfig);

            settings.LargeMessageChannelEnabled.Should().BeTrue();
            settings.LargeMessageDestinations.IsEmpty.Should().BeFalse();
        }

        [Fact(DisplayName = "Should_ParseLargeMessageSettingOverrides_When_ConfigOverridden")]
        public void Should_ParseLargeMessageSettingOverrides_When_ConfigOverridden()
        {
            var arteryConfig = ConfigurationFactory.ParseString("""
                akka.remote.artery.advanced.maximum-large-frame-size = 512k
                akka.remote.artery.advanced.large-buffer-pool-size = 8
                akka.remote.artery.advanced.outbound-large-message-queue-size = 64
                """).WithFallback(RemoteConfigFactory.Default()).GetConfig("akka.remote.artery");

            var settings = new ArterySettings(arteryConfig);

            settings.MaximumLargeFrameSize.Should().Be(512 * 1024);
            settings.LargeBufferPoolSize.Should().Be(8);
            settings.OutboundLargeMessageQueueSize.Should().Be(64);
        }

        [Theory(DisplayName = "Should_ThrowConfigurationException_When_LargeMessageSettingIsInvalid")]
        [InlineData("akka.remote.artery.advanced.maximum-large-frame-size = 0b")]
        [InlineData("akka.remote.artery.advanced.maximum-large-frame-size = 20000000b")] // > 0x00FFFFFF (16 MiB - 1)
        [InlineData("akka.remote.artery.advanced.large-buffer-pool-size = 0")]
        [InlineData("akka.remote.artery.advanced.outbound-large-message-queue-size = 0")]
        public void Should_ThrowConfigurationException_When_LargeMessageSettingIsInvalid(string overrideHocon)
        {
            var arteryConfig = ConfigurationFactory.ParseString(overrideHocon)
                .WithFallback(RemoteConfigFactory.Default())
                .GetConfig("akka.remote.artery");

            Assert.Throws<ConfigurationException>(() => new ArterySettings(arteryConfig));
        }

        [Fact(DisplayName = "Should_ThrowConfigurationException_When_MaximumLargeFrameSizeIsSmallerThanMaximumFrameSize")]
        public void Should_ThrowConfigurationException_When_MaximumLargeFrameSizeIsSmallerThanMaximumFrameSize()
        {
            var arteryConfig = ConfigurationFactory.ParseString("""
                    akka.remote.artery.advanced.maximum-frame-size = 512k
                    akka.remote.artery.advanced.maximum-large-frame-size = 256k
                    """)
                .WithFallback(RemoteConfigFactory.Default())
                .GetConfig("akka.remote.artery");

            Assert.Throws<ConfigurationException>(() => new ArterySettings(arteryConfig));
        }

        // ----------------------------------------------------------------------------------
        // L.4 (destination matching) -- exact path, single "*" wildcard, trailing "**" wildcard,
        // non-match. Pure function tests against the parsed WildcardIndex -- no ActorSystem/
        // networking needed.
        // ----------------------------------------------------------------------------------

        private static ArterySettings SettingsWithDestinations(params string[] destinations)
        {
            var quoted = string.Join(", ", destinations.Select(d => $"\"{d}\""));
            var arteryConfig = ConfigurationFactory.ParseString(
                $"akka.remote.artery.large-message-destinations = [{quoted}]"
            ).WithFallback(RemoteConfigFactory.Default()).GetConfig("akka.remote.artery");

            return new ArterySettings(arteryConfig);
        }

        [Fact(DisplayName = "Destination matcher: an exact path matches only that exact path, not a shorter or longer one")]
        public void Should_Match_Exact_Path_Only()
        {
            var settings = SettingsWithDestinations("/user/supervisor/actor");

            settings.LargeMessageDestinations.Find(new[] { "user", "supervisor", "actor" }).Should().NotBeNull();
            settings.LargeMessageDestinations.Find(new[] { "user", "supervisor" }).Should().BeNull("too short -- not the configured path");
            settings.LargeMessageDestinations.Find(new[] { "user", "supervisor", "actor", "child" }).Should().BeNull("too long -- not the configured path");
            settings.LargeMessageDestinations.Find(new[] { "user", "other" }).Should().BeNull("a completely different path");
        }

        [Fact(DisplayName = "Destination matcher: a single \"*\" segment matches any one name at that position, Pekko-style")]
        public void Should_Match_Single_Wildcard_Segment()
        {
            var settings = SettingsWithDestinations("/user/supervisor/actor/*");

            settings.LargeMessageDestinations.Find(new[] { "user", "supervisor", "actor", "child-a" }).Should().NotBeNull();
            settings.LargeMessageDestinations.Find(new[] { "user", "supervisor", "actor", "child-b" }).Should().NotBeNull();
            settings.LargeMessageDestinations.Find(new[] { "user", "supervisor", "actor" }).Should().BeNull("the wildcard requires one more segment");
            settings.LargeMessageDestinations.Find(new[] { "user", "supervisor", "actor", "child-a", "grandchild" }).Should().BeNull("the wildcard matches exactly one segment, not two");
        }

        [Fact(DisplayName = "Destination matcher: a trailing \"**\" segment matches any actor AT OR BELOW that position")]
        public void Should_Match_Trailing_Double_Wildcard_At_Any_Depth()
        {
            var settings = SettingsWithDestinations("/user/supervisor/**");

            settings.LargeMessageDestinations.Find(new[] { "user", "supervisor", "child" }).Should().NotBeNull();
            settings.LargeMessageDestinations.Find(new[] { "user", "supervisor", "child", "grandchild" }).Should().NotBeNull();
            settings.LargeMessageDestinations.Find(new[] { "user", "supervisor", "child", "grandchild", "great-grandchild" }).Should().NotBeNull();
            settings.LargeMessageDestinations.Find(new[] { "user", "supervisor" }).Should().BeNull("\"**\" requires at least one segment below the anchor, matching Pekko's semantics");
            settings.LargeMessageDestinations.Find(new[] { "user", "other", "child" }).Should().BeNull("a different subtree entirely");
        }

        [Fact(DisplayName = "Destination matcher: an actor whose path is not configured never matches")]
        public void Should_Not_Match_Unconfigured_Path()
        {
            var settings = SettingsWithDestinations("/user/big-actor");

            settings.LargeMessageDestinations.Find(new[] { "user", "small-actor" }).Should().BeNull();
        }

        // ----------------------------------------------------------------------------------
        // L.2/L.3 -- default-off gate (no large channel/connection materialized), and
        // system-message-never-large even when the watched path matches a configured pattern.
        // ----------------------------------------------------------------------------------

        [Fact(DisplayName = "Default-off: with no large-message-destinations configured, ordinary traffic never materializes the large outbound stream")]
        public async Task Should_Not_Materialize_Large_Stream_When_Feature_Disabled()
        {
            var systemA = ActorSystem.Create("ArteryLargeDefaultOffA", ArteryConfig());
            var systemB = ActorSystem.Create("ArteryLargeDefaultOffB", ArteryConfig());
            try
            {
                systemB.ActorOf(Props.Create(() => new Echo()), "echo");

                var echoRef = await systemA.ActorSelection(SelectionPath(systemB, "echo")).ResolveOne(TimeSpan.FromSeconds(10));
                var probe = CreateTestProbe(systemA);
                echoRef.Tell("hello", probe.Ref);
                await probe.ExpectMsgAsync("hello", TimeSpan.FromSeconds(10));

                var settings = new ArterySettings(RARP.For(systemA).Provider.RemoteSettings.Config.GetConfig("akka.remote.artery"));
                settings.LargeMessageChannelEnabled.Should().BeFalse();

                var association = ((ArteryRemoting)RARP.For(systemA).Provider.Transport).Registry.AssociationFor(RemoteAddressOf(systemB));
                association.IsLargeOutboundMaterialized.Should().BeFalse(
                    "the large outbound stream/connection must never be materialized when large-message-destinations is empty (gate L)");
                association.LargeQueueCount.Should().Be(0);
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                await systemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }

        [Fact(DisplayName = "System messages never ride the large stream even when the watched actor's path matches a configured large-message-destinations pattern")]
        public async Task Should_Not_Route_System_Messages_To_Large_Stream_Even_When_Path_Matches()
        {
            var largeConfig = ConfigurationFactory.ParseString("""
                akka.remote.artery.large-message-destinations = ["/user/watched-target"]
                """).WithFallback(ArteryConfig());

            var systemA = ActorSystem.Create("ArteryLargeSystemMsgA", largeConfig);
            var systemB = ActorSystem.Create("ArteryLargeSystemMsgB", largeConfig);
            try
            {
                var target = systemB.ActorOf(Props.Create(() => new Echo()), "watched-target");
                // A DIFFERENT, NON-matching actor used only to warm up the association -- warming
                // up via the watched-target itself would send an ORDINARY message to a path that
                // matches the configured pattern, which correctly (and confoundingly, for this
                // test) DOES ride the large stream; using a separate, non-matching path keeps this
                // test's proof clean: after warmup, the ONLY traffic this association's Watch/
                // Terminated interaction sends to the watched-target's exact path is system
                // messages.
                systemB.ActorOf(Props.Create(() => new Echo()), "warmup-target");

                var targetFromA = RARP.For(systemA).Provider.ResolveActorRef(SelectionPath(systemB, "watched-target"));
                var warmupTargetFromA = RARP.For(systemA).Provider.ResolveActorRef(SelectionPath(systemB, "warmup-target"));

                // Warm up the association with an ordinary round trip first (so Watch below is not
                // itself the very first thing establishing the association).
                var warmupProbe = CreateTestProbe(systemA);
                warmupTargetFromA.Tell("warmup", warmupProbe.Ref);
                await warmupProbe.ExpectMsgAsync("warmup", TimeSpan.FromSeconds(10));

                var terminatedProbe = CreateTestProbe(systemA);
                systemA.ActorOf(Props.Create(() => new PlainWatcher(targetFromA, terminatedProbe.Ref)));

                // Stop the watched target on B -- a genuine wire-level DeathWatchNotification must
                // arrive over the CONTROL stream for Terminated to fire on A.
                systemB.Stop(target);
                await terminatedProbe.ExpectMsgAsync("terminated", TimeSpan.FromSeconds(15));

                // THE regression proof: even though "/user/watched-target" matches the configured
                // large-message-destinations pattern, the Watch/DeathWatchNotification traffic that
                // just flowed is a SYSTEM message and must never have touched the large channel.
                var association = ((ArteryRemoting)RARP.For(systemA).Provider.Transport).Registry.AssociationFor(RemoteAddressOf(systemB));
                association.IsLargeOutboundMaterialized.Should().BeFalse(
                    "system/control messages must never route to the large channel even if their path matches a configured pattern");
                association.LargeQueueCount.Should().Be(0);
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                await systemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }

        // ----------------------------------------------------------------------------------
        // L.4 -- end-to-end loopback: a payload larger than the ordinary maximum-frame-size is
        // delivered over the dedicated large stream, and ordinary traffic to a non-matching actor
        // is unaffected.
        // ----------------------------------------------------------------------------------

        [Fact(DisplayName = "End-to-end: a payload larger than the ordinary maximum-frame-size is delivered to a configured large-message-destination, and an ordinary-stream message to a NON-matching actor still round-trips")]
        public async Task Should_Deliver_Large_Message_Over_Dedicated_Stream_Without_Disturbing_Ordinary_Traffic()
        {
            // Deliberately small ordinary maximum-frame-size: if the large payload were
            // (incorrectly) routed over the ordinary stream instead of the dedicated large one,
            // the receiver's frame parser would reject it outright (ArteryFramingException,
            // failing that connection) -- so successful delivery here is proof the large stream,
            // with its own larger frame limit, is what actually carried it.
            var largeConfig = ConfigurationFactory.ParseString("""
                akka.remote.artery.large-message-destinations = ["/user/large-target"]
                akka.remote.artery.advanced.maximum-frame-size = 32k
                """).WithFallback(ArteryConfig());

            var systemA = ActorSystem.Create("ArteryLargeE2EA", largeConfig);
            var systemB = ActorSystem.Create("ArteryLargeE2EB", largeConfig);
            try
            {
                systemB.ActorOf(Props.Create(() => new LargeMessageReceiver()), "large-target");
                systemB.ActorOf(Props.Create(() => new Echo()), "ordinary-target");

                var largeRef = await systemA.ActorSelection(SelectionPath(systemB, "large-target")).ResolveOne(TimeSpan.FromSeconds(10));
                var ordinaryRef = await systemA.ActorSelection(SelectionPath(systemB, "ordinary-target")).ResolveOne(TimeSpan.FromSeconds(10));

                // ~256 KB: comfortably above the 32 KiB ordinary maximum-frame-size configured
                // above, comfortably below the 2 MiB default maximum-large-frame-size.
                var bigPayload = new string('x', 256 * 1024);

                var probe = CreateTestProbe(systemA);
                largeRef.Tell(bigPayload, probe.Ref);
                var reply = await probe.ExpectMsgAsync<string>(TimeSpan.FromSeconds(20));
                reply.Should().Be($"received:{bigPayload.Length}", "the full large payload must have arrived intact");

                var association = ((ArteryRemoting)RARP.For(systemA).Provider.Transport).Registry.AssociationFor(RemoteAddressOf(systemB));
                association.IsLargeOutboundMaterialized.Should().BeTrue("the large message must have been routed onto the dedicated large outbound stream");

                // Ordinary traffic to a DIFFERENT, non-matching actor on the SAME association must
                // be completely unaffected by the large-message activity above.
                ordinaryRef.Tell("small-ping", probe.Ref);
                await probe.ExpectMsgAsync("small-ping", TimeSpan.FromSeconds(10));
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(15.Seconds());
                await systemB.Terminate().AwaitWithTimeout(15.Seconds());
            }
        }
    }
}
