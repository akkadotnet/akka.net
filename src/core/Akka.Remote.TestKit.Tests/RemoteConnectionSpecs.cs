﻿//-----------------------------------------------------------------------
// <copyright file="RemoteConnectionSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using FluentAssertions;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using Akka.Util.Internal;
using DotNetty.Transport.Channels;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Remote.TestKit.Tests
{
    public class RemoteConnectionSpecs : AkkaSpec
    {
        private const string Config = @"
            akka.testconductor.barrier-timeout = 5s
            akka.loglevel = DEBUG
            akka.actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
            akka.actor.debug.fsm = on
            akka.actor.debug.lifecycle = on
        ";

        public RemoteConnectionSpecs() : base(Config)
        {
            
        }

        [Fact(Skip = "Consistently fails on buildserver - appears to be some binding issue on Azure DevOps")]
        public async Task RemoteConnection_should_send_and_decode_messages()
        {
            var serverProbe = CreateTestProbe("server");
            var clientProbe = CreateTestProbe("client");

            var serverAddress = IPAddress.Parse("127.0.0.1");
            var serverEndpoint = new IPEndPoint(serverAddress, 0);

            IChannel server = null;
            IChannel client = null;

            try
            {
                using var cts = new CancellationTokenSource(10.Seconds());
                server = await RemoteConnection.CreateConnection(Role.Server, serverEndpoint, 3,
                    new TestConductorHandler(serverProbe.Ref))
                    .WaitAsync(cts.Token);

                var reachableEndpoint = (IPEndPoint)server.LocalAddress;

                client = await RemoteConnection.CreateConnection(Role.Client, reachableEndpoint, 3,
                    new PlayerHandler(serverEndpoint, 2, TimeSpan.FromSeconds(1), 3, clientProbe.Ref, Log, Sys.Scheduler))
                    .WaitAsync(cts.Token);
                cts.Dispose();

                serverProbe.ExpectMsg("active");
                var serverClientChannel = serverProbe.ExpectMsg<IChannel>();
                clientProbe.ExpectMsg<ClientFSM.Connected>();

                var address = RARP.For(Sys).Provider.DefaultAddress;

                // have the client send a message to the server
                await client.WriteAndFlushAsync(new Hello("test", address));
                var hello = serverProbe.ExpectMsg<Hello>();
                hello.Name.Should().Be("test");
                hello.Address.Should().Be(address);

                // have the server send a message back to the client
                await serverClientChannel.WriteAndFlushAsync(new Hello("test2", address));
                var hello2 = clientProbe.ExpectMsg<Hello>();
                hello2.Name.Should().Be("test2");
                hello2.Address.Should().Be(address);
            }
            finally
            {
                var tasks = new List<Task>();
                if(server is not null)
                    tasks.Add(server.CloseAsync());
                if (client is not null)
                    tasks.Add(client.CloseAsync());
                
                await Task.WhenAll(tasks)
                    .ShouldCompleteWithin(2.Seconds());
            }
          
        }

        [Fact(Skip = "This causes a deadlock sometimes")]
        public async Task RemoteConnection_should_send_and_decode_Done_message()
        {
            var serverProbe = CreateTestProbe("server");
            var clientProbe = CreateTestProbe("client");

            var serverAddress = IPAddress.Parse("127.0.0.1");
            var serverEndpoint = new IPEndPoint(serverAddress, 0);

            IChannel server = null;
            IChannel client = null;

            try
            {
                using var cts = new CancellationTokenSource(10.Seconds());
                server = await RemoteConnection.CreateConnection(Role.Server, serverEndpoint, 3,
                        new TestConductorHandler(serverProbe.Ref))
                    .WaitAsync(cts.Token);

                var reachableEndpoint = (IPEndPoint)server.LocalAddress;

                client = await RemoteConnection.CreateConnection(Role.Client, reachableEndpoint, 3,
                        new PlayerHandler(serverEndpoint, 2, TimeSpan.FromSeconds(1), 3, clientProbe.Ref, Log, Sys.Scheduler))
                    .WaitAsync(cts.Token);
                cts.Dispose();

                serverProbe.ExpectMsg("active");
                var serverClientChannel = serverProbe.ExpectMsg<IChannel>();
                clientProbe.ExpectMsg<ClientFSM.Connected>();

                var address = RARP.For(Sys).Provider.DefaultAddress;

                // have the client send a message to the server
                await client.WriteAndFlushAsync(Done.Instance);
                var done = serverProbe.ExpectMsg<Done>();
                done.Should().BeOfType<Done>();

                //have the server send a message back to the client
                await serverClientChannel.WriteAndFlushAsync(Done.Instance);
                var done2 = clientProbe.ExpectMsg<Done>();
                done2.Should().BeOfType<Done>();
            }
            finally
            {
                var tasks = new List<Task>();
                if(server is not null)
                    tasks.Add(server.CloseAsync());
                if (client is not null)
                    tasks.Add(client.CloseAsync());
                
                await Task.WhenAll(tasks)
                    .ShouldCompleteWithin(2.Seconds());
            }

        }
    }

    public class TestConductorHandler : ChannelHandlerAdapter
    {
        private readonly IActorRef _testActorRef;

        public TestConductorHandler(IActorRef testActorRef)
        {
            _testActorRef = testActorRef;
        }

        public override bool IsSharable => true;

        public override void ChannelActive(IChannelHandlerContext context)
        {
            _testActorRef.Tell("active");
            _testActorRef.Tell(context.Channel);
        }

        public override void ChannelInactive(IChannelHandlerContext context)
        {
            _testActorRef.Tell("inactive");
        }

        public override void ChannelRead(IChannelHandlerContext context, object message)
        {
            if (message is INetworkOp)
            {
                _testActorRef.Tell(message);
            }
            else
            {
                //_log.Debug("client {0} sent garbage `{1}`, disconnecting", channel.RemoteAddress, message);
                context.Channel.CloseAsync();
            }
        }
    }
}
