//-----------------------------------------------------------------------
// <copyright file="ActorRefSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Serialization;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Xunit;

namespace Akka.Tests.Actor
{
    public class ActorRefSpec : AkkaSpec, INoImplicitSender
    {
        [Fact]
        public void An_ActorRef_should_equal_itself()
        {
            var equalTestActorRef = new EqualTestActorRef(new RootActorPath(new Address("akka", "test")));

            equalTestActorRef.Equals(equalTestActorRef).ShouldBeTrue();
            // ReSharper disable EqualExpressionComparison
            (equalTestActorRef == equalTestActorRef).ShouldBeTrue();
            (equalTestActorRef != equalTestActorRef).ShouldBeFalse();
            // ReSharper restore EqualExpressionComparison
        }

        [Fact]
        public void An_ActorRef_should_equal_another_ActorRef_instance_with_same_path()
        {
            var actorPath1 = (new RootActorPath(new Address("akka", "test")) / "name").WithUid(4711);
            var actorPath2 = (new RootActorPath(new Address("akka", "test")) / "name").WithUid(4711);
            var equalTestActorRef1 = new EqualTestActorRef(actorPath1);
            var equalTestActorRef2 = new EqualTestActorRef(actorPath2);

            equalTestActorRef1.Equals(equalTestActorRef2).ShouldBeTrue();
            // ReSharper disable EqualExpressionComparison
            (Equals(equalTestActorRef1, equalTestActorRef2)).ShouldBeTrue();
            (!Equals(equalTestActorRef1, equalTestActorRef2)).ShouldBeFalse();
            // ReSharper restore EqualExpressionComparison
        }

        [Fact]
        public void An_ActorRef_should_not_equal_another_ActorRef_when_path_differs()
        {
            var referencePath = (new RootActorPath(new Address("akka", "test")) / "name").WithUid(4711);
            var path1 = (new RootActorPath(new Address("akka", "test")) / "name").WithUid(42);
            var path2 = (new RootActorPath(new Address("akka", "test")) / "name2").WithUid(4711);
            var path3 = (new RootActorPath(new Address("akka", "test2")) / "name").WithUid(4711);
            var refActorRef = new EqualTestActorRef(referencePath);
            var ref1 = new EqualTestActorRef(path1);
            var ref2 = new EqualTestActorRef(path2);
            var ref3 = new EqualTestActorRef(path3);

            refActorRef.Equals(ref1).ShouldBeFalse();
            refActorRef.Equals(ref2).ShouldBeFalse();
            refActorRef.Equals(ref3).ShouldBeFalse();
            // ReSharper disable EqualExpressionComparison
            (refActorRef == ref1).ShouldBeFalse();
            (refActorRef != ref1).ShouldBeTrue();
            (refActorRef == ref2).ShouldBeFalse();
            (refActorRef != ref2).ShouldBeTrue();
            (refActorRef == ref3).ShouldBeFalse();
            (refActorRef != ref3).ShouldBeTrue();
            // ReSharper restore EqualExpressionComparison
        }

        [Fact]
        public void An_ActorRef_should_not_allow_actors_to_be_created_outside_an_ActorOf()
        {
            Shutdown();
            InternalCurrentActorCellKeeper.Current = null;
            Intercept<ActorInitializationException>(() =>
            {
                new BlackHoleActor();
            });
        }


        [Fact]
        public void An_ActorRef_should_be_serializable_using_default_serialization_on_local_node()
        {
            var aref = ActorOf<BlackHoleActor>();
            var serializer = Sys.Serialization.FindSerializerFor(aref);
            var binary = serializer.ToBinary(aref);
            var bref = serializer.FromBinary(binary, typeof(IActorRef));

            bref.ShouldBe(aref);
        }

        [Fact]
        public void An_ActorRef_should_throw_an_exception_on_deserialize_if_no_system_in_scope()
        {
            var aref = ActorOf<BlackHoleActor>();

            var serializer = new NewtonSoftJsonSerializer(null);
            Intercept(() =>
            {
                var binary = serializer.ToBinary(aref);
                var bref = serializer.FromBinary(binary, typeof(IActorRef));
            });
        }

