//-----------------------------------------------------------------------
// <copyright file="RemoteDeploySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Routing;
using Akka.TestKit;
using Xunit;

namespace Akka.Remote.Tests
{
    public class RemoteDeploySpec : DualNodeSpec
    {
        public class DummyMessage
        {
            
        }

        public class RemoteEchoActor : ReceiveActor
        {
            public RemoteEchoActor()
            {
                ReceiveAny(msg =>
                {
                    Sender.Tell(msg,Self);
                });
            }
        }

        public RemoteDeploySpec()
            : base(
system1Config:@"",
system2Confg: @"
akka {
    actor.deployment {
        /echo {
            remote = ""akka.tcp://${sys1Name}@localhost:${port}""
        }
        /echorouter {
            router = round-robin-pool
            nr-of-instances = 3
            remote = ""akka.tcp://${sys1Name}@localhost:${port}""
        }        
    }
}")
        {
        }

        [Fact]
        public void Remote_deployed_actor_can_reply()
        {
            var echo = Sys2.ActorOf(Props.Create<RemoteEchoActor>(), "echo");
            echo.Tell(123, Sys2Probe);
            Sys2Probe.ExpectMsg<int>().ShouldBe(123);
        }

        [Fact]
        public void Remote_deployed_router_can_reply()
        {
            var echo = Sys2.ActorOf(Props.Create<RemoteEchoActor>().WithRouter(FromConfig.Instance), "echorouter");
            echo.Tell(123, Sys2Probe);
            Sys2Probe.ExpectMsg<int>().ShouldBe(123);
        }

        [Fact(Skip = "Remote deployed actors does not yet cause termination messages when stopped")]
        public void Remote_deployed_actor_sends_Terminated_when_stopped()
        {
            var echo = Sys2.ActorOf(Props.Create<RemoteEchoActor>(), "echo");

            Sys2Probe.Watch(echo);
            Sys2.Stop(echo);
            Sys2Probe.ExpectTerminated(echo);
        }
    }
}

