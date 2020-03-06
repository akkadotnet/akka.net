//-----------------------------------------------------------------------
// <copyright file="DotNettyTransportShutdownSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Transport;
using Akka.Remote.Transport.DotNetty;
using Akka.TestKit;
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
            var config = Sys.Settings.Config.GetConfig("akka.remote.dot-netty.tcp");
            Assert.False(config.IsNullOrEmpty());

            var t1 = new TcpTransport(Sys, config);
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
            var config = Sys.Settings.Config.GetConfig("akka.remote.dot-netty.tcp");
            Assert.False(config.IsNullOrEmpty());

            var t1 = new TcpTransport(Sys, config);
            var t2 = new TcpTransport(Sys, config);
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
                handle.ReadHandlerSource.SetResult(new ActorHandleEventListener(p1));
                var inboundHandle = p2.ExpectMsg<InboundAssociation>().Association; // wait for the inbound association handle to show up
                inboundHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(p2));

                AwaitCondition(() => t1.ConnectionGroup.Count == 2);
                AwaitCondition(() => t2.ConnectionGroup.Count == 2);

                var chan1 = t1.ConnectionGroup.Single(x => !x.Id.Equals(t1.ServerChannel.Id));
                var chan2 = t2.ConnectionGroup.Single(x => !x.Id.Equals(t2.ServerChannel.Id));

                // force a disassociation
                handle.Disassociate();

                // verify that the connections are terminated
                p1.ExpectMsg<Disassociated>();
                AwaitCondition(() => t1.ConnectionGroup.Count == 1);
                AwaitCondition(() => t2.ConnectionGroup.Count == 1);

                // verify that the connection channels were terminated on both ends
                chan1.CloseCompletion.IsCompleted.Should().BeTrue();
                chan2.CloseCompletion.IsCompleted.Should().BeTrue();
            }
            finally
            {
                await t1.Shutdown();
                await t2.Shutdown();
            }
        }

        [Fact]
        public async Task DotNettyTcpTransport_should_cleanly_terminate_active_endpoints_upon_outbound_shutdown()
        {
            var config = Sys.Settings.Config.GetConfig("akka.remote.dot-netty.tcp");
            Assert.False(config.IsNullOrEmpty());

            var t1 = new TcpTransport(Sys, config);
            var t2 = new TcpTransport(Sys, config);
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
                handle.ReadHandlerSource.SetResult(new ActorHandleEventListener(p1));
                var inboundHandle = p2.ExpectMsg<InboundAssociation>().Association; // wait for the inbound association handle to show up
                inboundHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(p2));

                AwaitCondition(() => t1.ConnectionGroup.Count == 2);
                AwaitCondition(() => t2.ConnectionGroup.Count == 2);

                var chan1 = t1.ConnectionGroup.Single(x => !x.Id.Equals(t1.ServerChannel.Id));
                var chan2 = t2.ConnectionGroup.Single(x => !x.Id.Equals(t2.ServerChannel.Id));

                //  shutdown remoting on t1
                await t1.Shutdown();

                p2.ExpectMsg<Disassociated>();
                // verify that the connections are terminated
                AwaitCondition(() => t1.ConnectionGroup.Count == 0, null, message: $"Expected 0 open connection but found {t1.ConnectionGroup.Count}");
                AwaitCondition(() => t2.ConnectionGroup.Count == 1, null,message: $"Expected 1 open connection but found {t2.ConnectionGroup.Count}");

                // verify that the connection channels were terminated on both ends
                chan1.CloseCompletion.IsCompleted.Should().BeTrue();
                chan2.CloseCompletion.IsCompleted.Should().BeTrue();
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
            var config = Sys.Settings.Config.GetConfig("akka.remote.dot-netty.tcp");
            Assert.False(config.IsNullOrEmpty());

            var t1 = new TcpTransport(Sys, config);
            var t2 = new TcpTransport(Sys, config);
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
                handle.ReadHandlerSource.SetResult(new ActorHandleEventListener(p1));
                var inboundHandle = p2.ExpectMsg<InboundAssociation>().Association; // wait for the inbound association handle to show up
                inboundHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(p2));

                AwaitCondition(() => t1.ConnectionGroup.Count == 2);
                AwaitCondition(() => t2.ConnectionGroup.Count == 2);

                var chan1 = t1.ConnectionGroup.Single(x => !x.Id.Equals(t1.ServerChannel.Id));
                var chan2 = t2.ConnectionGroup.Single(x => !x.Id.Equals(t2.ServerChannel.Id));

                // force a disassociation
                inboundHandle.Disassociate();

                // verify that the connections are terminated
                AwaitCondition(() => t1.ConnectionGroup.Count == 1, null, message: $"Expected 1 open connection but found {t1.ConnectionGroup.Count}");
                AwaitCondition(() => t2.ConnectionGroup.Count == 1, null, message: $"Expected 1 open connection but found {t2.ConnectionGroup.Count}");

                // verify that the connection channels were terminated on both ends
                chan1.CloseCompletion.IsCompleted.Should().BeTrue();
                chan2.CloseCompletion.IsCompleted.Should().BeTrue();
            }
            finally
            {
                await t1.Shutdown();
                await t2.Shutdown();
            }
        }

        [Fact]
        public async Task DotNettyTcpTransport_should_cleanly_terminate_active_endpoints_upon_inbound_shutdown()
        {
            var config = Sys.Settings.Config.GetConfig("akka.remote.dot-netty.tcp");
            Assert.False(config.IsNullOrEmpty());

            var t1 = new TcpTransport(Sys, config);
            var t2 = new TcpTransport(Sys, config);
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
                handle.ReadHandlerSource.SetResult(new ActorHandleEventListener(p1));
                var inboundHandle = p2.ExpectMsg<InboundAssociation>().Association; // wait for the inbound association handle to show up
                inboundHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(p2));

                AwaitCondition(() => t1.ConnectionGroup.Count == 2);
                AwaitCondition(() => t2.ConnectionGroup.Count == 2);

                var chan1 = t1.ConnectionGroup.Single(x => !x.Id.Equals(t1.ServerChannel.Id));
                var chan2 = t2.ConnectionGroup.Single(x => !x.Id.Equals(t2.ServerChannel.Id));

                // shutdown inbound side
                await t2.Shutdown();

                // verify that the connections are terminated
                AwaitCondition(() => t1.ConnectionGroup.Count == 1, null, message: $"Expected 1 open connection but found {t1.ConnectionGroup.Count}");
                AwaitCondition(() => t2.ConnectionGroup.Count == 0, null, message: $"Expected 0 open connection but found {t2.ConnectionGroup.Count}");

                // verify that the connection channels were terminated on both ends
                chan1.CloseCompletion.IsCompleted.Should().BeTrue();
                chan2.CloseCompletion.IsCompleted.Should().BeTrue();
            }
            finally
            {
                await t1.Shutdown();
                await t2.Shutdown();
            }
        }

        [Fact]
        public async Task DotNettyTcpTransport_should_cleanly_terminate_endpoints_upon_failed_outbound_connection()
        {
            var config = Sys.Settings.Config.GetConfig("akka.remote.dot-netty.tcp");
            Assert.False(config.IsNullOrEmpty());

            var t1 = new TcpTransport(Sys, config);
            try
            {
                var p1 = CreateTestProbe();

                // bind
                var c1 = await t1.Listen();
                c1.Item2.SetResult(new ActorAssociationEventListener(p1));

                // t1 --> t2 association
                await Assert.ThrowsAsync<InvalidAssociationException>(async () =>
                {
                    var a = await t1.Associate(c1.Item1.WithPort(c1.Item1.Port + 100));
                });


                AwaitCondition(() => t1.ConnectionGroup.Count == 1);
            }
            finally
            {
                await t1.Shutdown();
            }
        }
    }
}
