// -----------------------------------------------------------------------
//  <copyright file="ParentSampleTest.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.TestKit.Xunit2;
using Xunit;
#pragma warning disable CS0414 // Field is assigned but its value is never used

namespace DocsExamples.Testkit;

public class ParentSampleTest : TestKit
{
    [Fact]
    public void Test_Probe_Parent_Test()
    {
        #region TestProbeChild_0

        var parent = CreateTestProbe();
        var child = parent.ChildActorOf(Props.Create<Child>());

        parent.Send(child, "ping");
        parent.ExpectMsg("pong");

        #endregion TestProbeChild_0
    }

    [Fact]
    public void Fabricated_Parent_Should_Test_Child_Responses()
    {
        #region FabrikatedParent_0

        var proxy = CreateTestProbe();
        Action<IActorDsl> actor = d =>
        {
            IActorRef child = null;
            d.OnPreStart = context => child = context.ActorOf<Child>("child");
            d.ReceiveAny((m, c) =>
            {
                if (c.Sender.Equals(child))
                    proxy.Ref.Forward(m);
                else
                    child.Forward(m);
            });
        };

        var parent = Sys.ActorOf(Props.Create(() => new Act(actor)));
        proxy.Send(parent, "ping");
        proxy.ExpectMsg("pong");

        #endregion FabrikatedParent_0
    }

    #region DependentChild_0

    private class DependentChild : ReceiveActor
    {
        private IActorRef parent;

        public DependentChild(IActorRef parent)
        {
            this.parent = parent;

            Receive<string>(s => s.Equals("ping"), _ => { parent.Tell("pong", Self); });
        }
    }

    #endregion DependentChild_0


    private class GenericDependentParent : ReceiveActor
    {
        #region FabrikatedParent_1

        private readonly IActorRef _child;
        private bool _ponged;

        public GenericDependentParent(Func<IUntypedActorContext, IActorRef> childMaker)
        {
            _child = childMaker(Context);
            _ponged = false;

            Receive<string>(str => str.Equals("pingit"), _ => { _child.Tell("ping"); });
            Receive<string>(str => str.Equals("pong"), _ => { _ponged = true; });
        }

        #endregion FabrikatedParent_1
    }

    #region ParentStructure_0

    private class Parent : ReceiveActor
    {
        private readonly IActorRef child;
        private bool ponged;

        public Parent()
        {
            child = Context.ActorOf(Props.Create<Child>(), "child");
            Receive<string>(str => str.Equals("pingit"), _ => { child.Tell("ping"); });
            Receive<string>(str => str.Equals("pong"), _ => { ponged = true; });
        }
    }

    private class Child : ReceiveActor
    {
        public Child()
        {
            Receive<string>(str => str.Equals("ping"), _ => { Context.Parent.Tell("pong"); });
        }
    }

    #endregion ParentStructure_0
}