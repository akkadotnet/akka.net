using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Transport;
using Akka.Remote.Transport.DotNetty;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Xunit;
using FluentAssertions;

namespace Akka.Remote.Tests.Transport
{
    /// <summary>
    /// Verify that the <see cref="DotNettyTransport"/> can cleanly shut itself down.
    /// </summary>
    public class DotNettyTransportShutdownSpec : AkkaSpec
    {
        private static readonly Config Config = ConfigurationFactory.ParseString(@"
            akka {
                loglevel = DEBUG
                actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
                remote {
                    dot-netty.tcp {
                        port = 0
                        hostname = ""127.0.0.1""
                        log-transport = true
                    }
                }
            }");

        public DotNettyTransportShutdownSpec() : base(Config)
        {
            
        }

        [Fact]
        public async Task DotNettyTcpTransport_should_cleanly_terminate_unused_inbound_endpoint()
        {
            var t1 = new TcpTransport(Sys, Sys.Settings.Config.GetConfig("akka.remote.dot-netty.tcp"));
            try
            {
                // bind
                await t1.Listen();

                // verify that ServerChannel is active and open
                var sc = t1.ServerChannel;
                sc.Should().NotBeNull();
                sc.Active.Should().BeTrue();
                sc.Open.Should().BeTrue();

                // shutdown
                await t1.Shutdown();
                sc.Open.Should().BeFalse();
                sc.CloseCompletion.IsCompleted.Should().BeTrue();
            }
            finally
            {
                await t1.Shutdown();
            }
        }

        [Fact]
        public async Task DotNettyTcpTransport_should_cleanly_terminate_active_endpoints_upon_outbound_disassociate()
        {
            var t1 = new TcpTransport(Sys, Sys.Settings.Config.GetConfig("akka.remote.dot-netty.tcp"));
            var t2 = new TcpTransport(Sys, Sys.Settings.Config.GetConfig("akka.remote.dot-netty.tcp"));
            try
            {
                var p1 = CreateTestProbe();
                var p2 = CreateTestProbe();

                // bind
                var c1 = await t1.Listen();
                c1.Item2.SetResult(new ActorAssociationEventListener(p1));
                var c2 = await t2.Listen();
                c2.Item2.SetResult(new ActorAssociationEventListener(p2));

                // t1 --> t2 association
                var handle = await t1.Associate(c2.Item1);
                p2.ExpectMsg<IAssociationEvent>(); // wait for the inbound association handle to show up
                t1.ConnectionGroup.Count.Should().Be(2);
                t2.ConnectionGroup.Count.Should().Be(2);

                // force a disassociation
                handle.Disassociate();

                // verify that the connections are terminated
                AwaitCondition(() => t1.ConnectionGroup.Count == 1);
                AwaitCondition(() => t2.ConnectionGroup.Count == 1);
            }
            finally
            {
                await t1.Shutdown();
                await t2.Shutdown();
            }
        }

        [Fact]
        public async Task DotNettyTcpTransport_should_cleanly_terminate_active_endpoints_upon_inbound_disassociate()
        {
            var t1 = new TcpTransport(Sys, Sys.Settings.Config.GetConfig("akka.remote.dot-netty.tcp"));
            var t2 = new TcpTransport(Sys, Sys.Settings.Config.GetConfig("akka.remote.dot-netty.tcp"));
            try
            {
                var p1 = CreateTestProbe();
                var p2 = CreateTestProbe();

                // bind
                var c1 = await t1.Listen();
                c1.Item2.SetResult(new ActorAssociationEventListener(p1));
                var c2 = await t2.Listen();
                c2.Item2.SetResult(new ActorAssociationEventListener(p2));

                // t1 --> t2 association
                var handle = await t1.Associate(c2.Item1);
                var inboundHandle = p2.ExpectMsg<InboundAssociation>(); // wait for the inbound association handle to show up
                t1.ConnectionGroup.Count.Should().Be(2);
                t2.ConnectionGroup.Count.Should().Be(2);

                // force a disassociation
                inboundHandle.Association.Disassociate();

                // verify that the connections are terminated
                AwaitCondition(() => t1.ConnectionGroup.Count == 1);
                AwaitCondition(() => t2.ConnectionGroup.Count == 1);
            }
            finally
            {
                await t1.Shutdown();
                await t2.Shutdown();
            }
        }
    }
}
