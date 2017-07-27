using System;
using System.Runtime.Remoting.Messaging;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.TestKit.Xunit2;
using Akka.Actor.Dsl;
using Xunit;

namespace DocsExamples.Testkit
{
    public class ParentSampleTest : TestKit
    {
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

        [Fact]
        public void Fabricated_Parent_Should_Test_Child_Responses()
        {
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
        }


        class GenericDependentParent : ReceiveActor
        {
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
        }
    }
}