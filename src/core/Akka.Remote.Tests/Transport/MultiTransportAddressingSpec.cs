//-----------------------------------------------------------------------
// <copyright file="MultiTransportAddressingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Remote.Tests.Transport;

/// <summary>
/// Added this spec to prove the existence of https://github.com/akkadotnet/akka.net/issues/7378
/// </summary>
public class MultiTransportAddressingSpec : TestKit.Xunit2.TestKit
{
    public MultiTransportAddressingSpec(ITestOutputHelper output) : base(GetConfig(Sys1Port1, Sys1Port2), "MultiTransportSpec", output)
    {
    }

    public const int Sys1Port1 = 9991;
    public const int Sys1Port2 = 9992;

    public const int Sys2Port1 = 9993;
    public const int Sys2Port2 = 9994;

    private static Config GetConfig(int transportPort1, int transportPort2, string actorSystemName = "MultiTransportSpec")
    {
        return $$"""
                 
                         akka {
                             actor.provider = remote
                             remote {
                                 enabled-transports = [
                                     "akka.remote.test1",
                                     "akka.remote.test2"
                                 ]
                                 test1 {
                                     transport-class = "Akka.Remote.Transport.TestTransport, Akka.Remote"
                                     applied-adapters = []
                                     registry-key = aX33k0jWKg
                                     local-address = "test1://{{actorSystemName}}@localhost:{{transportPort1}}"
                                     maximum-payload-bytes = 32000b
                                     scheme-identifier = test1
                                 }
                                 test2 {
                                     transport-class = "Akka.Remote.Transport.TestTransport, Akka.Remote"
                                     applied-adapters = []
                                     registry-key = aX33k0j11c
                                     local-address = "test2://{{actorSystemName}}@localhost:{{transportPort2}}"
                                     maximum-payload-bytes = 32000b
                                     scheme-identifier = test2
                                 }
                             }
                         }
                     
                 """;
    }


    [Fact]
    public async Task Should_Use_Second_Transport_For_Communication()
    {
        const string secondActorSystemName = "MultiTransportSpec2";
        var secondSystem = ActorSystem.Create(secondActorSystemName, GetConfig(Sys2Port1, Sys2Port2, secondActorSystemName).WithFallback(Sys.Settings.Config));
        InitializeLogger(secondSystem);
        var assertProbe = CreateTestProbe(secondSystem);
        
        try
        {
           
            var echoActor = secondSystem.ActorOf(Props.Create(() => new EchoActor(assertProbe)), "echo");

            // use the first connection
            await PingAndVerify("test1", Sys2Port1);
            
            // use the second connection
            await PingAndVerify("test2", Sys2Port2);
        }
        finally
        {
            Shutdown(secondSystem);
        }

        return;

        async Task PingAndVerify(string scheme, int port)
        {
            var selection = Sys.ActorSelection($"akka.{scheme}://{secondActorSystemName}@localhost:{port}/user/echo");
            
            // important: https://github.com/akkadotnet/akka.net/issues/7378 only occurs with IActorRefs
            var actor = await selection.ResolveOne(TimeSpan.FromSeconds(1));
            
            // assert that the remote actor is using the correct transport
            Assert.Contains(scheme, actor.Path.Address.Protocol);
            
            actor.Tell("ping");
            var reply = await ExpectMsgAsync<string>(TimeSpan.FromSeconds(3));
            Assert.Equal("pong", reply);

            var senderFromNode2Pov = await assertProbe.ExpectMsgAsync<IActorRef>();
            Assert.Contains(scheme, senderFromNode2Pov.Path.Address.Protocol);

            var senderPath = LastSender.Path.ToString();
            Assert.Contains(scheme, senderPath);
        }
    }

    public class EchoActor : ReceiveActor
    {
        private readonly IActorRef _testProbe;
        
        public EchoActor(IActorRef testProbe)
        {
            _testProbe = testProbe;
            Receive<string>(msg =>
            {
                if (msg == "ping")
                {
                    _testProbe.Tell(Sender);
                    Sender.Tell("pong");
                }
            });
        }
    }
}
