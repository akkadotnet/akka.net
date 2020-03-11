//-----------------------------------------------------------------------
// <copyright file="RemotingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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

namespace Akka.Remote.Tests
{
    public class RemotingSpec : AkkaSpec
    {
        public RemotingSpec(ITestOutputHelper helper) : base(GetConfig(), helper)
        {
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

        protected string GetOtherRemoteSysConfig()
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

        private readonly ActorSystem _remoteSystem;
        private ICanTell _remote;
        private readonly ICanTell _here;

        private TimeSpan DefaultTimeout => Dilated(TestKitSettings.DefaultTimeout);


        protected override void AfterAll()
        {
            Shutdown(_remoteSystem, RemainingOrDefault);
            AssociationRegistry.Clear();
            base.AfterAll();
        }


        #region Tests


        [Fact]
        public void Remoting_must_support_remote_lookups()
        {
            _here.Tell("ping", TestActor);
            ExpectMsg(("pong", TestActor));
        }

        [Fact]
        public async Task Remoting_must_support_Ask()
        {
            //TODO: using smaller numbers for the cancellation here causes a bug.
            //the remoting layer uses some "initialdelay task.delay" for 4 seconds.
            //so the token is cancelled before the delay completed.. 
            var (msg, actorRef) = await _here.Ask<(string, IActorRef)>("ping", DefaultTimeout);
            Assert.Equal("pong", msg);
            Assert.IsType<FutureActorRef>(actorRef);
        }

        [Fact(Skip = "Racy")]
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
        public void Resolve_does_not_deadlock()
        {
            // here is really an ActorSelection
            var actorSelection = (ActorSelection)_here;
            var actorRef = actorSelection.ResolveOne(TimeSpan.FromSeconds(10)).Result;
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
        public void Remoting_must_not_send_remote_recreated_actor_with_same_name()
        {
            var echo = _remoteSystem.ActorOf(Props.Create(() => new Echo1()), "otherEcho1");
            echo.Tell(71);
            ExpectMsg(71);
            echo.Tell(PoisonPill.Instance);
            ExpectMsg("postStop");
            echo.Tell(72);
            ExpectNoMsg(TimeSpan.FromSeconds(1));

            var echo2 = _remoteSystem.ActorOf(Props.Create(() => new Echo1()), "otherEcho1");
            echo2.Tell(73);
            ExpectMsg(73);

            // msg to old IActorRef (different UID) should not get through
            echo2.Path.Uid.ShouldNotBe(echo.Path.Uid);
            echo.Tell(74);
            ExpectNoMsg(TimeSpan.FromSeconds(1));

            _remoteSystem.ActorSelection("/user/otherEcho1").Tell(75);
            ExpectMsg(75);

            Sys.ActorSelection("akka.test://remote-sys@localhost:12346/user/otherEcho1").Tell(76);
            ExpectMsg(76);
        }

        [Fact(Skip = "Racy on Azure DevOps")]
        public void Remoting_must_lookup_actors_across_node_boundaries()
        {
            Action<IActorDsl> act = dsl =>
            {
                dsl.Receive<(Props, string)>((t, ctx) => ctx.Sender.Tell(ctx.ActorOf(t.Item1, t.Item2)));
                dsl.Receive<string>((s, ctx) =>
                {
                    var sender = ctx.Sender;
                    ctx.ActorSelection(s).ResolveOne(DefaultTimeout).PipeTo(sender);
                });
            };

            var l = Sys.ActorOf(Props.Create(() => new Act(act)), "looker");

            // child is configured to be deployed on remote-sys (remoteSystem)
            l.Tell((Props.Create<Echo1>(), "child"));
            var child = ExpectMsg<IActorRef>();

            // grandchild is configured to be deployed on RemotingSpec (Sys)
            child.Tell((Props.Create<Echo1>(), "grandchild"));
            var grandchild = ExpectMsg<IActorRef>();
            grandchild.AsInstanceOf<IActorRefScope>().IsLocal.ShouldBeTrue();
            grandchild.Tell(43);
            ExpectMsg(43);
            var myRef = Sys.ActorSelection("/user/looker/child/grandchild").ResolveOne(TimeSpan.FromSeconds(3)).Result;
            (myRef is LocalActorRef).ShouldBeTrue(); // due to a difference in how ActorFor and ActorSelection are implemented, this will return a LocalActorRef
            myRef.Tell(44);
            ExpectMsg(44);
            LastSender.ShouldBe(grandchild);
            LastSender.ShouldBeSame(grandchild);
            child.AsInstanceOf<RemoteActorRef>().Parent.ShouldBe(l);

            var cRef = Sys.ActorSelection("/user/looker/child").ResolveOne(TimeSpan.FromSeconds(3)).Result;
            cRef.ShouldBe(child);
            l.Ask<IActorRef>("child/..", TimeSpan.FromSeconds(3)).Result.ShouldBe(l);
            Sys.ActorSelection("/user/looker/child").Ask<ActorSelection>(new ActorSelReq(".."), TimeSpan.FromSeconds(3))
                .ContinueWith(ts => ts.Result.ResolveOne(TimeSpan.FromSeconds(3))).Unwrap().Result.ShouldBe(l);

            Watch(child);
            child.Tell(PoisonPill.Instance);
            ExpectMsg("postStop");
            ExpectTerminated(child);
            l.Tell((Props.Create<Echo1>(), "child"));
            var child2 = ExpectMsg<IActorRef>();
            child2.Tell(45);
            ExpectMsg(45);
            // msg to old IActorRef (different uid) should not get through
            child2.Path.Uid.ShouldNotBe(child.Path.Uid);
            child.Tell(46);
            ExpectNoMsg(TimeSpan.FromSeconds(1));
            Sys.ActorSelection("user/looker/child").Tell(47);
            ExpectMsg(47);
        }

        [Fact]
        public void Remoting_must_select_actors_across_node_boundaries()
        {
            Action<IActorDsl> act = dsl =>
            {
                dsl.Receive<(Props, string)>((t, ctx) => ctx.Sender.Tell(ctx.ActorOf(t.Item1, t.Item2)));
                dsl.Receive<ActorSelReq>((req, ctx) => ctx.Sender.Tell(ctx.ActorSelection(req.S)));
            };

            var l = Sys.ActorOf(Props.Create(() => new Act(act)), "looker");
            // child is configured to be deployed on remoteSystem
            l.Tell((Props.Create<Echo1>(), "child"));
            var child = ExpectMsg<IActorRef>();
            // grandchild is configured to be deployed on RemotingSpec (system)
            child.Tell((Props.Create<Echo1>(), "grandchild"));
            var grandchild = ExpectMsg<IActorRef>();
            (grandchild as IActorRefScope).IsLocal.ShouldBeTrue();
            grandchild.Tell(53);
            ExpectMsg(53);
            var myself = Sys.ActorSelection("user/looker/child/grandchild");
            myself.Tell(54);
            ExpectMsg(54);
            LastSender.ShouldBe(grandchild);
            LastSender.ShouldBeSame(grandchild);
            myself.Tell(new Identify(myself));
            var grandchild2 = ExpectMsg<ActorIdentity>().Subject;
            grandchild2.ShouldBe(grandchild);
            Sys.ActorSelection("user/looker/child").Tell(new Identify(null));
            ExpectMsg<ActorIdentity>().Subject.ShouldBe(child);
            l.Tell(new ActorSelReq("child/.."));
            ExpectMsg<ActorSelection>().Tell(new Identify(null));
            ExpectMsg<ActorIdentity>().Subject.ShouldBeSame(l);
            Sys.ActorSelection("user/looker/child").Tell(new ActorSelReq(".."));
            ExpectMsg<ActorSelection>().Tell(new Identify(null));
            ExpectMsg<ActorIdentity>().Subject.ShouldBeSame(l);

            grandchild.Tell((Props.Create<Echo1>(), "grandgrandchild"));
            var grandgrandchild = ExpectMsg<IActorRef>();

            Sys.ActorSelection("/user/looker/child").Tell(new Identify("idReq1"));
            ExpectMsg<ActorIdentity>(i => i.MessageId.Equals("idReq1")).Subject.ShouldBe(child);
            
            Sys.ActorSelection(child.Path).Tell(new Identify("idReq2"));
            ExpectMsg<ActorIdentity>(i => i.MessageId.Equals("idReq2")).Subject.ShouldBe(child);
            Sys.ActorSelection("/user/looker/*").Tell(new Identify("idReq3"));
            ExpectMsg<ActorIdentity>(i => i.MessageId.Equals("idReq3")).Subject.ShouldBe(child);

            Sys.ActorSelection("/user/looker/child/grandchild").Tell(new Identify("idReq4"));
            ExpectMsg<ActorIdentity>(i => i.MessageId.Equals("idReq4")).Subject.ShouldBe(grandchild);
            
            Sys.ActorSelection(child.Path / "grandchild").Tell(new Identify("idReq5"));
            ExpectMsg<ActorIdentity>(i => i.MessageId.Equals("idReq5")).Subject.ShouldBe(grandchild);
            Sys.ActorSelection("/user/looker/*/grandchild").Tell(new Identify("idReq6"));
            ExpectMsg<ActorIdentity>(i => i.MessageId.Equals("idReq6")).Subject.ShouldBe(grandchild);
            Sys.ActorSelection("/user/looker/child/*").Tell(new Identify("idReq7"));
            ExpectMsg<ActorIdentity>(i => i.MessageId.Equals("idReq7")).Subject.ShouldBe(grandchild);
            
            Sys.ActorSelection(child.Path / "*").Tell(new Identify("idReq8"));
            ExpectMsg<ActorIdentity>(i => i.MessageId.Equals("idReq8")).Subject.ShouldBe(grandchild);

            Sys.ActorSelection("/user/looker/child/grandchild/grandgrandchild").Tell(new Identify("idReq9"));
            ExpectMsg<ActorIdentity>(i => i.MessageId.Equals("idReq9")).Subject.ShouldBe(grandgrandchild);
            
            Sys.ActorSelection(child.Path / "grandchild" / "grandgrandchild").Tell(new Identify("idReq10"));
            ExpectMsg<ActorIdentity>(i => i.MessageId.Equals("idReq10")).Subject.ShouldBe(grandgrandchild);
            Sys.ActorSelection("/user/looker/child/*/grandgrandchild").Tell(new Identify("idReq11"));
            ExpectMsg<ActorIdentity>(i => i.MessageId.Equals("idReq11")).Subject.ShouldBe(grandgrandchild);
            Sys.ActorSelection("/user/looker/child/*/*").Tell(new Identify("idReq12"));
            ExpectMsg<ActorIdentity>(i => i.MessageId.Equals("idReq12")).Subject.ShouldBe(grandgrandchild);
            
            Sys.ActorSelection(child.Path / "*" / "grandgrandchild").Tell(new Identify("idReq13"));
            ExpectMsg<ActorIdentity>(i => i.MessageId.Equals("idReq13")).Subject.ShouldBe(grandgrandchild);

            //ActorSelection doesn't support ToSerializationFormat directly
            //var sel1 = Sys.ActorSelection("/user/looker/child/grandchild/grandgrandchild");
            //Sys.ActorSelection(sel1.ToSerializationFormat()).Tell(new Identify("idReq18"));
            //ExpectMsg<ActorIdentity>(i => i.MessageId.Equals("idReq18")).Subject.ShouldBe(grandgrandchild);

            child.Tell(new Identify("idReq14"));
            ExpectMsg<ActorIdentity>(i => i.MessageId.Equals("idReq14")).Subject.ShouldBe(child);
            Watch(child);
            child.Tell(PoisonPill.Instance);
            ExpectMsg("postStop");
            ExpectMsg<Terminated>().ActorRef.ShouldBe(child);
            l.Tell((Props.Create<Echo1>(), "child"));
            var child2 = ExpectMsg<IActorRef>();
            child2.Tell(new Identify("idReq15"));
            ExpectMsg<ActorIdentity>(i => i.MessageId.Equals("idReq15")).Subject.ShouldBe(child2);
            
            Sys.ActorSelection(child.Path).Tell(new Identify("idReq16"));
            ExpectMsg<ActorIdentity>(i => i.MessageId.Equals("idReq16")).Subject.ShouldBe(child2);
            child.Tell(new Identify("idReq17"));
            ExpectMsg<ActorIdentity>(i => i.MessageId.Equals("idReq17")).Subject.ShouldBe(null);

            child2.Tell(55);
            ExpectMsg(55);
            // msg to old ActorRef (different uid) should not get through
            child2.Path.Uid.ShouldNotBe(child.Path.Uid);
            child.Tell(56);
            ExpectNoMsg(TimeSpan.FromSeconds(1));
            Sys.ActorSelection("user/looker/child").Tell(57);
            ExpectMsg(57);
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
                Resolve.SetResolver(new TestResolver());
                var r = Sys.ActorOf(Props.CreateBy<Resolve<Echo2>>(), "echo");
                Assert.Equal("akka.test://remote-sys@localhost:12346/remote/akka.test/RemotingSpec@localhost:12345/user/echo", r.Path.ToString());
            }
            finally
            {
                Resolve.SetResolver(null);
            }
        }

        [Fact()]
        public void Remoting_must_create_by_IndirectActorProducer_and_ping()
        {
            try
            {
                Resolve.SetResolver(new TestResolver());
                var r = Sys.ActorOf(Props.CreateBy<Resolve<Echo2>>(), "echo");
                Assert.Equal("akka.test://remote-sys@localhost:12346/remote/akka.test/RemotingSpec@localhost:12345/user/echo", r.Path.ToString());
                r.Tell("ping", TestActor);
                ExpectMsg(("pong", TestActor), TimeSpan.FromSeconds(1.5));
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
            var msg = ExpectMsg<(string, IActorRef)>();
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
            var msg = ExpectMsg<(string, IActorRef)>();
            Assert.Equal("pong", msg.Item1);
            Assert.Equal(reporter, msg.Item2);
        }

        [Fact]
        public void Stash_inbound_connections_until_UID_is_known_for_pending_outbound()
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
                AwaitCondition(() => registry.TransportsReady(rawLocalAddress, rawRemoteAddress));
                var testTransport = registry.TransportFor(rawLocalAddress).Value.Item1;
                testTransport.WriteBehavior.PushConstant(true);

                // Force an outbound associate on the real system (which we will hijack)
                // we send no handshake packet, so this remains a pending connection
                var dummySelection = thisSystem.ActorSelection(ActorPath.Parse(remoteAddress + "/user/noonethere"));
                dummySelection.Tell("ping", Sys.DeadLetters);

                var remoteHandle = remoteTransportProbe.ExpectMsg<InboundAssociation>(TimeSpan.FromMinutes(4));
                remoteHandle.Association.ReadHandlerSource.TrySetResult((IHandleEventListener)(new ActionHandleEventListener(ev => { })));

                // Now we initiate an emulated inbound connection to the real system
                var inboundHandleProbe = CreateTestProbe();
                var inboundHandleTask = remoteTransport.Associate(rawLocalAddress);
                inboundHandleTask.Wait(TimeSpan.FromSeconds(3));
                var inboundHandle = inboundHandleTask.Result;
                inboundHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(inboundHandleProbe));

                AwaitAssert(() =>
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
                inboundHandleProbe.ExpectNoMsg(1000);

                // Finish the handshake for the outbound connection - this will unstash the inbound pending connection.
                remoteHandle.Association.Write(handshakePacket);

                inboundHandleProbe.ExpectMsg<Disassociated>(TimeSpan.FromMinutes(5));
            }
            finally
            {
                Shutdown(thisSystem);
            }
        }

