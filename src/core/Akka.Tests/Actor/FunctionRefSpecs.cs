//-----------------------------------------------------------------------
// <copyright file="FunctionRefSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Actor
{
    public class FunctionRefSpec : AkkaSpec
    {
        #region internal classes

        sealed class GetForwarder : IEquatable<GetForwarder>
        {
            public IActorRef ReplyTo { get; }

            public GetForwarder(IActorRef replyTo)
            {
                ReplyTo = replyTo;
            }

            public bool Equals(GetForwarder other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return Equals(ReplyTo, other.ReplyTo);
            }

            public override bool Equals(object obj) => obj is GetForwarder forwarder && Equals(forwarder);

            public override int GetHashCode() => (ReplyTo != null ? ReplyTo.GetHashCode() : 0);
        }

        sealed class DropForwarder : IEquatable<DropForwarder>
        {
            public FunctionRef Ref { get; }

            public DropForwarder(FunctionRef @ref)
            {
                Ref = @ref;
            }

            public bool Equals(DropForwarder other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return Equals(Ref, other.Ref);
            }

            public override bool Equals(object obj) => obj is DropForwarder forwarder && Equals(forwarder);

            public override int GetHashCode() => (Ref != null ? Ref.GetHashCode() : 0);
        }

        sealed class Forwarded : IEquatable<Forwarded>
        {
            public object Message { get; }
            public IActorRef Sender { get; }

            public Forwarded(object message, IActorRef sender)
            {
                Message = message;
                Sender = sender;
            }

            public bool Equals(Forwarded other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return Equals(Message, other.Message) && Equals(Sender, other.Sender);
            }

            public override bool Equals(object obj) => obj is Forwarded forwarded && Equals(forwarded);

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((Message != null ? Message.GetHashCode() : 0) * 397) ^ (Sender != null ? Sender.GetHashCode() : 0);
                }
            }
        }

        sealed class Super : ReceiveActor
        {
            public Super()
            {
                Receive<GetForwarder>(get =>
                {
                    var cell = (ActorCell)Context;
                    var fref = cell.AddFunctionRef((sender, msg) =>
                    {
                        get.ReplyTo.Tell(new Forwarded(msg, sender));
                    });
                    get.ReplyTo.Tell(fref);
                });
                Receive<DropForwarder>(drop => {
                    var cell = (ActorCell)Context;
                    cell.RemoveFunctionRef(drop.Ref);
                });
            }
        }

        sealed class SupSuper : ReceiveActor
        {
            public SupSuper()
            {
                var s = Context.ActorOf(Props.Create<Super>(), "super");
                ReceiveAny(msg => s.Tell(msg));
            }
        }

        #endregion

        public FunctionRefSpec(ITestOutputHelper output) : base(output, null)
        {
        }

        #region top level

        [Fact]
        public void FunctionRef_created_by_top_level_actor_must_forward_messages()
        {
            var s = SuperActor();
            var forwarder = GetFunctionRef(s);

            forwarder.Tell("hello");
            ExpectMsg(new Forwarded("hello", TestActor));
        }

        [Fact]
        public void FunctionRef_created_by_top_level_actor_must_be_watchable()
        {
            var s = SuperActor();
            var forwarder = GetFunctionRef(s);

            s.Tell(new GetForwarder(TestActor));
            var f = ExpectMsg<FunctionRef>();
            Watch(f);
            s.Tell(new DropForwarder(f));
            ExpectTerminated(f);
        }

        [Fact]
        public void FunctionRef_created_by_top_level_actor_must_be_able_to_watch()
        {
            var s = SuperActor();
            var forwarder = GetFunctionRef(s);

            s.Tell(new GetForwarder(TestActor));
            var f = ExpectMsg<FunctionRef>();
            forwarder.Watch(f);
            s.Tell(new DropForwarder(f));
            ExpectMsg(new Forwarded(new Terminated(f, true, false), f));
        }

        [Fact]
        public void FunctionRef_created_by_top_level_actor_must_terminate_when_their_parent_terminates()
        {
            var s = SuperActor();
            var forwarder = GetFunctionRef(s);

            Watch(forwarder);
            s.Tell(PoisonPill.Instance);
            ExpectTerminated(forwarder);
        }

        private FunctionRef GetFunctionRef(IActorRef s)
        {
            s.Tell(new GetForwarder(TestActor));
            return ExpectMsg<FunctionRef>();
        }

        private IActorRef SuperActor() => Sys.ActorOf(Props.Create<Super>(), "super");

        #endregion

        #region non-top level

        [Fact]
        public void FunctionRef_created_by_non_top_level_actor_must_forward_messages()
        {
            var s = SupSuperActor();
            var forwarder = GetFunctionRef(s);

            forwarder.Tell("hello");
            ExpectMsg(new Forwarded("hello", TestActor));
        }

        [Fact]
        public void FunctionRef_created_by_non_top_level_actor_must_be_watchable()
        {
            var s = SupSuperActor();
            var forwarder = GetFunctionRef(s);

            s.Tell(new GetForwarder(TestActor));
            var f = ExpectMsg<FunctionRef>();
            Watch(f);
            s.Tell(new DropForwarder(f));
            ExpectTerminated(f);
        }

        [Fact]
        public void FunctionRef_created_by_non_top_level_actor_must_be_able_to_watch()
        {
            var s = SupSuperActor();
            var forwarder = GetFunctionRef(s);

            s.Tell(new GetForwarder(TestActor));
            var f = ExpectMsg<FunctionRef>();
            forwarder.Watch(f);
            s.Tell(new DropForwarder(f));
            ExpectMsg(new Forwarded(new Terminated(f, true, false), f));
        }

        [Fact]
        public void FunctionRef_created_by_non_top_level_actor_must_terminate_when_their_parent_terminates()
        {
            var s = SupSuperActor();
            var forwarder = GetFunctionRef(s);

            Watch(forwarder);
            s.Tell(PoisonPill.Instance);
            ExpectTerminated(forwarder);
        }

        private IActorRef SupSuperActor() => Sys.ActorOf(Props.Create<SupSuper>(), "supsuper");

        #endregion

        [Fact(Skip = "FIXME")]
        public void FunctionRef_when_not_registered_must_not_be_found()
        {
            var provider = ((ExtendedActorSystem)Sys).Provider;
            var fref = new FunctionRef(TestActor.Path / "blabla", provider, Sys.EventStream, (x, y) => { });
            EventFilter.Exception<InvalidOperationException>().ExpectOne(() =>
            {
                // needs to be something that fails when the deserialized form is not a FunctionRef
                // this relies upon serialize-messages during tests
                TestActor.Tell(new DropForwarder(fref));
                ExpectNoMsg(TimeSpan.FromSeconds(1));
            });
        }
    }
}
