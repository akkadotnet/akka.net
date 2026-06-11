//-----------------------------------------------------------------------
// <copyright file="StreamTcpTransportInteropSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Transport
{
    public class StreamTcpTransportInteropSpec : AkkaSpec
    {
        public StreamTcpTransportInteropSpec(ITestOutputHelper output) : base(ConfigurationFactory.Empty, output)
        {
        }

        [Fact]
        public async Task StreamTcpTransport_should_interoperate_with_classic_DotNetty_and_reconnect_after_stream_node_restart()
        {
            var streamPort = GetFreeTcpPort();
            ActorSystem classic = null;
            ActorSystem stream = null;

            try
            {
                classic = ActorSystem.Create("classic-sys", ClassicConfig(0));
                InitializeLogger(classic);
                classic.ActorOf(Props.Create<Echo>(), "echo");

                stream = StartStreamSystem("stream-sys", streamPort);

                var classicAddress = RARP.For(classic).Provider.DefaultAddress;
                var streamAddress = RARP.For(stream).Provider.DefaultAddress;

                await AssertRemoteEcho(classic, streamAddress, "classic-to-stream-1", TimeSpan.FromSeconds(15));
                await AssertRemoteEcho(stream, classicAddress, "stream-to-classic-1", TimeSpan.FromSeconds(15));

                Shutdown(stream, TimeSpan.FromSeconds(10));
                stream = null;

                stream = StartStreamSystem("stream-sys", streamPort);
                var restartedStreamAddress = RARP.For(stream).Provider.DefaultAddress;
                restartedStreamAddress.Should().Be(streamAddress);

                await AssertRemoteEcho(classic, restartedStreamAddress, "classic-to-stream-2", TimeSpan.FromSeconds(15));
                await AssertRemoteEcho(stream, classicAddress, "stream-to-classic-2", TimeSpan.FromSeconds(15));
            }
            finally
            {
                if (stream != null)
                    Shutdown(stream);
                if (classic != null)
                    Shutdown(classic);
            }
        }

        [Fact]
        public async Task StreamTcpTransport_should_support_stream_to_stream_remote_messaging()
        {
            ActorSystem streamA = null;
            ActorSystem streamB = null;

            try
            {
                streamA = StartStreamSystem("stream-a", GetFreeTcpPort());
                streamB = StartStreamSystem("stream-b", GetFreeTcpPort());
                var addressA = RARP.For(streamA).Provider.DefaultAddress;
                var addressB = RARP.For(streamB).Provider.DefaultAddress;

                await AssertRemoteEcho(streamA, addressB, "stream-a-to-stream-b");
                await AssertRemoteEcho(streamB, addressA, "stream-b-to-stream-a");
            }
            finally
            {
                if (streamB != null)
                    Shutdown(streamB);
                if (streamA != null)
                    Shutdown(streamA);
            }
        }

        private ActorSystem StartStreamSystem(string systemName, int port)
        {
            var system = ActorSystem.Create(systemName, StreamConfig(port));
            InitializeLogger(system);
            system.ActorOf(Props.Create<Echo>(), "echo");
            return system;
        }

        private async Task AssertRemoteEcho(ActorSystem sendingSystem, Address remoteAddress, string payload, TimeSpan? timeout = null)
        {
            var max = timeout ?? TimeSpan.FromSeconds(5);
            await AwaitAssertAsync(async () =>
            {
                var probe = CreateTestProbe(sendingSystem);
                var echo = sendingSystem.ActorSelection(new RootActorPath(remoteAddress) / "user" / "echo");
                echo.Tell(payload, probe.Ref);
                var response = await probe.ExpectMsgAsync<string>(TimeSpan.FromSeconds(2));
                response.Should().Be($"echo:{payload}");
            }, max);
        }

        private static Config ClassicConfig(int port)
        {
            return ConfigurationFactory.ParseString($@"
                akka.actor.provider = remote
                akka.remote.retry-gate-closed-for = 1s
                akka.remote.log-remote-lifecycle-events = off
                akka.remote.enabled-transports = [""akka.remote.dot-netty.tcp""]
                akka.remote.dot-netty.tcp {{
                    hostname = localhost
                    port = {port}
                }}
                akka.test.single-expect-default = 3s");
        }

        private static Config StreamConfig(int port)
        {
            return ConfigurationFactory.ParseString($@"
                akka.actor.provider = remote
                akka.remote.retry-gate-closed-for = 1s
                akka.remote.log-remote-lifecycle-events = off
                akka.remote.enabled-transports = [""akka.remote.dot-netty.tcp""]
                akka.remote.dot-netty.tcp {{
                    transport-class = ""Akka.Remote.Transport.Streams.TcpStreamTransport, Akka.Remote""
                    hostname = localhost
                    port = {port}
                    tcp-reuse-addr = on
                }}
                akka.test.single-expect-default = 3s");
        }

        private static int GetFreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private sealed class Echo : ReceiveActor
        {
            public Echo()
            {
                Receive<string>(message => Sender.Tell($"echo:{message}"));
            }
        }
    }
}