        [Fact]
        public void Properly_quarantine_stashed_inbound_connections()
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
                AwaitCondition(() => registry.TransportsReady(rawLocalAddress, rawRemoteAddress));
                var testTransport = registry.TransportFor(rawLocalAddress).Value.Item1;
                testTransport.WriteBehavior.PushConstant(true);

                // Force an outbound associate on the real system (which we will hijack)
                // we send no handshake packet, so this remains a pending connection
                var dummySelection = thisSystem.ActorSelection(ActorPath.Parse(remoteAddress + "/user/noonethere"));
                dummySelection.Tell("ping", Sys.DeadLetters);

                var remoteHandle = remoteTransportProbe.ExpectMsg<InboundAssociation>(TimeSpan.FromMinutes(4));
                remoteHandle.Association.ReadHandlerSource.TrySetResult((IHandleEventListener)(new ActionHandleEventListener(ev => {})));

                // Now we initiate an emulated inbound connection to the real system
                var inboundHandleProbe = CreateTestProbe();
                var inboundHandleTask = remoteTransport.Associate(rawLocalAddress);
                inboundHandleTask.Wait(TimeSpan.FromSeconds(3));
                var inboundHandle = inboundHandleTask.Result;
                inboundHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(inboundHandleProbe));

                AwaitAssert(() =>
                {
                    registry.GetRemoteReadHandlerFor(inboundHandle.AsInstanceOf<TestAssociationHandle>()).Should().NotBeNull();
                });

