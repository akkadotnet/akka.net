﻿//-----------------------------------------------------------------------
// <copyright file="RemotingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Configuration;
using Akka.Remote.Transport;
using Akka.Routing;
using Akka.TestKit;
using Akka.TestKit.TestEvent;
using Akka.Util;
using Akka.Util.Internal;
using FluentAssertions;
using Google.Protobuf;
using Xunit;
using Xunit.Abstractions;
using Nito.AsyncEx;
using ThreadLocalRandom = Akka.Util.ThreadLocalRandom;
using Akka.Remote.Serialization;
using Akka.TestKit.Extensions;
using Akka.TestKit.Xunit2.Attributes;
using FluentAssertions.Extensions;

namespace Akka.Remote.Tests
{
    public class RemotingSpec : AkkaSpec
    {
        public RemotingSpec(ITestOutputHelper helper) : base(GetConfig(), helper)
        {
        }

        private static string GetConfig()
        {
            return @"
            common-helios-settings {
              port = 0
              hostname = ""localhost""
              #enforce-ip-family = true
            }

            akka {
              actor.provider = remote

              remote {
                transport = ""Akka.Remote.Remoting,Akka.Remote""
                actor.serialize-messages = off

                retry-gate-closed-for = 1 s
                log-remote-lifecycle-events = on

                enabled-transports = [
                  ""akka.remote.test"",
                  ""akka.remote.dot-netty.tcp"",
                 # ""akka.remote.dot-netty.udp""
                ]

                dot-netty.tcp = ${common-helios-settings}
                helios.udp = ${common-helios-settings}

                test {
                  transport-class = ""Akka.Remote.Transport.TestTransport,Akka.Remote""
                  applied-adapters = []
                  registry-key = aX33k0jWKg
                  local-address = ""test://RemotingSpec@localhost:12345""
                  maximum-payload-bytes = 32000b
                  scheme-identifier = test
                }
              }

              actor.deployment {
                /blub.remote = ""akka.test://remote-sys@localhost:12346""
                /echo.remote = ""akka.test://remote-sys@localhost:12346""
                /looker/child.remote = ""akka.test://remote-sys@localhost:12346""
                /looker/child/grandchild.remote = ""akka.test://RemotingSpec@localhost:12345""
              }

              test.timefactor = 2.5
            }";
        }

        private string GetOtherRemoteSysConfig()
        {
            return @"
            common-helios-settings {
              port = 0
              hostname = ""localhost""
              #enforce-ip-family = true
            }

            akka {
              actor.provider = remote

              remote {
                transport = ""Akka.Remote.Remoting,Akka.Remote""

                retry-gate-closed-for = 1 s
                log-remote-lifecycle-events = on

                enabled-transports = [
                  ""akka.remote.test"",
                  ""akka.remote.dot-netty.tcp"",
#""akka.remote.helios.udp""
                ]

                dot-netty.tcp = ${common-helios-settings}
                helios.udp = ${common-helios-settings}

                test {
                  transport-class = ""Akka.Remote.Transport.TestTransport,Akka.Remote""
                  applied-adapters = []
                  registry-key = aX33k0jWKg
                  local-address = ""test://remote-sys@localhost:12346""
                  maximum-payload-bytes = 128000b
                  scheme-identifier = test
                }
              }

              actor.deployment {
                /blub.remote = ""akka.test://remote-sys@localhost:12346""
                /looker/child.remote = ""akka.test://remote-sys@localhost:12346""
                /looker/child/grandchild.remote = ""akka.test://RemotingSpec@localhost:12345""
              }
            }";
        }

        private ActorSystem _remoteSystem;
        private ICanTell _remote;
        private ICanTell _here;

        private TimeSpan DefaultTimeout => Dilated(TestKitSettings.DefaultTimeout);

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            
            var c1 = ConfigurationFactory.ParseString(GetConfig());
            var c2 = ConfigurationFactory.ParseString(GetOtherRemoteSysConfig());
            var conf = c2.WithFallback(c1);  //ConfigurationFactory.ParseString(GetOtherRemoteSysConfig());

            _remoteSystem = ActorSystem.Create("remote-sys", conf);
            InitializeLogger(_remoteSystem);
            Deploy(Sys, new Deploy(@"/gonk", new RemoteScope(Addr(_remoteSystem, "tcp"))));
            Deploy(Sys, new Deploy(@"/zagzag", new RemoteScope(Addr(_remoteSystem, "udp"))));

            _remote = _remoteSystem.ActorOf(Props.Create<Echo2>(), "echo");
            _here = Sys.ActorSelection("akka.test://remote-sys@localhost:12346/user/echo");

            AtStartup();
        }

