//-----------------------------------------------------------------------
// <copyright file="ParentSampleTest.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.TestKit.Xunit2;
using Xunit;

namespace DocsExamples.Testkit
{
    public class ParentSampleTest : TestKit
    {
#region ParentStructure_0
        class Parent : ReceiveActor
        {
            private IActorRef child;
            private bool ponged = false;

            public Parent()
            {
                child = Context.ActorOf(Props.Create<Child>(), "child");
                Receive<string>(str => str.Equals("pingit"), m =>
                {
                    child.Tell("ping");
                });
                Receive<string>(str => str.Equals("pong"), m =>
                {
                    ponged = true;
                });
            }
        }

        class Child : ReceiveActor
        {
            public Child()
            {
                Receive<string>(str => str.Equals("ping"), m =>
                {
                    Context.Parent.Tell("pong");
                });
            }
        }
#endregion ParentStructure_0

#region DependentChild_0
        class DependentChild : ReceiveActor
        {
            private IActorRef parent;

            public DependentChild(IActorRef parent)
            {
                this.parent = parent;

                Receive<string>(s => s.Equals("ping"), m =>
                {
                    parent.Tell("pong", Self);
                });
            }
        }
#endregion DependentChild_0

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
                d.ReceiveAny((m,c) =>
                {
                    if(c.Sender.Equals(child))
                        proxy.Ref.Forward(m);
                    else
                    {
                        child.Forward(m);
                    }
                });
            };

            var parent = Sys.ActorOf(Props.Create(() => new Act(actor)));
            proxy.Send(parent, "ping");
            proxy.ExpectMsg("pong");
#endregion FabrikatedParent_0
        }


        class GenericDependentParent : ReceiveActor
        {
#region FabrikatedParent_1
            private IActorRef child;
            private bool ponged;

            public GenericDependentParent(Func<IUntypedActorContext, IActorRef> childMaker)
            {
                child = childMaker(Context);
                ponged = false;

                Receive<string>(str => str.Equals("pingit"), m =>
                {
                    child.Tell("ping");
                });
                Receive<string>(str => str.Equals("pong"), m =>
                {
                    ponged = true;
                });
            }
#endregion FabrikatedParent_1
        }
    }
}
