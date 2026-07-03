//-----------------------------------------------------------------------
// <copyright file="PipeDotNettyInteropSpec.cs" company="Akka.NET Project">
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
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.v3;

namespace Akka.Remote.Tests.Transport
{
    /// <summary>
    /// Cross-transport interop tests proving that the <see cref="Akka.Remote.Transport.Pipelines.TcpPipeTransport"/>
    /// running in its <b>default protobuf envelope mode</b> is wire-compatible with the
    /// legacy DotNetty TCP transport. 🌸
    ///
    /// <para>
    /// Both transports advertise the <c>akka.tcp</c> scheme and — when the pipe transport uses
    /// <c>envelope = protobuf</c> (the default) — speak the exact same AkkaProtocol PDU format on the
    /// wire. These tests spin up one real <see cref="ActorSystem"/> per transport and exchange real
    /// messages in <b>both directions</b> to prove the handshake, heartbeat, and message-level
    /// envelopes all decode correctly across the two implementations.
    /// </para>
    ///
    /// <!-- CopilotNotes: Sys (the AkkaSpec base system) runs the *pipe* transport; the second
    ///      system (_dotNettySystem) runs DotNetty. Both bind 127.0.0.1:0 (OS-assigned port) so the
    ///      tests stay fast and independent. We resolve the bound addresses via
    ///      RARP.For(system).Provider.DefaultAddress — see RemotingTerminatorSpecs for the pattern.
    ///      The whole point is wire compatibility, so a successful round-trip *is* the assertion. -->
    /// </summary>
    public class PipeDotNettyInteropSpec : AkkaSpec
    {
        // ── HOCON ──────────────────────────────────────────────────────────────