        protected override async Task AfterAllAsync()
        {
            if(_remoteSystem != null)
                await ShutdownAsync(_remoteSystem);
            AssociationRegistry.Clear();
            await base.AfterAllAsync();
        }

        #region Tests


        [Fact]
        public async Task Remoting_must_support_remote_lookups()
        {
            _here.Tell("ping", TestActor);
            await ExpectMsgAsync(("pong", TestActor));
        }

        [Fact]
        public async Task Remoting_must_support_Ask()
        {
            //TODO: using smaller numbers for the cancellation here causes a bug.
            //the remoting layer uses some "initialdelay task.delay" for 4 seconds.
            //so the token is cancelled before the delay completed.. 
            var (msg, actorRef) = await _here.Ask<(string, IActorRef)>("ping", DefaultTimeout);
            Assert.Equal("pong", msg);
            Assert.IsType<FutureActorRef<(string, IActorRef)>>(actorRef);
        }
        
        [LocalFact(SkipLocal = "Racy in AzDo CI/CD")]
        public async Task Ask_does_not_deadlock()
        {
            // see https://github.com/akkadotnet/akka.net/issues/2546

            // the configure await causes the continuation (== the second ask) to be scheduled on the HELIOS worker thread
            var msg = await _here.Ask<(string, IActorRef)>("ping", DefaultTimeout).ConfigureAwait(false);
            Assert.Equal("pong", msg.Item1);

            // the .Result here blocks the helios worker thread, deadlocking the whole system.
            var msg2 = _here.Ask<(string, IActorRef)>("ping", DefaultTimeout).Result;
            Assert.Equal("pong", msg2.Item1);
        }
        
        [Fact]
        public async Task Resolve_does_not_deadlock()
        {
            // here is really an ActorSelection
            var actorSelection = (ActorSelection)_here;
            Assert.True(await actorSelection.ResolveOne(10.Seconds()).AwaitWithTimeout(10.2.Seconds()), "ResolveOne failed to resolve within 10 seconds");
            // the only test is that the ResolveOne works, so if we got here, the test passes
        }

        [Fact]
        public void Resolve_does_not_deadlock_GuiApplication()
        {
            AsyncContext.Run(() =>
            {
                // here is really an ActorSelection
                var actorSelection = (ActorSelection)_here;
                var actorRef = actorSelection.ResolveOne(TimeSpan.FromSeconds(10)).Result;
                // the only test is that the ResolveOne works, so if we got here, the test passes
                return Task.Delay(0);
            });
        }

