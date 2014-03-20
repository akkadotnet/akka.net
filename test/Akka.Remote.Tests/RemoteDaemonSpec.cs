using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Akka.Tests;
using Akka.Actor;
using Akka.Configuration;
using System.Collections.Concurrent;

namespace Akka.Remote.Tests
{
    [TestClass]
    public class RemoteDaemonSpec : AkkaSpec
    {
        public class SomeActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                Context.System.EventStream.Publish(message);
            }
        }

        public class MyRemoteActor : UntypedActor
        {
            private ActorRef child = Context.ActorOf<SomeActor>("child");
            protected override void OnReceive(object message)
            {               
            }
        }

        protected override string GetConfig()
        {
            return @"
akka {  
    actor {
        provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
    }
    remote {
        server {
            host = ""127.0.0.1""
            port = 8091
        }
    }
}
";
        }

        [TestMethod]
        public void CanCreateActorUsingRemoteDaemonAndInteractWithChild()
        {           
            //var p = new TestProbe();
            //sys.EventStream.Subscribe(p.Ref, typeof(string));
            //var supervisor = sys.ActorOf<SomeActor>();
            //var provider = (RemoteActorRefProvider)sys.Provider;
            //var daemon = provider.RemoteDaemon;

            ////ask to create an actor MyRemoteActor, this actor has a child "child"
            //daemon.Tell(new DaemonMsgCreate(Props.Create(() => new MyRemoteActor()),null,"user/foo",supervisor));
            ////try to resolve the child actor "child"
            //var child = provider.ResolveActorRef(provider.RootPath / "remote/user/foo/child".Split('/'));
            ////pass a message to the child
            //child.Tell("hello");
            ////expect the child to forward the message to the eventstream
            //p.expectMsg("hello");
        }
    }
}
