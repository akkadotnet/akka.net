﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Transport;
using Akka.Tests;
using Akka.Util;
using Google.ProtocolBuffers;
using Xunit;

namespace Akka.Remote.Tests
{
    
    public class RemotingSpec : AkkaSpec
    {
        #region Setup / Config

        public RemotingSpec()
        {
            var c1 = ConfigurationFactory.ParseString(GetConfig());
            var c2 = ConfigurationFactory.ParseString(GetOtherRemoteSysConfig());


            var conf = c2.WithFallback(c1);  //ConfigurationFactory.ParseString(GetOtherRemoteSysConfig());

            remoteSystem = ActorSystem.Create("remote-sys", conf);
            Deploy(sys, new Deploy(@"/gonk", new RemoteScope(Addr(remoteSystem, "tcp"))));
            Deploy(sys, new Deploy(@"/zagzag", new RemoteScope(Addr(remoteSystem, "udp"))));

            remote = remoteSystem.ActorOf(Props.Create<Echo2>(), "echo");
            here = sys.ActorSelection("akka.test://remote-sys@localhost:12346/user/echo");
        }

        protected override string GetConfig()
        {
            return @"
            common-helios-settings {
              port = 0
              hostname = ""localhost""
            }

            akka {
              actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""

              remote {
                transport = ""Akka.Remote.Remoting,Akka.Remote""

                retry-gate-closed-for = 1 s
                log-remote-lifecycle-events = on

                enabled-transports = [
                  ""akka.remote.test"",
                  ""akka.remote.helios.tcp"",
#""akka.remote.helios.udp""
                ]

                helios.tcp = ${common-helios-settings}
                helios.udp = ${common-helios-settings}

                test {
                  transport-class = ""Akka.Remote.Transport.TestTransport,Akka.Remote""
                  applied-adapters = []
                  registry-key = aX33k0jWKg
                  local-address = ""test://RemotingSpec@localhost:12345""
                  maximum-payload-bytes = 32000 bytes
                  scheme-identifier = test
                }
              }

              actor.deployment {
                /blub.remote = ""akka.test://remote-sys@localhost:12346""
                /looker1/child.remote = ""akka.test://remote-sys@localhost:12346""
                /looker1/child/grandchild.remote = ""akka.test://RemotingSpec@localhost:12345""
                /looker2/child.remote = ""akka.test://remote-sys@localhost:12346""
                /looker2/child/grandchild.remote = ""akka.test://RemotingSpec@localhost:12345""
              }
            }";
        }

        protected string GetOtherRemoteSysConfig()
        {
            return @"
            common-helios-settings {
              port = 0
              hostname = ""localhost""
            }

            akka {
              actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""

              remote {
                transport = ""Akka.Remote.Remoting,Akka.Remote""

                retry-gate-closed-for = 1 s
                log-remote-lifecycle-events = on

                enabled-transports = [
                  ""akka.remote.test"",
                  ""akka.remote.helios.tcp"",
#""akka.remote.helios.udp""
                ]

                helios.tcp = ${common-helios-settings}
                helios.udp = ${common-helios-settings}

                test {
                  transport-class = ""Akka.Remote.Transport.TestTransport,Akka.Remote""
                  applied-adapters = []
                  registry-key = aX33k0jWKg
                  local-address = ""test://remote-sys@localhost:12346""
                    maximum-payload-bytes = 48000 bytes
                  scheme-identifier = test
                }
              }

              actor.deployment {
                /blub.remote = ""akka.test://remote-sys@localhost:12346""
                /looker1/child.remote = ""akka.test://remote-sys@localhost:12346""
                /looker1/child/grandchild.remote = ""akka.test://RemotingSpec@localhost:12345""
                /looker2/child.remote = ""akka.test://remote-sys@localhost:12346""
                /looker2/child/grandchild.remote = ""akka.test://RemotingSpec@localhost:12345""
              }
            }";
        }

        private ActorSystem remoteSystem;
        private ICanTell remote;
        private ICanTell here;



        
        public override void Dispose()
        {
            remoteSystem.Shutdown();
            AssociationRegistry.Clear();
        }

        #endregion

