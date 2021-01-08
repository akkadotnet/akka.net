//-----------------------------------------------------------------------
// <copyright file="ActorLookupSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Actor
{
    public class ActorLookupSpec : AkkaSpec
    {
        public sealed class Create
        {
            public Create(string child)
            {
                Child = child;
            }

            public string Child { get; private set; }
        }

        public interface IQuery { }

        public sealed class LookupElems : IQuery
        {
            public LookupElems(IEnumerable<string> path)
            {
                Path = path;
            }

            public IEnumerable<string> Path { get; private set; }
        }

        public sealed class LookupString : IQuery
        {
            public LookupString(string path)
            {
                Path = path;
            }

            public string Path { get; private set; }
        }

        public sealed class LookupPath : IQuery
        {
            public LookupPath(ActorPath path)
            {
                Path = path;
            }

            public ActorPath Path { get; private set; }
        }

        public sealed class GetSender : IQuery
        {
            public GetSender(IActorRef to)
            {
                To = to;
            }

            public IActorRef To { get; private set; }
        }

        public static readonly Props P = Props.Create<Node>();

        public class Node : ReceiveActor
        {
            private IActorRefProvider Provider { get { return Context.System.AsInstanceOf<ActorSystemImpl>().Provider; } }

            public Node()
            {
                Receive<Create>(create => Sender.Tell(Context.ActorOf(P, create.Child)));
                Receive<LookupElems>(elems => Sender.Tell(Provider.ResolveActorRef(new RootActorPath(Provider.DefaultAddress) / elems.Path)));
                Receive<LookupPath>(path => Sender.Tell(Provider.ResolveActorRef(path.Path)));
                Receive<LookupString>(str => Sender.Tell(Provider.ResolveActorRef(str.Path)));
                Receive<GetSender>(s => s.To.Tell(Sender));
            }
        }

        private IActorRef c1;
        private IActorRef c2;
        private IActorRef c21;

        protected readonly ActorSystemImpl SysImpl;
        protected IActorRefProvider Provider => SysImpl.Provider;

        private IActorRef user;
        private IActorRef syst;
        private IActorRef root;

        public ActorLookupSpec()
        {
            c1 = Sys.ActorOf(P, "c1");
            c2 = Sys.ActorOf(P, "c2");
            c21 = c2.Ask<IActorRef>(new Create("c21"), RemainingOrDefault).Result;
            SysImpl = (ActorSystemImpl) Sys;
            user = SysImpl.Guardian;
            syst = SysImpl.SystemGuardian;
            root = SysImpl.LookupRoot;
        }

        private IActorRef Empty(string path)
        {
            return new EmptyLocalActorRef(SysImpl.Provider, root.Path / path, Sys.EventStream);
        }

        [Fact]
        public void ActorSystem_must_find_actors_by_looking_up_their_path()
        {
            Provider.ResolveActorRef(c1.Path).Should().Be(c1);
            Provider.ResolveActorRef(c2.Path).Should().Be(c2);
            Provider.ResolveActorRef(c21.Path).Should().Be(c21);
            Provider.ResolveActorRef(user.Path / "c1").Should().Be(c1);
            Provider.ResolveActorRef(user.Path / "c2").Should().Be(c2);
            Provider.ResolveActorRef(user.Path / "c2" / "c21").Should().Be(c21);
        }

        [Fact]
        public void ActorSystem_must_find_Actors_by_looking_up_their_string_representation()
        {
            // this is only true for local actor references
            Provider.ResolveActorRef(c1.Path.ToString()).Should().Be(c1);
            Provider.ResolveActorRef(c2.Path.ToString()).Should().Be(c2);
            Provider.ResolveActorRef(c21.Path.ToString()).Should().Be(c21);
        }

        [Fact]
        public void ActorSystem_must_take_actor_incarnation_into_account_when_comparing_actor_references()
        {
            var name = "abcdefg";
            var a1 = Sys.ActorOf(P, name);
            Watch(a1);
            a1.Tell(PoisonPill.Instance);
            ExpectTerminated(a1);

            // let it be completely removed from the user guardian
            ExpectNoMsg(1.Seconds());

            // not equal, because it's terminated
            Provider.ResolveActorRef(a1.Path.ToString()).Should().NotBe(a1);

            var a2 = Sys.ActorOf(P, name);
            a2.Path.Should().Be(a1.Path);
            a2.Path.ToString().Should().Be(a1.Path.ToString());
            a2.Should().NotBe(a1);
            a2.ToString().Should().NotBe(a1.ToString());

            Watch(a2);
            a2.Tell(PoisonPill.Instance);
            ExpectTerminated(a2);
        }

        [Fact]
        public void ActorSystem_must_find_temporary_actors()
        {
            var f = c1.Ask(new GetSender(TestActor));
            var a = ExpectMsg<IInternalActorRef>();
            a.Path.Elements.Head().Should().Be("temp");
            Provider.ResolveActorRef(a.Path).Should().Be(a);
            Provider.ResolveActorRef(a.Path.ToString()).Should().Be(a);
            Provider.ResolveActorRef(a.Path.ToString() + "/hello").AsInstanceOf<IInternalActorRef>().IsTerminated.Should().Be(true);
            f.IsCompleted.Should().Be(false);
            a.IsTerminated.Should().Be(false);
            a.Tell(42);
            AwaitAssert(() => f.IsCompleted.Should().Be(true));
            AwaitAssert(() => f.Result.Should().Be(42));
        }

        /*
         * NOTE: the rest of the specs are not ported because Akka.NET doesn't allow any of those types of actor path lookups,
         * because the ActorFor API was deprecated in JVM Akka in version 2.2 and was thus, never ported to Akka.NET.
         * 
         * IActorRefProvider.ResolveActorRef imposes stricter requirements on lookups - all paths must be absolute with the address included.
         * 
         * Resolving from an actor context is also not necessary, since that action is never performed there. Only on the ActorRefProvider.
         */
    }
}

