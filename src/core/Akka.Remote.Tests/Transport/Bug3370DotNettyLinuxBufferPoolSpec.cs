//-----------------------------------------------------------------------
// <copyright file="Bug3370DotNettyLinuxBufferPoolSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Configuration;
using Akka.Remote.Transport.DotNetty;
using Akka.TestKit;
using DotNetty.Buffers;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Transport
{
    /// <summary>
    /// Spec designed to verify that https://github.com/akkadotnet/akka.net/issues/3370
    /// </summary>
    public class Bug3370DotNettyLinuxBufferPoolSpec : AkkaSpec
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
                        enable-pooling = false
                    }
                }
            }");

        public Bug3370DotNettyLinuxBufferPoolSpec() : base(Config)
        {

        }

        [Fact]
        public async Task DotNettyTcpTransport_should_start_without_pooling()
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
                sc.Allocator.Should().NotBeOfType<PooledByteBufferAllocator>(); // verify we are not using pooling
                sc.Allocator.Should().BeOfType<UnpooledByteBufferAllocator>(); 
            }
            finally
            {
                await t1.Shutdown();
            }
        }

        [Fact]
        public void DotNettyTcpTransport_should_communicate_without_pooling()
        {
            var sys2 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);

            InitializeLogger(sys2);
            try
            {
                var echo = sys2.ActorOf(act => { act.ReceiveAny((o, context) => context.Sender.Tell(o)); }, "echo");

                var address1 = RARP.For(Sys).Provider.DefaultAddress;
                var address2 = RARP.For(sys2).Provider.DefaultAddress;
                var echoPath = new RootActorPath(address2) / "user" / "echo";
                var probe = CreateTestProbe();
                Sys.ActorSelection(echoPath).Tell("hello", probe.Ref);
                probe.ExpectMsg("hello");
            }
            finally
            { 
                Shutdown(sys2);
            }
        }
    }
}
