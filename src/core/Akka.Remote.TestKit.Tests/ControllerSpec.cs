//-----------------------------------------------------------------------
// <copyright file="ControllerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Akka.Util;
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

        private readonly RoleName A = new("a");
        private readonly RoleName B = new("b");

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

        [Fact(DisplayName = "Controller should keep a re-registered node when its previous connection reports a disconnect")]
        public async Task Controller_must_keep_a_re_registered_node_when_the_previous_connection_disconnects()
        {
            var address = Address.Parse("akka://sys");
            var c = Sys.ActorOf(Props.Create(() => new Controller(1, new IPEndPoint(IPAddress.Loopback, 0))));
            var oldConnection = CreateTestProbe();
            var newConnection = CreateTestProbe();

            oldConnection.Send(c, new Controller.NodeInfo(A, address, oldConnection.Ref));
            await oldConnection.ExpectMsgAsync<ToClient<Done>>();

            // Tear the node down the way TestConductor.Shutdown does, then let it come back on a
            // fresh connection under the same role, the way StartNewSystem does.
            c.Tell(new Terminate(A, new Left<bool, int>(true)));
            await oldConnection.ExpectMsgAsync<ToClient<TerminateMsg>>();

            newConnection.Send(c, new Controller.NodeInfo(A, address, newConnection.Ref));
            await newConnection.ExpectMsgAsync<ToClient<Done>>();

            // The connection that has already been replaced now reports its disconnect. It must
            // not evict the registration that replaced it.
            oldConnection.Send(c, new Controller.ClientDisconnected(A));

            c.Tell(Controller.GetNodes.Instance);
            var nodes = await ExpectMsgAsync<IEnumerable<RoleName>>();
            Assert.Contains(A, nodes.ToList());

            // The barrier coordinator has to still know the node as well, otherwise its arrivals
            // are ignored and every later barrier stalls.
            newConnection.Send(c, new EnterBarrier("after-restart", null, A));
            var result = await newConnection.ExpectMsgAsync<ToClient<BarrierResult>>();
            Assert.True(result.Msg.Success);
        }
    }
}

