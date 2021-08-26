//-----------------------------------------------------------------------
// <copyright file="BugFix4384Spec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Configuration;
using Akka.Routing;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Actor
{
    public class BugFix4384Spec : TestKit.Xunit2.TestKit
    {
        public ActorSystem Sys1 { get; }
        public Address Sys1Address { get; }
        public ActorSystem Sys2 { get; }
        public Address Sys2Address { get; }
        
        public BugFix4384Spec(ITestOutputHelper outputHelper) : base(nameof(BugFix4384Spec), outputHelper)
        {
            var sys1Port = GetFreeTcpPort();
            var sys2Port = GetFreeTcpPort();
            Sys1 = ActorSystem.Create("Sys1", config: ConfigurationFactory.ParseString($@"
                akka.actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                akka.remote.dot-netty.tcp.port = {sys1Port}
                akka.remote.dot-netty.tcp.hostname = 127.0.0.1
            ").WithFallback(DefaultConfig.WithFallback(FullDebugConfig)));
            Sys2 = ActorSystem.Create("Sys2", config: ConfigurationFactory.ParseString($@"
                akka.actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                akka.remote.dot-netty.tcp.port = {sys2Port}
                akka.remote.dot-netty.tcp.hostname = 127.0.0.1
            ").WithFallback(DefaultConfig.WithFallback(FullDebugConfig)));
            Sys1Address = new Address("akka.tcp", Sys1.Name, "127.0.0.1", sys1Port);
            Sys2Address = new Address("akka.tcp", Sys2.Name, "127.0.0.1", sys2Port);
            
            InitializeLogger(Sys1);
            InitializeLogger(Sys2);
        }

        [Fact]
        public async Task Ask_from_local_actor_without_remote_association_should_work()
        {
            // create actor in Sys1
            const string actorName = "actor1";
            Sys1.ActorOf(dsl => dsl.ReceiveAny((m, ctx) => TestActor.Tell(m)), actorName);
            
            // create ActorSelection from Sys2 --> Sys1
            var sel = Sys2.ActorSelection(new RootActorPath(Sys1Address) / "user" / actorName);
            
            // make sure that actor1 is able to resolve temporary actor's path
            // see https://github.com/akkadotnet/akka.net/issues/4384 - tmp actor should belong to Sys2 here
            var msg = await sel.Ask<ActorIdentity>(new Identify("foo"), TimeSpan.FromSeconds(30));
            msg.MessageId.Should().Be("foo");
        }
        
        [Fact(Skip = "The spec above contains the reproduction of the real issue")]
        public async Task ConsistentHashingPoolRoutersShouldWorkAsExpectedWithHashMapping()
        {
            var poolRouter =
                Sys1.ActorOf(Props.Create(() => new ReporterActor(TestActor)).WithRouter(new ConsistentHashingPool(5,
                        msg =>
                        {
                            if (msg is IConsistentHashable c)
                                return c.ConsistentHashKey;
                            return msg;
                        })),
                    "router1");

            // use some auto-received messages to ensure that those still work
            var numRoutees = (await poolRouter.Ask<Routees>(new GetRoutees(), TimeSpan.FromSeconds(2))).Members.Count();
            
            // establish association between ActorSystems
            var sys2Probe = CreateTestProbe(Sys2);
            var secondActor = Sys1.ActorOf(act => act.ReceiveAny((o, ctx) => ctx.Sender.Tell(o)), "foo");

            Sys2.ActorSelection(new RootActorPath(Sys1Address) / "user" / secondActor.Path.Name).Tell("foo", sys2Probe);
            sys2Probe.ExpectMsg("foo");

            // have ActorSystem2 message it via tell
            var sel = Sys2.ActorSelection(new RootActorPath(Sys1Address) / "user" / "router1");
            sel.Tell(new HashableString("foo"));
            ExpectMsg<HashableString>(str => str.Str.Equals("foo"));

            // have ActorSystem2 message it via Ask
            sel.Ask(new Identify("bar2"), TimeSpan.FromSeconds(3)).PipeTo(sys2Probe);
            var remoteRouter = sys2Probe.ExpectMsg<ActorIdentity>(x => x.MessageId.Equals("bar2"), TimeSpan.FromSeconds(5)).Subject;

            var s2Actor = Sys2.ActorOf(act =>
            {
                act.ReceiveAny((o, ctx) =>
                    sel.Ask<ActorIdentity>(new Identify(o), TimeSpan.FromSeconds(3)).PipeTo(sys2Probe));
            });
            s2Actor.Tell("hit");
            sys2Probe.ExpectMsg<ActorIdentity>(x => x.MessageId.Equals("hit"), TimeSpan.FromSeconds(5));
        }

        class ReporterActor : ReceiveActor
        {
            public ReporterActor(IActorRef actor) => ReceiveAny(actor.Tell);
        }

        class HashableString : IConsistentHashable
        {
            public string Str { get; }
            
            /// <inheritdoc />
            public object ConsistentHashKey { get; }

            public HashableString(string str)
            {
                Str = str;
            }
        }
        
        private static int GetFreeTcpPort()
        {
            var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }
    }
}