        /// <summary>Config for the system running the System.IO.Pipelines transport (default protobuf envelope).</summary>
        private static readonly Config PipeConfig = ConfigurationFactory.ParseString(@"
            akka {
                loglevel = DEBUG
                actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                remote {
                    enabled-transports = [""akka.remote.pipe.tcp""]
                    pipe.tcp {
                        hostname = ""127.0.0.1""
                        port     = 0
                        # The default — stated explicitly to document intent: protobuf == DotNetty wire format.
                        envelope = protobuf
                    }
                }
            }");

        /// <summary>Config for the system running the legacy DotNetty TCP transport.</summary>
        private static readonly Config DotNettyConfig = ConfigurationFactory.ParseString(@"
            akka {
                loglevel = DEBUG
                actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                remote {
                    enabled-transports = [""akka.remote.dot-netty.tcp""]
                    dot-netty.tcp {
                        hostname = ""127.0.0.1""
                        port     = 0
                    }
                }
            }");

        // ── Fields ─────────────────────────────────────────────────────────────

        private readonly ActorSystem _dotNettySystem;

        /// <summary>Bound address of the pipe-transport system (this == Sys).</summary>
        private Address PipeAddress => RARP.For(Sys).Provider.DefaultAddress;

        /// <summary>Bound address of the DotNetty-transport system.</summary>
        private Address DotNettyAddress => RARP.For(_dotNettySystem).Provider.DefaultAddress;

        // ── Constructor ────────────────────────────────────────────────────────

        public PipeDotNettyInteropSpec(ITestOutputHelper output)
            : base(PipeConfig, output)
        {
            _dotNettySystem = ActorSystem.Create("DotNettySystem", DotNettyConfig.WithFallback(Sys.Settings.Config));
            InitializeLogger(_dotNettySystem);
        }

        protected override void AfterAll()
        {
            // Always tear the second system down so its sockets are released. 🧹
            Shutdown(_dotNettySystem);
            base.AfterAll();
        }

        // ── Sanity check ───────────────────────────────────────────────────────

        [Fact(DisplayName = "Both transports should advertise the akka.tcp scheme so they are mutually addressable")]
        public void Both_Transports_Should_Use_Akka_Tcp_Scheme()
        {
            // If these schemes ever diverge, cross-transport addressing silently breaks — guard it. uwu
            PipeAddress.Protocol.Should().Be("akka.tcp");
            DotNettyAddress.Protocol.Should().Be("akka.tcp");
            PipeAddress.Port.Should().NotBe(DotNettyAddress.Port);
        }

        // ── Pipe ──► DotNetty ──────────────────────────────────────────────────

        [Fact(DisplayName = "Pipe transport should resolve and message an actor hosted on the DotNetty transport")]
        public async Task Pipe_Should_Message_Actor_On_DotNetty()
        {
            // Host the echo actor on the DotNetty side.
            _dotNettySystem.ActorOf(Props.Create(() => new EchoActor()), "echo");

            // From the PIPE system, resolve the remote actor and round-trip a message.
            var remote = await Sys.ActorSelection(new RootActorPath(DotNettyAddress) / "user" / "echo")
                .ResolveOne(TimeSpan.FromSeconds(5));

            remote.Path.Address.Protocol.Should().Be("akka.tcp");

            remote.Tell("ping from pipe", TestActor);
            (await ExpectMsgAsync<string>(TimeSpan.FromSeconds(5))).Should().Be("ping from pipe");
        }

        // ── DotNetty ──► Pipe ──────────────────────────────────────────────────

        [Fact(DisplayName = "DotNetty transport should resolve and message an actor hosted on the Pipe transport")]
        public async Task DotNetty_Should_Message_Actor_On_Pipe()
        {
            // Host the echo actor on the PIPE side (Sys).
            Sys.ActorOf(Props.Create(() => new EchoActor()), "echo");

            // Use a probe living inside the DotNetty system as the requester/asserter.
            var probe = CreateTestProbe(_dotNettySystem);

            var remote = await _dotNettySystem.ActorSelection(new RootActorPath(PipeAddress) / "user" / "echo")
                .ResolveOne(TimeSpan.FromSeconds(5));

            remote.Path.Address.Protocol.Should().Be("akka.tcp");

            remote.Tell("ping from dotnetty", probe.Ref);
            (await probe.ExpectMsgAsync<string>(TimeSpan.FromSeconds(5))).Should().Be("ping from dotnetty");
        }

        // ── Bidirectional Ask round-trip ───────────────────────────────────────

        [Fact(DisplayName = "Ask should round-trip across the pipe<->dotnetty boundary and preserve the reply Sender")]
        public async Task Ask_Should_RoundTrip_Across_Transports()
        {
            _dotNettySystem.ActorOf(Props.Create(() => new EchoActor()), "ask-echo");

            var remote = await Sys.ActorSelection(new RootActorPath(DotNettyAddress) / "user" / "ask-echo")
                .ResolveOne(TimeSpan.FromSeconds(5));

            // Ask exercises the temp-actor reply path: the DotNetty actor must be able to route a
            // reply back to a transient actor living on the pipe transport.
            var reply = await remote.Ask<string>("ask-ping", TimeSpan.FromSeconds(5));
            reply.Should().Be("ask-ping");
        }

        // ── Ordering / throughput ──────────────────────────────────────────────

        [Fact(DisplayName = "Multiple messages should arrive in order across the pipe<->dotnetty boundary")]
        public async Task Multiple_Messages_Should_Arrive_In_Order()
        {
            Sys.ActorOf(Props.Create(() => new EchoActor()), "order-echo");

            var probe = CreateTestProbe(_dotNettySystem);
            var remote = await _dotNettySystem.ActorSelection(new RootActorPath(PipeAddress) / "user" / "order-echo")
                .ResolveOne(TimeSpan.FromSeconds(5));

            const int messageCount = 20;
            for (var i = 0; i < messageCount; i++)
                remote.Tell($"msg-{i}", probe.Ref);

            for (var i = 0; i < messageCount; i++)
                (await probe.ExpectMsgAsync<string>(TimeSpan.FromSeconds(5))).Should().Be($"msg-{i}");
        }

        // ── Large payload (multi-segment frames) ───────────────────────────────

        [Fact(DisplayName = "A large payload should survive the pipe<->dotnetty round trip intact")]
        public async Task Large_Payload_Should_RoundTrip_Intact()
        {
            _dotNettySystem.ActorOf(Props.Create(() => new EchoActor()), "big-echo");

            var remote = await Sys.ActorSelection(new RootActorPath(DotNettyAddress) / "user" / "big-echo")
                .ResolveOne(TimeSpan.FromSeconds(5));

            // ~16 KB string — comfortably under the default maximum-frame-size but large enough to
            // span multiple read segments on the pipe side.
            var big = new string('A', 16 * 1024) + "-omega";

            var reply = await remote.Ask<string>(big, TimeSpan.FromSeconds(10));
            reply.Should().Be(big);
            reply.Length.Should().Be(big.Length);
        }

        // ── Remote deployment across transports ────────────────────────────────

        [Fact(DisplayName = "An actor remotely deployed from the pipe system onto the dotnetty system should be reachable")]
        public async Task Pipe_Should_Remotely_Deploy_Onto_DotNetty()
        {
            // Deploy an echo actor from the PIPE system onto the DOTNETTY system.
            var deployed = Sys.ActorOf(
                Props.Create(() => new EchoActor())
                    .WithDeploy(Deploy.None.WithScope(new RemoteScope(DotNettyAddress))),
                "deployed-echo");

            // The deployed actor's path should live on the DotNetty system's address.
            deployed.Path.Address.Should().Be(DotNettyAddress);

            deployed.Tell("deployed ping", TestActor);
            (await ExpectMsgAsync<string>(TimeSpan.FromSeconds(5))).Should().Be("deployed ping");
        }

        // ── Identify handshake ─────────────────────────────────────────────────

        [Fact(DisplayName = "Identify should resolve an ActorIdentity across the pipe<->dotnetty boundary")]
        public async Task Identify_Should_Resolve_Across_Transports()
        {
            _dotNettySystem.ActorOf(Props.Create(() => new EchoActor()), "ident-echo");

            var selection = Sys.ActorSelection(new RootActorPath(DotNettyAddress) / "user" / "ident-echo");
            var identity = await selection.Ask<ActorIdentity>(new Identify("hello"), TimeSpan.FromSeconds(5));

            identity.MessageId.Should().Be("hello");
            identity.Subject.Should().NotBeNull();
            identity.Subject!.Path.Address.Protocol.Should().Be("akka.tcp");
        }

        // ── Test actor ─────────────────────────────────────────────────────────

        /// <summary>
        /// A trivial echo actor: replies to the sender with the exact message it received.
        /// <!-- CopilotNotes: Kept transport-agnostic on purpose so it can be hosted on either side. -->
        /// </summary>
        private sealed class EchoActor : ReceiveActor
        {
            public EchoActor()
            {
                ReceiveAny(msg => Sender.Tell(msg));
            }
        }
    }
}