                var pduCodec = new AkkaPduProtobuffCodec(Sys);

                var handshakePacket = pduCodec.ConstructAssociate(new HandshakeInfo(rawRemoteAddress, remoteUID));

                // Finish the inbound handshake so now it is handed up to Remoting
                inboundHandle.Write(handshakePacket);

                // No disassociation now, the connection is still stashed
                inboundHandleProbe.ExpectNoMsg(1000);

                // Quarantine unrelated connection
                RARP.For(thisSystem).Provider.Quarantine(remoteAddress, -1);
                inboundHandleProbe.ExpectNoMsg(1000);

                // Quarantine the connection
                RARP.For(thisSystem).Provider.Quarantine(remoteAddress, remoteUID);

                // Even though the connection is stashed it will be disassociated
                inboundHandleProbe.ExpectMsg<Disassociated>();
            }
            finally
            {
                Shutdown(thisSystem);
            }
        }

        [Fact]
        public void Drop_sent_messages_over_payload_size()
        {
            var oversized = ByteStringOfSize(MaxPayloadBytes + 1);
            EventFilter.Exception<OversizedPayloadException>(start: "Discarding oversized payload sent to ").ExpectOne(() =>
            {
                VerifySend(oversized, () =>
                {
                    ExpectNoMsg();
                });
            });
        }

        [Fact]
        public void Drop_received_messages_over_payload_size()
        {
            EventFilter.Exception<OversizedPayloadException>(start: "Discarding oversized payload received").ExpectOne(() =>
            {
                VerifySend(MaxPayloadBytes + 1, () =>
                {
                    ExpectNoMsg();
                });
            });
        }

        [Fact]
        public void Nobody_should_be_converted_back_to_its_singleton()
        {
            _here.Tell(ActorRefs.Nobody, TestActor);
            ExpectMsg(ActorRefs.Nobody, TimeSpan.FromSeconds(1.5));
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
                message.Match()
                    .With<int>(i => Sender.Tell(ByteStringOfSize(i)))
                    .Default(x => Sender.Tell(x));
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

        private void VerifySend(object msg, Action afterSend)
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
                afterSend();
                ExpectNoMsg();
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
        protected new void AtStartup()
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

        public sealed class ActorSelReq
        {
            public ActorSelReq(string s)
            {
                S = s;
            }

            public string S { get; private set; }
        }

        class Reporter : UntypedActor
        {
            private IActorRef _reportTarget;

            public Reporter(IActorRef reportTarget)
            {
                _reportTarget = reportTarget;
            }


            protected override void OnReceive(object message)
            {
                _reportTarget.Forward(message);
            }
        }

        class NestedDeployer : UntypedActor
        {
            private Props _reporterProps;
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

        class Echo1 : UntypedActor
        {
            private IActorRef target = Context.System.DeadLetters;
            protected override void OnReceive(object message)
            {
                message.Match()
                    .With<(Props, string)>(props => Sender.Tell(Context.ActorOf<Echo1>(props.Item2)))
                    .With<Exception>(ex => { throw ex; })
                    .With<ActorSelReq>(sel => Sender.Tell(Context.ActorSelection(sel.S)))
                    .Default(x =>
                    {
                        target = Sender;
                        Sender.Tell(x);
                    });
            }

            protected override void PreStart() { }
            protected override void PreRestart(Exception reason, object message)
            {
                target.Tell("preRestart");
            }

            protected override void PostRestart(Exception reason) { }
            protected override void PostStop()
            {
                target.Tell("postStop");
            }

        }

        class Echo2 : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                message.Match()
                    .With<string>(str =>
                    {
                        if (str.Equals("ping")) Sender.Tell(("pong", Sender));
                    })
                    .With<(string, IActorRef)>(actorTuple =>
                    {
                        if (actorTuple.Item1.Equals("ping"))
                        {
                            Sender.Tell(("pong", actorTuple.Item2));
                        }
                        if (actorTuple.Item1.Equals("pong"))
                        {
                            actorTuple.Item2.Tell(("pong", Sender.Path.ToSerializationFormat()));
                        }
                    })
                    .Default(msg => Sender.Tell(msg));
            }
        }

        class Proxy : UntypedActor
        {
            private IActorRef _one;
            private IActorRef _another;

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

        class TestResolver : IResolver
        {
            public T Resolve<T>(object[] args)
            {
                return Activator.CreateInstance(typeof(T), args).AsInstanceOf<T>();
            }
        }

        class ActionHandleEventListener : IHandleEventListener
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

