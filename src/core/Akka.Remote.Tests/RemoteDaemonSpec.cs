//-----------------------------------------------------------------------
// <copyright file="RemoteDaemonSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.TestKit;
using Xunit;

namespace Akka.Remote.Tests
{
    
    public class RemoteDaemonSpec : AkkaSpec
    {
        public RemoteDaemonSpec(): base(GetConfig()){}

        public class SomeActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                Context.System.EventStream.Publish(message);
            }
        }

        public class MyRemoteActor : UntypedActor
        {
            public MyRemoteActor(ManualResetEventSlim childCreatedEvent)
            {
                var child = Context.ActorOf<SomeActor>("child");
                childCreatedEvent.Set();
            }
            protected override void OnReceive(object message)
            {               
            }
        }

        private static string GetConfig()
        {
            return @"
akka {  
    actor {
        provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
    }
    remote {
         test-transport {
            transport-class = ""Akka.Remote.Transport.TestTransport, Akka.Remote""
            applied-adapters = []
            transport-protocol = test
            port = 891
            hostname = ""127.0.0.1""
        }
    }
}
";
        }

        [Fact]
        public void Can_create_actor_using_remote_daemon_and_interact_with_child()
        {
            var p = CreateTestProbe();
            Sys.EventStream.Subscribe(p.Ref, typeof(string));
            var supervisor = Sys.ActorOf<SomeActor>();
            var provider = (RemoteActorRefProvider)((ActorSystemImpl)Sys).Provider;
            var daemon = provider.RemoteDaemon;
            var childCreatedEvent=new ManualResetEventSlim();


            var path = (((ActorSystemImpl)Sys).Guardian.Path.Address + "/remote/user/foo").ToString();

            //ask to create an actor MyRemoteActor, this actor has a child "child"
            daemon.Tell(new DaemonMsgCreate(Props.Create(() => new MyRemoteActor(childCreatedEvent)), null, path, supervisor));

            //Wait for the child to be created (actors are instantiated async)
            childCreatedEvent.Wait();

            //try to resolve the child actor "child"
            var child = provider.ResolveActorRef(provider.RootPath / "remote/user/foo/child".Split('/'));
            //pass a message to the child
            child.Tell("hello");
            //expect the child to forward the message to the eventstream
            p.ExpectMsg("hello");
        }
    }
}

