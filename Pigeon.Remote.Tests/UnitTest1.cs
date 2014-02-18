using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pigeon.Tests;
using Pigeon.Actor;
using Pigeon.Configuration;

namespace Pigeon.Remote.Tests
{
    [TestClass]
    public class RemoteDaemonSpec : AkkaSpec
    {
        public class SomeActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {               
            }
        }
        [TestMethod]
        public void TestMethod1()
        {
            var config = ConfigurationFactory.ParseString(@"
akka {  
    actor {
        provider = ""Pigeon.Remote.RemoteActorRefProvider, Pigeon.Remote""
        debug {
            lifecycle = on
            receive = on
        }
    }
    remote {
        server {
            host = ""127.0.0.1""
            port = 8091
        }
    }
}
");
            var sys = ActorSystem.Create("foo", config);
            var supervisor = sys.ActorOf<SomeActor>();
            var provider = (RemoteActorRefProvider)sys.Provider;
            var daemon = provider.RemoteDaemon;

            daemon.Tell(new DaemonMsgCreate(Props.Create(() => new SomeActor()),null,"/user/foo",supervisor));
            //TODO: complete this test
        }
    }
}
