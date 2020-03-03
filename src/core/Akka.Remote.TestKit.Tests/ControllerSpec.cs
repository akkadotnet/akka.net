//-----------------------------------------------------------------------
// <copyright file="ControllerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Remote.TestKit.Tests
{
    public class ControllerSpec : AkkaSpec
    {
        private const string Config = @"
            akka.testconductor.barrier-timeout = 5s
            akka.actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
            akka.actor.debug.fsm = on
            akka.actor.debug.lifecycle = on
        ";

        public ControllerSpec()
            : base(Config)
        {
        }

        private readonly RoleName A = new RoleName("a");
        private readonly RoleName B = new RoleName("b");

        [Fact]
        public void Controller_must_publish_its_nodes()
        {
            var c = Sys.ActorOf(Props.Create(() => new Controller(1, new IPEndPoint(IPAddress.Loopback, 0))));
            c.Tell(new Controller.NodeInfo(A, Address.Parse("akka://sys"), TestActor));
            ExpectMsg<ToClient<Done>>();
            c.Tell(new Controller.NodeInfo(B, Address.Parse("akka://sys"), TestActor));
            ExpectMsg<ToClient<Done>>();
            c.Tell(Controller.GetNodes.Instance);
            ExpectMsg<IEnumerable<RoleName>>(names => XAssert.Equivalent(names, new[] {A, B}));
            AwaitAssert(() =>
            {
                Watch(c);
                c.Tell(PoisonPill.Instance);
                ExpectMsg<Terminated>();
            }, TimeSpan.FromSeconds(20));
        }
    }
}