        [Fact]
        public void
            An_ActoRef_should_return_EmptyLocalActorRef_on_deserialize_if_not_present_in_actor_hierarchy_and_remoting_is_not_enabled
            ()
        {
            var aref = ActorOf<BlackHoleActor>("non-existing");
            var aserializer = Sys.Serialization.FindSerializerForType(typeof (IActorRef));
            var binary = aserializer.ToBinary(aref);

            Watch(aref);

            aref.Tell(PoisonPill.Instance);

            ExpectMsg<Terminated>();

            var bserializer = Sys.Serialization.FindSerializerForType(typeof (IActorRef));

            AwaitCondition(() =>
            {
                var bref = (IActorRef) bserializer.FromBinary(binary, typeof (IActorRef));
                try
                {
                    bref.GetType().ShouldBe(typeof (EmptyLocalActorRef));
                    bref.Path.ShouldBe(aref.Path);

                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            });
        }

        [Fact]
        public void An_ActorRef_should_restart_when_Killed()
        {
            EventFilter.Exception<ActorKilledException>().ExpectOne(() =>
            {
                var latch = CreateTestLatch(2);
                var boss = ActorOf(a =>
                {
                    var child = a.ActorOf(c =>
                    {
                        c.ReceiveAny((msg, ctx) => { });
                        c.OnPreRestart = (reason, msg, ctx) =>
                        {
                            latch.CountDown(); 
                            c.DefaultPreRestart(reason, msg);
                        };
                        c.OnPostRestart = (reason, ctx) =>
                        {
                            latch.CountDown();
                            c.DefaultPostRestart(reason);
                        };
                    });
                    a.Strategy = new OneForOneStrategy(2, TimeSpan.FromSeconds(1), r=> Directive.Restart);
                    a.Receive<string>((_, ctx) => child.Tell(Kill.Instance));
                });

                boss.Tell("send kill");
                latch.Ready(TimeSpan.FromSeconds(5));
            });
        }

        [Fact]
        public void An_ActorRef_should_support_nested_ActorOfs()
        {
            var a = Sys.ActorOf(Props.Create(() => new NestingActor(Sys)));
            var t1 = a.Ask("any");
            t1.Wait(TimeSpan.FromSeconds(3));
            var nested = t1.Result as IActorRef;

            Assert.NotNull(a);
            Assert.NotNull(nested);
            Assert.True(a != nested);
        }

        [Fact]
        public void An_ActorRef_should_support_advanced_nested_ActorOfs()
        {
            var i = Sys.ActorOf(Props.Create(() => new InnerActor()));
            var a = Sys.ActorOf(Props.Create(() => new OuterActor(i)));

            var t1 = a.Ask("innerself");
            t1.Wait(TimeSpan.FromSeconds(3));
            var inner = t1.Result as IActorRef;
            Assert.True(inner != a);

            var t2 = a.Ask(a);
            t2.Wait(TimeSpan.FromSeconds(3));
            var self = t2.Result as IActorRef;
            self.ShouldBe(a);

            var t3 = a.Ask("self");
            t3.Wait(TimeSpan.FromSeconds(3));
            var self2 = t3.Result as IActorRef;
            self2.ShouldBe(a);

            var t4 = a.Ask("msg");
            t4.Wait(TimeSpan.FromSeconds(3));
            var msg = t4.Result as string;
            msg.ShouldBe("msg");
        }

        [Fact]
        public void An_ActorRef_should_support_reply_via_Sender()
        {
            var latch = new TestLatch(4);
            var serverRef = Sys.ActorOf(Props.Create<ReplyActor>());
            var clientRef = Sys.ActorOf(Props.Create(() => new SenderActor(serverRef, latch)));

            clientRef.Tell("complex");
            clientRef.Tell("simple");
            clientRef.Tell("simple");
            clientRef.Tell("simple");

            latch.Ready(TimeSpan.FromSeconds(3));
            latch.Reset();

            clientRef.Tell("complex2");
            clientRef.Tell("simple");
            clientRef.Tell("simple");
            clientRef.Tell("simple");

            latch.Ready(TimeSpan.FromSeconds(3));
            Sys.Stop(clientRef);
            Sys.Stop(serverRef);
        }

        [Fact]
        public void An_ActorRef_should_support_ActorOfs_where_actor_class_is_not_public()
        {
            var a = Sys.ActorOf(NonPublicActor.CreateProps());
            a.Tell("pigdog", TestActor);
            ExpectMsg("pigdog");
            Sys.Stop(a);
        }

        [Fact]
        public void An_ActorRef_should_stop_when_sent_a_poison_pill()
        {
            var timeout = TimeSpan.FromSeconds(20);
            var actorRef = Sys.ActorOf(Props.Create(() => new PoisonPilledActor()));

            var t1 = actorRef.Ask(5, timeout);
            var t2 = actorRef.Ask(0, timeout);
            actorRef.Tell(PoisonPill.Instance);

            t1.Wait(timeout);
            t2.Wait(timeout);

            t1.Result.ShouldBe("five");
            t2.Result.ShouldBe("zero");

            VerifyActorTermination(actorRef);
        }

        [Fact]
        public void An_ActorRef_should_be_able_to_check_for_existence_of_the_children()
        {
            var timeout = TimeSpan.FromSeconds(3);
            var parent = Sys.ActorOf(Props.Create(() => new ChildAwareActor("child")));

            var t1 = parent.Ask("child");
            t1.Wait(timeout);
            Assert.True((bool)t1.Result);

            var t2 = parent.Ask("what");
            t2.Wait(timeout);
            Assert.True(!(bool)t2.Result);
        }

        [Fact]
        public void An_ActorRef_should_never_have_a_null_Sender_Bug_1212()
        {          
            var actor = ActorOfAsTestActorRef<NonPublicActor>(Props.Create<NonPublicActor>(SupervisorStrategy.StoppingStrategy));
            // actors with a null sender should always write to deadletters
            EventFilter.DeadLetter<object>().ExpectOne(() => actor.Tell(new object(), null));

            // will throw an exception if there's a bug
            ExpectNoMsg();
        }

        private void VerifyActorTermination(IActorRef actorRef)
        {
            var watcher = CreateTestProbe();
            watcher.Watch(actorRef);
            watcher.ExpectTerminated(actorRef, TimeSpan.FromSeconds(20));
        }

        private class NestingActor : ActorBase
        {
            internal readonly IActorRef Nested;

            public NestingActor(ActorSystem system)
            {
                Nested = system.ActorOf<BlackHoleActor>();
            }

            protected override bool Receive(object message)
            {
                Sender.Tell(Nested);
                return true;
            }
        }

        private struct ReplyTo
        {
            public ReplyTo(IActorRef sender)
                : this()
            {
                Sender = sender;
            }

            public IActorRef Sender { get; set; }
        }

        private class ReplyActor : ActorBase
        {
            private IActorRef _replyTo;

            protected override bool Receive(object message)
            {
                var type = message.ToString();
                switch (type)
                {
                    case "complexRequest":
                        {
                            _replyTo = Sender;
                            var worker = Context.ActorOf(Props.Create<WorkerActor>());
                            worker.Tell("work");
                            break;
                        }
                    case "complexRequest2":
                        {
                            var worker = Context.ActorOf(Props.Create<WorkerActor>());
                            worker.Tell(new ReplyTo(Sender));
                            break;
                        }
                    case "workDone":
                        _replyTo.Tell("complexReply");
                        break;
                    case "simpleRequest":
                        Sender.Tell("simpleReply");
                        break;
                }

                return true;
            }
        }

        private class NonPublicActor : ReceiveActor
        {
            internal static Props CreateProps()
            {
                return Props.Create<NonPublicActor>();
            }

            public NonPublicActor()
            {
                Receive<object>(msg => Sender.Tell(msg));
            }
        }

        private class WorkerActor : ReceiveActor
        {
            public WorkerActor()
            {
                Receive<string>(msg =>
                {
                    if (msg == "work")
                    {
                        Work();
                        Sender.Tell("workDone");
                        Context.Stop(Self);
                    }
                });
                Receive<ReplyTo>(msg =>
                {
                    Work();
                    msg.Sender.Tell("complexReply");
                });
            }

            private void Work()
            {
                Thread.Sleep(1000);
            }
        }

        private class SenderActor : ActorBase
        {
            private IActorRef _replyTo;
            private TestLatch _latch;

            public SenderActor(IActorRef replyTo, TestLatch latch)
            {
                _latch = latch;
                _replyTo = replyTo;
            }

            protected override bool Receive(object message)
            {
                var msg = message.ToString();
                switch (msg)
                {
                    case "complex": _replyTo.Tell("complexRequest"); break;
                    case "complex2": _replyTo.Tell("complexRequest2"); break;
                    case "simple": _replyTo.Tell("simpleRequest"); break;
                    case "complexReply": _latch.CountDown(); break;
                    case "simpleReply": _latch.CountDown(); break;
                }

                return true;
            }
        }

        private class InnerActor : ActorBase
        {
            protected override bool Receive(object message)
            {
                if (message.ToString() == "innerself")
                {
                    Sender.Tell(Self);
                }
                else
                {
                    Sender.Tell(message);
                }
                return true;
            }
        }
        private class FailingInnerActor : ActorBase
        {
            protected ActorBase Fail;

            public FailingInnerActor(ActorBase fail)
            {
                Fail = fail;
            }

            protected override bool Receive(object message)
            {
                if (message.ToString() == "innerself")
                {
                    Sender.Tell(Self);
                }
                else
                {
                    Sender.Tell(message);
                }
                return true;
            }
        }

        private class FailingChildInnerActor : FailingInnerActor
        {
            public FailingChildInnerActor(ActorBase fail)
                : base(fail)
            {
                Fail = new InnerActor();
            }
        }

        private class OuterActor : ActorBase
        {
            private readonly IActorRef _inner;

            public OuterActor(IActorRef inner)
            {
                _inner = inner;
            }

            protected override bool Receive(object message)
            {
                if (message.ToString() == "self")
                {
                    Sender.Tell(Self);
                }
                else
                {
                    _inner.Forward(message);
                }
                return true;
            }
        }

        private class FailingOuterActor : ActorBase
        {
            private readonly IActorRef _inner;
            protected ActorBase Fail;

            public FailingOuterActor(IActorRef inner)
            {
                _inner = inner;
                Fail = new InnerActor();
            }
            protected override bool Receive(object message)
            {
                if (message.ToString() == "self")
                {
                    Sender.Tell(Self);
                }
                else
                {
                    _inner.Forward(message);
                }
                return true;
            }
        }

        private class FailingChildOuterActor : FailingOuterActor
        {
            public FailingChildOuterActor(IActorRef inner)
                : base(inner)
            {
                Fail = new InnerActor();
            }
        }

        private class PoisonPilledActor : ActorBase
        {
            protected override bool Receive(object message)
            {
                if (message is int)
                {
                    var i = (int)message;
                    string msg = null;
                    if (i == 0) msg = "zero";
                    else if (i == 5) msg = "five";

                    Sender.Tell(msg);

                    return true;
                }
                return false;
            }
        }

        private class ChildAwareActor : ActorBase
        {
            private readonly IActorRef _child;

            public ChildAwareActor(string name)
            {
                _child = Context.ActorOf(Props.Create(() => new BlackHoleActor()), name);
            }

            protected override bool Receive(object message)
            {
                if (message is string)
                {
                    var name = message.ToString();
                    var child = Context.Child(name);
                    Sender.Tell(!child.IsNobody());
                    return true;
                }

                return false;
            }
        }

        private class EqualTestActorRef : ActorRefBase
        {
            private ActorPath _path;

            public EqualTestActorRef(ActorPath path)
            {
                _path = path;
            }

            public override ActorPath Path { get { return _path; } }

            protected override void TellInternal(object message, IActorRef sender)
            {
                throw new NotImplementedException();
            }
        }
    }
}

