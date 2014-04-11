using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Tests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Akka.Remote.Tests
{
    [TestClass]
    public class RemotingSpec : AkkaSpec
    {
        #region Setup / Config

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
                ]

                helios.tcp = $${common-helios-settings}

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

        private ActorSystem remoteSystem;

        public override void Setup()
        {
            base.Setup();
            var conf = ConfigurationFactory.ParseString(@"
                akka.remote.test{
                    local-address = ""test://remote-sys@localhost:12346""
                    maximum-payload-bytes = 48000 bytes
                }
            ").WithFallback(sys.Settings.Config);

            remoteSystem = ActorSystem.Create("remote-sys", conf);
        }

        #endregion

        #region Internal Methods

        private void Deploy(ActorSystem sys, Deploy d)
        {
            //sys.Provider.AsInstanceOf<RemoteActorRefProvider>().Deployer.
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
