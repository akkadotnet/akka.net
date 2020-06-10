//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Routing;
using Shared;

namespace System1
{
    public class ReplyActor : UntypedActor
    {
        protected override void OnReceive(object message)
        {
            Console.WriteLine("Message from {0} - {1}", Sender.Path, message);
        }
    }

    internal class Program
    {
        private static void Main(string[] args)
        {
            var config = ConfigurationFactory.ParseString(@"
akka {  
    log-config-on-start = on
    stdout-loglevel = DEBUG
    loglevel = DEBUG
    actor {
        provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
        
        debug {  
          receive = on 
          autoreceive = on
          lifecycle = on
          event-stream = on
          unhandled = on
        }

        deployment {
            /localactor {
                router = round-robin-pool
                nr-of-instances = 5
            }
            /remoteactor {
                router = round-robin-pool
                nr-of-instances = 5
                remote = ""akka.tcp://system2@localhost:8080""
            }
        }
    }
    remote {
        dot-netty.tcp {
		    port = 8090
		    hostname = localhost
        }
    }
}
");
            using (var system = ActorSystem.Create("system1", config))
            {
                var reply = system.ActorOf<ReplyActor>("reply");
                //create a local group router (see config)
                var local = system.ActorOf(Props.Create(() => new SomeActor("hello", 123)).WithRouter(FromConfig.Instance), "localactor");

                //create a remote deployed actor
                var remote = system.ActorOf(Props.Create(() => new SomeActor(null, 123)).WithRouter(FromConfig.Instance), "remoteactor");

                //these messages should reach the workers via the routed local ref
                local.Tell("Local message 1", reply);
                local.Tell("Local message 2", reply);
                local.Tell("Local message 3", reply);
                local.Tell("Local message 4", reply);
                local.Tell("Local message 5", reply);

                //this should reach the remote deployed ref
                remote.Tell("Remote message 1", reply);
                remote.Tell("Remote message 2", reply);
                remote.Tell("Remote message 3", reply);
                remote.Tell("Remote message 4", reply);
                remote.Tell("Remote message 5", reply);

                Console.ReadLine();
            }
        }
    }
}