        [Fact]
        public async Task Remoting_must_not_send_remote_recreated_actor_with_same_name()
        {
            var echo = _remoteSystem.ActorOf(Props.Create(() => new Echo1()), "otherEcho1");
            echo.Tell(71);
            await ExpectMsgAsync(71);
            echo.Tell(PoisonPill.Instance);
            await ExpectMsgAsync("postStop");
            echo.Tell(72);
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));

            var echo2 = _remoteSystem.ActorOf(Props.Create(() => new Echo1()), "otherEcho1");
            echo2.Tell(73);
            await ExpectMsgAsync(73);

            // msg to old IActorRef (different UID) should not get through
            echo2.Path.Uid.ShouldNotBe(echo.Path.Uid);
            echo.Tell(74);
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));

            _remoteSystem.ActorSelection("/user/otherEcho1").Tell(75);
            await ExpectMsgAsync(75);

            Sys.ActorSelection("akka.test://remote-sys@localhost:12346/user/otherEcho1").Tell(76);
            await ExpectMsgAsync(76);
        }

        [LocalFact(SkipLocal = "Racy in AzDo CI/CD")]
        public async Task Remoting_must_lookup_actors_across_node_boundaries()
        {
            Action<IActorDsl> act = dsl =>
            {
                dsl.Receive<(Props, string)>((t, ctx) => ctx.Sender.Tell(ctx.ActorOf(t.Item1, t.Item2)));
                dsl.Receive<string>((s, ctx) =>
                {
                    var sender = ctx.Sender;
                    // NOTE: This fails if converted to ReceiveAsync and await ResolveOne(). Bug?
                    ctx.ActorSelection(s).ResolveOne(3.Seconds()).PipeTo(sender);
                });
            };

            var l = Sys.ActorOf(Props.Create(() => new Act(act)), "looker");

            // child is configured to be deployed on remote-sys (remoteSystem)
            l.Tell((Props.Create<Echo1>(), "child"));
            var child = await ExpectMsgAsync<IActorRef>();

            // grandchild is configured to be deployed on RemotingSpec (Sys)
            child.Tell((Props.Create<Echo1>(), "grandchild"));
            var grandchild = await ExpectMsgAsync<IActorRef>();
            grandchild.AsInstanceOf<IActorRefScope>().IsLocal.ShouldBeTrue();
            grandchild.Tell(43);
            await ExpectMsgAsync(43);
            var myRef = await Sys.ActorSelection("/user/looker/child/grandchild").ResolveOne(TimeSpan.FromSeconds(3));
            (myRef is LocalActorRef).ShouldBeTrue(); // due to a difference in how ActorFor and ActorSelection are implemented, this will return a LocalActorRef
            myRef.Tell(44);
            await ExpectMsgAsync(44);
            LastSender.ShouldBe(grandchild);
            LastSender.ShouldBeSame(grandchild);
            child.AsInstanceOf<RemoteActorRef>().Parent.ShouldBe(l);

            var cRef = await Sys.ActorSelection("/user/looker/child").ResolveOne(TimeSpan.FromSeconds(3));
            cRef.ShouldBe(child);
            (await l.Ask<IActorRef>("child/..", TimeSpan.FromSeconds(3))).ShouldBe(l);
            var selection = await Sys.ActorSelection("/user/looker/child").Ask<ActorSelection>(new ActorSelReq(".."), TimeSpan.FromSeconds(3));
            var resolved = await selection.ResolveOne(TimeSpan.FromSeconds(3));
            resolved.ShouldBe(l);

            Watch(child);
            child.Tell(PoisonPill.Instance);
            await ExpectMsgAsync("postStop");
            await ExpectTerminatedAsync(child);
            l.Tell((Props.Create<Echo1>(), "child"));
            var child2 = ExpectMsg<IActorRef>();
            child2.Tell(45);
            await ExpectMsgAsync(45);
            
            // msg to old IActorRef (different uid) should not get through
            child2.Path.Uid.ShouldNotBe(child.Path.Uid);
            child.Tell(46);
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
            
            Sys.ActorSelection("user/looker/child").Tell(47);
            await ExpectMsgAsync(47);
        }

        [Fact]
        public async Task Remoting_must_select_actors_across_node_boundaries()
        {
            Action<IActorDsl> act = dsl =>
            {
                dsl.Receive<(Props, string)>((t, ctx) => ctx.Sender.Tell(ctx.ActorOf(t.Item1, t.Item2)));
                dsl.Receive<ActorSelReq>((req, ctx) => ctx.Sender.Tell(ctx.ActorSelection(req.S)));
            };

            var l = Sys.ActorOf(Props.Create(() => new Act(act)), "looker");
            // child is configured to be deployed on remoteSystem
            l.Tell((Props.Create<Echo1>(), "child"));
            var child = await ExpectMsgAsync<IActorRef>();
            
            // grandchild is configured to be deployed on RemotingSpec (system)
            child.Tell((Props.Create<Echo1>(), "grandchild"));
            var grandchild = await ExpectMsgAsync<IActorRef>();
            ((IActorRefScope)grandchild).IsLocal.ShouldBeTrue();
            grandchild.Tell(53);
            await ExpectMsgAsync(53);
            
            var myself = Sys.ActorSelection("user/looker/child/grandchild");
            myself.Tell(54);
            await ExpectMsgAsync(54);
            LastSender.ShouldBe(grandchild);
            LastSender.ShouldBeSame(grandchild);
            
            myself.Tell(new Identify(myself));
            var grandchild2 = (await ExpectMsgAsync<ActorIdentity>()).Subject;
            grandchild2.ShouldBe(grandchild);
            
            Sys.ActorSelection("user/looker/child").Tell(new Identify(null));
            (await ExpectMsgAsync<ActorIdentity>()).Subject.ShouldBe(child);
            
            l.Tell(new ActorSelReq("child/.."));
            (await ExpectMsgAsync<ActorSelection>()).Tell(new Identify(null));
            (await ExpectMsgAsync<ActorIdentity>()).Subject.ShouldBeSame(l);
            
            Sys.ActorSelection("user/looker/child").Tell(new ActorSelReq(".."));
            (await ExpectMsgAsync<ActorSelection>()).Tell(new Identify(null));
            (await ExpectMsgAsync<ActorIdentity>()).Subject.ShouldBeSame(l);

            grandchild.Tell((Props.Create<Echo1>(), "grandgrandchild"));
            var grandgrandchild = await ExpectMsgAsync<IActorRef>();

            Sys.ActorSelection("/user/looker/child").Tell(new Identify("idReq1"));
            (await ExpectMsgAsync<ActorIdentity>(i => i.MessageId.Equals("idReq1")))
                .Subject.ShouldBe(child);
            
            Sys.ActorSelection(child.Path).Tell(new Identify("idReq2"));
            (await ExpectMsgAsync<ActorIdentity>(i => i.MessageId.Equals("idReq2")))
                .Subject.ShouldBe(child);
            Sys.ActorSelection("/user/looker/*").Tell(new Identify("idReq3"));
            (await ExpectMsgAsync<ActorIdentity>(i => i.MessageId.Equals("idReq3")))
                .Subject.ShouldBe(child);

            Sys.ActorSelection("/user/looker/child/grandchild").Tell(new Identify("idReq4"));
            (await ExpectMsgAsync<ActorIdentity>(i => i.MessageId.Equals("idReq4")))
                .Subject.ShouldBe(grandchild);
            
            Sys.ActorSelection(child.Path / "grandchild").Tell(new Identify("idReq5"));
            (await ExpectMsgAsync<ActorIdentity>(i => i.MessageId.Equals("idReq5"))).Subject.ShouldBe(grandchild);
            Sys.ActorSelection("/user/looker/*/grandchild").Tell(new Identify("idReq6"));
            (await ExpectMsgAsync<ActorIdentity>(i => i.MessageId.Equals("idReq6"))).Subject.ShouldBe(grandchild);
            Sys.ActorSelection("/user/looker/child/*").Tell(new Identify("idReq7"));
            (await ExpectMsgAsync<ActorIdentity>(i => i.MessageId.Equals("idReq7"))).Subject.ShouldBe(grandchild);
            
            Sys.ActorSelection(child.Path / "*").Tell(new Identify("idReq8"));
            (await ExpectMsgAsync<ActorIdentity>(i => i.MessageId.Equals("idReq8"))).Subject.ShouldBe(grandchild);

            Sys.ActorSelection("/user/looker/child/grandchild/grandgrandchild").Tell(new Identify("idReq9"));
            (await ExpectMsgAsync<ActorIdentity>(i => i.MessageId.Equals("idReq9"))).Subject.ShouldBe(grandgrandchild);
            
            Sys.ActorSelection(child.Path / "grandchild" / "grandgrandchild").Tell(new Identify("idReq10"));
            (await ExpectMsgAsync<ActorIdentity>(i => i.MessageId.Equals("idReq10"))).Subject.ShouldBe(grandgrandchild);
            Sys.ActorSelection("/user/looker/child/*/grandgrandchild").Tell(new Identify("idReq11"));
            (await ExpectMsgAsync<ActorIdentity>(i => i.MessageId.Equals("idReq11"))).Subject.ShouldBe(grandgrandchild);
            Sys.ActorSelection("/user/looker/child/*/*").Tell(new Identify("idReq12"));
            (await ExpectMsgAsync<ActorIdentity>(i => i.MessageId.Equals("idReq12"))).Subject.ShouldBe(grandgrandchild);
            
            Sys.ActorSelection(child.Path / "*" / "grandgrandchild").Tell(new Identify("idReq13"));
            (await ExpectMsgAsync<ActorIdentity>(i => i.MessageId.Equals("idReq13"))).Subject.ShouldBe(grandgrandchild);

            //ActorSelection doesn't support ToSerializationFormat directly
            //var sel1 = Sys.ActorSelection("/user/looker/child/grandchild/grandgrandchild");
            //Sys.ActorSelection(sel1.ToSerializationFormat()).Tell(new Identify("idReq18"));
            //ExpectMsg<ActorIdentity>(i => i.MessageId.Equals("idReq18")).Subject.ShouldBe(grandgrandchild);

            child.Tell(new Identify("idReq14"));
            (await ExpectMsgAsync<ActorIdentity>(i => i.MessageId.Equals("idReq14"))).Subject.ShouldBe(child);
            Watch(child);
            child.Tell(PoisonPill.Instance);
            await ExpectMsgAsync("postStop");
            (await ExpectMsgAsync<Terminated>()).ActorRef.ShouldBe(child);
            l.Tell((Props.Create<Echo1>(), "child"));
            var child2 = await ExpectMsgAsync<IActorRef>();
            child2.Tell(new Identify("idReq15"));
            (await ExpectMsgAsync<ActorIdentity>(i => i.MessageId.Equals("idReq15"))).Subject.ShouldBe(child2);
            
            Sys.ActorSelection(child.Path).Tell(new Identify("idReq16"));
            (await ExpectMsgAsync<ActorIdentity>(i => i.MessageId.Equals("idReq16"))).Subject.ShouldBe(child2);
            child.Tell(new Identify("idReq17"));
            (await ExpectMsgAsync<ActorIdentity>(i => i.MessageId.Equals("idReq17"))).Subject.ShouldBe(null);

            child2.Tell(55);
            await ExpectMsgAsync(55);
            // msg to old ActorRef (different uid) should not get through
            child2.Path.Uid.ShouldNotBe(child.Path.Uid);
            child.Tell(56);
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
            Sys.ActorSelection("user/looker/child").Tell(57);
            await ExpectMsgAsync(57);
        }

        [Fact]
        public void Remoting_must_create_and_supervise_children_on_remote_Node()
        {
            var r = Sys.ActorOf<Echo1>("blub");
            Assert.Equal("akka.test://remote-sys@localhost:12346/remote/akka.test/RemotingSpec@localhost:12345/user/blub", r.Path.ToString());
        }

        [Fact]
        public void Remoting_must_create_by_IndirectActorProducer()
        {
            try
            {
                var r = Sys.ActorOf(Props.CreateBy(new TestResolver<Echo2>()), "echo");
                Assert.Equal("akka.test://remote-sys@localhost:12346/remote/akka.test/RemotingSpec@localhost:12345/user/echo", r.Path.ToString());
            }
            finally
            {
                Resolve.SetResolver(null);
            }
        }

        [Fact]
        public async Task Remoting_must_create_by_IndirectActorProducer_and_ping()
        {
            try
            {
                var r = Sys.ActorOf(Props.CreateBy(new TestResolver<Echo2>()), "echo");
                Assert.Equal("akka.test://remote-sys@localhost:12346/remote/akka.test/RemotingSpec@localhost:12345/user/echo", r.Path.ToString());
                r.Tell("ping", TestActor);
                await ExpectMsgAsync(("pong", TestActor), TimeSpan.FromSeconds(1.5));
            }
            finally
            {
                Resolve.SetResolver(null);
            }
        }

        [Fact()]
        public async Task Bug_884_Remoting_must_support_reply_to_Routee()
        {
            var router = Sys.ActorOf(new RoundRobinPool(3).Props(Props.Create(() => new Reporter(TestActor))));
            var routees = await router.Ask<Routees>(new GetRoutees());

            //have one of the routees send the message
            var targetRoutee = routees.Members.Cast<ActorRefRoutee>().Select(x => x.Actor).First();
            _here.Tell("ping", targetRoutee);
            var msg = await ExpectMsgAsync<(string, IActorRef)>();
            Assert.Equal("pong", msg.Item1);
            Assert.Equal(targetRoutee, msg.Item2);
        }

        [Fact]
        public async Task Bug_884_Remoting_must_support_reply_to_child_of_Routee()
        {
            var props = Props.Create(() => new Reporter(TestActor));
            var router = Sys.ActorOf(new RoundRobinPool(3).Props(Props.Create(() => new NestedDeployer(props))));
            var routees = await router.Ask<Routees>(new GetRoutees());

            //have one of the routees send the message
            var targetRoutee = routees.Members.Cast<ActorRefRoutee>().Select(x => x.Actor).First();
            var reporter = await targetRoutee.Ask<IActorRef>(new NestedDeployer.GetNestedReporter());
            _here.Tell("ping", reporter);
            var msg = await ExpectMsgAsync<(string, IActorRef)>();
            Assert.Equal("pong", msg.Item1);
            Assert.Equal(reporter, msg.Item2);
        }

        [Fact]
        public async Task Stash_inbound_connections_until_UID_is_known_for_pending_outbound()
        {
            var localAddress = new Address("akka.test", "system1", "localhost", 1);
            var rawLocalAddress = new Address("test", "system1", "localhost", 1);
            var remoteAddress = new Address("akka.test", "system2", "localhost", 2);
            var rawRemoteAddress = new Address("test", "system2", "localhost", 2);

            var config = ConfigurationFactory.ParseString(@"
                  akka.remote.enabled-transports = [""akka.remote.test""]
                  akka.remote.retry-gate-closed-for = 5s     
                  akka.remote.log-remote-lifecycle-events = on
                  akka.loglevel = DEBUG
     
            akka.remote.test {
                registry-key = TRKAzR
                local-address = """ + $"test://{localAddress.System}@{localAddress.Host}:{localAddress.Port}" + @"""
            }").WithFallback(_remoteSystem.Settings.Config);

            var thisSystem = ActorSystem.Create("this-system", config);
            MuteSystem(thisSystem);

            try
            {
                // Set up a mock remote system using the test transport
                var registry = AssociationRegistry.Get("TRKAzR");
                var remoteTransport = new TestTransport(rawRemoteAddress, registry);
                var remoteTransportProbe = CreateTestProbe();

                registry.RegisterTransport(remoteTransport, Task.FromResult<IAssociationEventListener>
                    (new ActorAssociationEventListener(remoteTransportProbe)));

                // Hijack associations through the test transport
                await AwaitConditionAsync(async () => registry.TransportsReady(rawLocalAddress, rawRemoteAddress));
                var testTransport = registry.TransportFor(rawLocalAddress).Value.Item1;
                testTransport.WriteBehavior.PushConstant(true);

                // Force an outbound associate on the real system (which we will hijack)
                // we send no handshake packet, so this remains a pending connection
                var dummySelection = thisSystem.ActorSelection(ActorPath.Parse(remoteAddress + "/user/noonethere"));
                dummySelection.Tell("ping", Sys.DeadLetters);

                var remoteHandle = await remoteTransportProbe.ExpectMsgAsync<InboundAssociation>(TimeSpan.FromMinutes(4));
                remoteHandle.Association.ReadHandlerSource.TrySetResult(new ActionHandleEventListener(ev => { }));

                // Now we initiate an emulated inbound connection to the real system
                var inboundHandleProbe = CreateTestProbe();
                var inboundHandle = await remoteTransport.Associate(rawLocalAddress).WithTimeout(TimeSpan.FromSeconds(3));
                inboundHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(inboundHandleProbe));

                await AwaitAssertAsync(() =>
                {
                    registry.GetRemoteReadHandlerFor(inboundHandle.AsInstanceOf<TestAssociationHandle>()).Should().NotBeNull();
                });

                var pduCodec = new AkkaPduProtobuffCodec(Sys);

                var handshakePacket = pduCodec.ConstructAssociate(new HandshakeInfo(rawRemoteAddress, 0));
                var brokenPacket = pduCodec.ConstructPayload(ByteString.CopyFrom(0, 1, 2, 3, 4, 5, 6));

                // Finish the inbound handshake so now it is handed up to Remoting
                inboundHandle.Write(handshakePacket);
                // Now bork the connection with a malformed packet that can only signal an error if the Endpoint is already registered
                // but not while it is stashed
                inboundHandle.Write(brokenPacket);

                // No disassociation now - the connection is still stashed
                await inboundHandleProbe.ExpectNoMsgAsync(1000);

                // Finish the handshake for the outbound connection - this will unstash the inbound pending connection.
                remoteHandle.Association.Write(handshakePacket);

                await inboundHandleProbe.ExpectMsgAsync<Disassociated>(TimeSpan.FromMinutes(5));
            }
            finally
            {
                await ShutdownAsync(thisSystem);
            }
        }

        [Fact]
        public async Task Properly_quarantine_stashed_inbound_connections()
        {
            var localAddress = new Address("akka.test", "system1", "localhost", 1);
            var rawLocalAddress = new Address("test", "system1", "localhost", 1);
            var remoteAddress = new Address("akka.test", "system2", "localhost", 2);
            var rawRemoteAddress = new Address("test", "system2", "localhost", 2);
            var remoteUID = 16;

            var config = ConfigurationFactory.ParseString(@"
                  akka.remote.enabled-transports = [""akka.remote.test""]
                  akka.remote.retry-gate-closed-for = 5s     
                  akka.remote.log-remote-lifecycle-events = on  
     
            akka.remote.test {
                registry-key = JMeMndLLsw
                local-address = """ + $"test://{localAddress.System}@{localAddress.Host}:{localAddress.Port}" + @"""
            }").WithFallback(_remoteSystem.Settings.Config);

            var thisSystem = ActorSystem.Create("this-system", config);
            MuteSystem(thisSystem);

            try
            {
                // Set up a mock remote system using the test transport
                var registry = AssociationRegistry.Get("JMeMndLLsw");
                var remoteTransport = new TestTransport(rawRemoteAddress, registry);
                var remoteTransportProbe = CreateTestProbe();

                registry.RegisterTransport(remoteTransport, Task.FromResult<IAssociationEventListener>
                    (new ActorAssociationEventListener(remoteTransportProbe)));

                // Hijack associations through the test transport
                await AwaitConditionAsync(async () => registry.TransportsReady(rawLocalAddress, rawRemoteAddress));
                var testTransport = registry.TransportFor(rawLocalAddress).Value.Item1;
                testTransport.WriteBehavior.PushConstant(true);

                // Force an outbound associate on the real system (which we will hijack)
                // we send no handshake packet, so this remains a pending connection
                var dummySelection = thisSystem.ActorSelection(ActorPath.Parse(remoteAddress + "/user/noonethere"));
                dummySelection.Tell("ping", Sys.DeadLetters);

                var remoteHandle = await remoteTransportProbe.ExpectMsgAsync<InboundAssociation>(TimeSpan.FromMinutes(4));
                remoteHandle.Association.ReadHandlerSource.TrySetResult(new ActionHandleEventListener(ev => {}));

                // Now we initiate an emulated inbound connection to the real system
                var inboundHandleProbe = CreateTestProbe();
                
                var inboundHandle = await remoteTransport.Associate(rawLocalAddress).WithTimeout(TimeSpan.FromSeconds(3));
                inboundHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(inboundHandleProbe));

                await AwaitAssertAsync(() =>
                {
                    registry.GetRemoteReadHandlerFor(inboundHandle.AsInstanceOf<TestAssociationHandle>()).Should().NotBeNull();
                });

                var pduCodec = new AkkaPduProtobuffCodec(Sys);

                var handshakePacket = pduCodec.ConstructAssociate(new HandshakeInfo(rawRemoteAddress, remoteUID));

                // Finish the inbound handshake so now it is handed up to Remoting
                inboundHandle.Write(handshakePacket);

                // No disassociation now, the connection is still stashed
                await inboundHandleProbe.ExpectNoMsgAsync(1000);

                // Quarantine unrelated connection
                RARP.For(thisSystem).Provider.Quarantine(remoteAddress, -1);
                await inboundHandleProbe.ExpectNoMsgAsync(1000);

                // Quarantine the connection
                RARP.For(thisSystem).Provider.Quarantine(remoteAddress, remoteUID);

                // Even though the connection is stashed it will be disassociated
                await inboundHandleProbe.ExpectMsgAsync<Disassociated>();
            }
            finally
            {
                await ShutdownAsync(thisSystem);
            }
        }

        [Fact]
        public async Task Drop_sent_messages_over_payload_size()
        {
            var oversized = ByteStringOfSize(MaxPayloadBytes + 1);
            await EventFilter.Exception<OversizedPayloadException>(start: "Discarding oversized payload sent to ")
                .ExpectOneAsync(async () =>
                {
                    await VerifySendAsync(oversized, async () =>
                    {
                        await ExpectNoMsgAsync();
                    });
                });
        }

        [Fact]
        public async Task Drop_received_messages_over_payload_size()
        {
            await EventFilter.Exception<OversizedPayloadException>(start: "Discarding oversized payload received")
                .ExpectOneAsync(async () =>
                {
                    await VerifySendAsync(MaxPayloadBytes + 1, async () =>
                    {
                        await ExpectNoMsgAsync();
                    });
                });
        }

        [Fact]
        public async Task Nobody_should_be_converted_back_to_its_singleton()
        {
            _here.Tell(ActorRefs.Nobody, TestActor);
            await ExpectMsgAsync(ActorRefs.Nobody, TimeSpan.FromSeconds(1.5));
        }

        #endregion

        #region Internal Methods

        private int MaxPayloadBytes
        {
            get
            {
                var byteSize = Sys.Settings.Config.GetByteSize("akka.remote.test.maximum-payload-bytes", null);
                if (byteSize != null)
                    return (int)byteSize.Value;
                return 0;
            }
        }

        private class Bouncer : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                switch (message)
                {
                    case int i:
                        Sender.Tell(ByteStringOfSize(i));
                        break;
                    default:
                        Sender.Tell(message);
                        break;
                }
            }
        }

        private class Forwarder : UntypedActor
        {
            private readonly IActorRef _testActor;

            public Forwarder(IActorRef testActor)
            {
                _testActor = testActor;
            }

            protected override void OnReceive(object message)
            {
                _testActor.Tell(message);
            }
        }

        private static ByteString ByteStringOfSize(int size)
        {
            return ByteString.CopyFrom(new byte[size]);
        }

        private async Task VerifySendAsync(object msg, Func<Task> afterSend)
        {
            var bigBounceId = $"bigBounce-{ThreadLocalRandom.Current.Next()}";
            var bigBounceOther = _remoteSystem.ActorOf(Props.Create<Bouncer>().WithDeploy(Actor.Deploy.Local),
                bigBounceId);

            var bigBounceHere =
                Sys.ActorSelection($"akka.test://remote-sys@localhost:12346/user/{bigBounceId}");
            var eventForwarder = Sys.ActorOf(Props.Create(() => new Forwarder(TestActor)).WithDeploy(Actor.Deploy.Local));
            Sys.EventStream.Subscribe(eventForwarder, typeof(AssociationErrorEvent));
            Sys.EventStream.Subscribe(eventForwarder, typeof(DisassociatedEvent));
            try
            {
                bigBounceHere.Tell(msg, TestActor);
                await afterSend();
                await ExpectNoMsgAsync();
            }
            finally
            {
                Sys.EventStream.Unsubscribe(eventForwarder, typeof(AssociationErrorEvent));
                Sys.EventStream.Unsubscribe(eventForwarder, typeof(DisassociatedEvent));
                eventForwarder.Tell(PoisonPill.Instance);
                bigBounceOther.Tell(PoisonPill.Instance);
            }
        }
        
        /// <summary>
        /// Have to hide other method otherwise we get an NRE due to base class
        /// constructor being called first.
        /// </summary>
        private new void AtStartup()
        {
            MuteSystem(Sys);
            _remoteSystem.EventStream.Publish(EventFilter.Error(start: "AssociationError").Mute());
            // OversizedPayloadException inherits from EndpointException, so have to mute it for now
            //_remoteSystem.EventStream.Publish(EventFilter.Exception<EndpointException>().Mute());
        }

        private void MuteSystem(ActorSystem system)
        {
            system.EventStream.Publish(EventFilter.Error(start: "AssociationError").Mute());
            system.EventStream.Publish(EventFilter.Warning(start: "AssociationError").Mute());
            system.EventStream.Publish(EventFilter.Warning(contains: "dead letter").Mute());
        }

        private Address Addr(ActorSystem system, string proto)
        {
            return ((ExtendedActorSystem)system).Provider.GetExternalAddressFor(new Address($"akka.{proto}", "", "", 0));
        }

        private int Port(ActorSystem system, string proto)
        {
            return Addr(system, proto).Port.Value;
        }

        private void Deploy(ActorSystem system, Deploy d)
        {
            ((ExtendedActorSystem)system).Provider.AsInstanceOf<RemoteActorRefProvider>().Deployer.SetDeploy(d);
        }

        #endregion

        #region Messages and Internal Actors

        private sealed class ActorSelReq
        {
            public ActorSelReq(string s)
            {
                S = s;
            }

            public string S { get; }
        }

        private class Reporter : UntypedActor
        {
            private readonly IActorRef _reportTarget;

            public Reporter(IActorRef reportTarget)
            {
                _reportTarget = reportTarget;
            }


            protected override void OnReceive(object message)
            {
                _reportTarget.Forward(message);
            }
        }

        private class NestedDeployer : UntypedActor
        {
            private readonly Props _reporterProps;
            private IActorRef _reporterActorRef;

            public class GetNestedReporter { }

            public NestedDeployer(Props reporterProps)
            {
                _reporterProps = reporterProps;
            }

            protected override void PreStart()
            {
                _reporterActorRef = Context.ActorOf(_reporterProps);
            }

            protected override void OnReceive(object message)
            {
                if (message is GetNestedReporter)
                {
                    Sender.Tell(_reporterActorRef);
                }
                else
                {
                    Unhandled(message);
                }
            }
        }

        private class Echo1 : UntypedActor
        {
            private IActorRef _target = Context.System.DeadLetters;
            protected override void OnReceive(object message)
            {
                switch (message)
                {
                    case ValueTuple<Props, string> props:
                        Sender.Tell(Context.ActorOf<Echo1>(props.Item2));
                        break;
                    case Exception ex:
                        throw ex;
                    case ActorSelReq sel:
                        Sender.Tell(Context.ActorSelection(sel.S));
                        break;
                    default:
                        _target = Sender;
                        Sender.Tell(message);
                        break;
                }
            }

            protected override void PreStart() { }
            protected override void PreRestart(Exception reason, object message)
            {
                _target.Tell("preRestart");
            }

            protected override void PostRestart(Exception reason) { }
            protected override void PostStop()
            {
                _target.Tell("postStop");
            }

        }

        private class Echo2 : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                switch (message)
                {
                    case string str:
                        if (str.Equals("ping")) Sender.Tell(("pong", Sender));
                        break;
                    case ValueTuple<string, IActorRef> actorTuple:
                        switch (actorTuple.Item1)
                        {
                            case "ping":
                                Sender.Tell(("pong", actorTuple.Item2));
                                break;
                            case "pong":
                                actorTuple.Item2.Tell(("pong", Sender.Path.ToSerializationFormat()));
                                break;
                        }
                        break;
                    default:
                        Sender.Tell(message);
                        break;
                }
            }
        }

        private class Proxy : UntypedActor
        {
            private readonly IActorRef _one;
            private readonly IActorRef _another;

            public Proxy(IActorRef one, IActorRef another)
            {
                _one = one;
                _another = another;
            }

            protected override void OnReceive(object message)
            {
                if (Sender.Path.Equals(_one.Path)) _another.Tell(message);
                if (Sender.Path.Equals(_another.Path)) _one.Tell(message);
            }
        }

        private class TestResolver<TActor> : IIndirectActorProducer where TActor:ActorBase
        {
            public Type ActorType => typeof(TActor);
            private readonly object[] _args;

            public TestResolver(params object[] args)
            {
                _args = args;
            }

            public ActorBase Produce()
            {
                return (ActorBase)Activator.CreateInstance(ActorType, _args);
            }

            public void Release(ActorBase actor)
            {
                
            }
        }

        private class ActionHandleEventListener : IHandleEventListener
        {
            private readonly Action<IHandleEvent> _handler;

            public ActionHandleEventListener() : this(ev => { }) { }

            public ActionHandleEventListener(Action<IHandleEvent> handler)
            {
                _handler = handler;
            }

            public void Notify(IHandleEvent ev)
            {
                _handler(ev);
            }
        }


        #endregion
    }
}