        #region Tests

#if TMPFIX
        [Fact]
        public void Remoting_must_support_remote_lookups()
        {
            here.Tell("ping", testActor);
            expectMsg(Tuple.Create("pong", testActor), TimeSpan.FromSeconds(1.5));
        }
#endif

#if TMPFIX
        [Fact]
        public async Task Remoting_must_support_Ask()
        {
            //TODO: using smaller numbers for the cancellation here causes a bug.
            //the remoting layer uses some "initialdelay task.delay" for 4 seconds.
            //so the token is cancelled before the delay completed.. 
            var msg = await here.Ask<Tuple<string,ActorRef>>("ping", TimeSpan.FromSeconds(1.5));
            Assert.Equal("pong", msg.Item1);
            Assert.IsType<FutureActorRef>(msg.Item2);
        }
#endif

        [Fact]
        public void Remoting_must_create_and_supervise_children_on_remote_Node()
        {
            var r = sys.ActorOf<Echo1>("blub");
            Assert.Equal("akka.test://remote-sys@localhost:12346/remote/akka.test/RemotingSpec@localhost:12345/user/blub", r.Path.ToString());
        }

        #endregion

        #region Internal Methods

        private int MaxPayloadBytes
        {
            get
            {
                var byteSize = sys.Settings.Config.GetByteSize("akka.remote.test.maximum-payload-bytes");
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
            private readonly ActorRef _testActor;

            public Forwarder(ActorRef testActor)
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
            var bigBounceId = string.Format("bigBounce-{0}", ThreadLocalRandom.Current.Next());
            var bigBounceOther = remoteSystem.ActorOf(Props.Create<Bouncer>().WithDeploy(Actor.Deploy.Local),
                bigBounceId);

            var bigBounceHere =
                sys.ActorSelection(string.Format("akka.test://remote-sys@localhost:12346/user/{0}", bigBounceId));
            var eventForwarder = sys.ActorOf(Props.Create(() => new Forwarder(testActor)).WithDeploy(Actor.Deploy.Local));
            sys.EventStream.Subscribe(eventForwarder, typeof(AssociationErrorEvent));
            sys.EventStream.Subscribe(eventForwarder, typeof(DisassociatedEvent));
            try
            {
                bigBounceHere.Tell(msg, testActor);
                afterSend();
                expectNoMsg(TimeSpan.FromMilliseconds(500));
            }
            finally
            {
                sys.EventStream.Unsubscribe(eventForwarder, typeof(AssociationErrorEvent));
                sys.EventStream.Unsubscribe(eventForwarder, typeof(DisassociatedEvent));
                eventForwarder.Tell(PoisonPill.Instance);
                bigBounceOther.Tell(PoisonPill.Instance);
            }
        }

        private void AtStartup()
        {
            //TODO need to implement test filters first
        }



        private Address Addr(ActorSystem system, string proto)
        {
            return system.Provider.GetExternalAddressFor(new Address(string.Format("akka.{0}", proto), "", "", 0));
        }

        private int Port(ActorSystem system, string proto)
        {
            return Addr(system, proto).Port.Value;
        }

        private void Deploy(ActorSystem system, Deploy d)
        {
            system.Provider.AsInstanceOf<RemoteActorRefProvider>().Deployer.SetDeploy(d);
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

        class Echo1 : UntypedActor
        {
            private ActorRef target = Context.System.DeadLetters;
            protected override void OnReceive(object message)
            {
                message.Match()
                    .With<Tuple<Props, string>>(props => Sender.Tell(Context.ActorOf<Echo1>(props.Item2)))
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
                        if (str.Equals("ping")) Sender.Tell(Tuple.Create("pong", Sender));
                    })
                    .With<Tuple<string, ActorRef>>(actorTuple =>
                    {
                        if (actorTuple.Item1.Equals("ping"))
                        {
                            Sender.Tell(Tuple.Create("pong", actorTuple.Item2));
                        }
                        if (actorTuple.Item1.Equals("pong"))
                        {
                            actorTuple.Item2.Tell(Tuple.Create("pong", Sender.Path.ToSerializationFormat()));
                        }
                    });
            }
        }

        class Proxy : UntypedActor
        {
            private ActorRef _one;
            private ActorRef _another;

            public Proxy(ActorRef one, ActorRef another)
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

        #endregion
    }
}
