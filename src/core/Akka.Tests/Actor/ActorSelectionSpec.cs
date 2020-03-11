//-----------------------------------------------------------------------
// <copyright file="ActorSelectionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Actor.Dsl;
using Akka.Event;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Actor
{
    using Akka.Util;

    public class ActorSelectionSpec : AkkaSpec
    {
        private const string Config = @"
            akka.test.default-timeout = 5 s
            akka.loglevel=DEBUG
            ";

        private readonly IActorRef _c1;
        private readonly IActorRef _c2;
        private readonly IActorRef _c21;
        private readonly IActorRef[] _all;

        public ActorSelectionSpec() : base(Config)
        {
            _c1 = Sys.ActorOf(Props, "c1");
            _c2 = Sys.ActorOf(Props, "c2");
            _c21 = _c2.Ask<IActorRef>(new Create("c21")).Result;

            _all = new[] { _c1, _c2, _c21 };
        }

        private ActorSystemImpl SystemImpl => Sys as ActorSystemImpl;
        private IInternalActorRef User => SystemImpl.Guardian;
        private IInternalActorRef System => SystemImpl.SystemGuardian;
        private IInternalActorRef Root => SystemImpl.LookupRoot;

        private IActorRef Identify(ActorSelection selection)
        {
            var idProbe = CreateTestProbe();
            selection.Tell(new Identify(selection), idProbe.Ref);
            var result = idProbe.ExpectMsg<ActorIdentity>().Subject;
            var asked = selection.Ask<ActorIdentity>(new Identify(selection)).Result;
            asked.Subject.ShouldBe(result);
            asked.MessageId.ShouldBe(selection);
            IActorRef resolved;
            try
            {
                resolved = selection.ResolveOne(TimeSpan.FromSeconds(3)).Result;
            }
            catch
            {
                resolved = null;
            }
            resolved.ShouldBe(result);
            return result;
        }

        private IActorRef Identify(string path) => Identify(Sys.ActorSelection(path));

        private IActorRef Identify(ActorPath path) => Identify(Sys.ActorSelection(path));

        private IActorRef AskNode(IActorRef node, IQuery query)
        {
            var result = node.Ask(query).Result;

            var actorRef = result as IActorRef;
            if (actorRef != null)
                return actorRef;

            var selection = result as ActorSelection;
            return selection != null ? Identify(selection) : null;
        }

        [Fact]
        public void An_ActorSystem_must_select_actors_by_their_path()
        {
            Identify(_c1.Path).ShouldBe(_c1);
            Identify(_c2.Path).ShouldBe(_c2);
            Identify(_c21.Path).ShouldBe(_c21);
            Identify("user/c1").ShouldBe(_c1);
            Identify("user/c2").ShouldBe(_c2);
            Identify("user/c2/c21").ShouldBe(_c21);
        }

        [Fact]
        public void An_ActorSystem_must_select_actors_by_their_string_path_representation()
        {
            Identify(_c1.Path.ToString()).ShouldBe(_c1);
            Identify(_c2.Path.ToString()).ShouldBe(_c2);
            Identify(_c21.Path.ToString()).ShouldBe(_c21);

            Identify(_c1.Path.ToStringWithoutAddress()).ShouldBe(_c1);
            Identify(_c2.Path.ToStringWithoutAddress()).ShouldBe(_c2);
            Identify(_c21.Path.ToStringWithoutAddress()).ShouldBe(_c21);
        }

        [Fact]
        public void An_ActorSystem_must_take_actor_incarnation_into_account_when_comparing_actor_references()
        {
            const string name = "abcdefg";
            var a1 = Sys.ActorOf(Props, name);
            Watch(a1);
            a1.Tell(PoisonPill.Instance);
            ExpectMsg<Terminated>().ActorRef.ShouldBe(a1);

            //not equal because it's terminated
            Identify(a1.Path).ShouldBe(null);

            var a2 = Sys.ActorOf(Props, name);
            a2.Path.ShouldBe(a1.Path);
            a2.Path.ToString().ShouldBe(a1.Path.ToString());
            a2.ShouldNotBe(a1);
            a2.ToString().ShouldNotBe(a1.ToString());

            Watch(a2);
            a2.Tell(PoisonPill.Instance);
            ExpectMsg<Terminated>().ActorRef.ShouldBe(a2);
        }

        [Fact]
        public void An_ActorSystem_must_select_actors_by_their_root_anchored_relative_path()
        {
            Identify(_c1.Path.ToStringWithoutAddress()).ShouldBe(_c1);
            Identify(_c2.Path.ToStringWithoutAddress()).ShouldBe(_c2);
            Identify(_c21.Path.ToStringWithoutAddress()).ShouldBe(_c21);
        }

        [Fact]
        public void An_ActorSystem_must_select_actors_by_their_relative_path()
        {
            Identify(_c1.Path.Elements.Join("/")).ShouldBe(_c1);
            Identify(_c2.Path.Elements.Join("/")).ShouldBe(_c2);
            Identify(_c21.Path.Elements.Join("/")).ShouldBe(_c21);
        }

        [Fact]
        public void An_ActorSystem_must_select_system_generated_actors()
        {
            Identify("/user").ShouldBe(User);
            Identify("/system").ShouldBe(System);
            Identify(System.Path).ShouldBe(System);
            Identify(System.Path.ToStringWithoutAddress()).ShouldBe(System);
            Identify("/").ShouldBe(Root);
            //We return Nobody for an empty path 
            //Identify("").ShouldBe(Root);
            Identify("").ShouldBe(Nobody.Instance);
            Identify(new RootActorPath(Root.Path.Address)).ShouldBe(Root);
            Identify("..").ShouldBe(Root);
            Identify(Root.Path).ShouldBe(Root);
            Identify(Root.Path.ToStringWithoutAddress()).ShouldBe(Root);
            Identify("user").ShouldBe(User);
            Identify("system").ShouldBe(System);
            Identify("user/").ShouldBe(User);
            Identify("system/").ShouldBe(System);
        }

        [Fact]
        public void An_ActorSystem_must_return_ActorIdentity_None_respectively_for_non_existing_paths_and_DeadLetters()
        {
            Identify("a/b/c").ShouldBe(null);
            Identify("a/b/c").ShouldBe(null);
            Identify("akka://all-systems/Nobody").ShouldBe(null);
            Identify("akka://all-systems/user").ShouldBe(null);
            Identify("user/hallo").ShouldBe(null);
            Identify("foo://user").ShouldBe(Nobody.Instance);
            Identify("/deadLetters").ShouldBe(Nobody.Instance);
            Identify("deadLetters").ShouldBe(Nobody.Instance);
            Identify("deadLetters/").ShouldBe(Nobody.Instance);
        }


        [Fact]
        public void An_ActorContext_must_select_actors_by_their_path()
        {
            Action<IActorRef, IActorRef> check =
                (looker, result) => AskNode(looker, new SelectPath(result.Path)).ShouldBe(result);

            foreach (var l in _all)
                foreach (var r in _all)
                    check(l, r);
        }

        [Fact]
        public void An_ActorContext_must_select_actor_by_their_string_path_representation()
        {
            Action<IActorRef, IActorRef> check = (looker, result) =>
            {
                AskNode(looker, new SelectString(result.Path.ToStringWithoutAddress())).ShouldBe(result);
                // with trailing /
                AskNode(looker, new SelectString(result.Path.ToStringWithoutAddress() + "/")).ShouldBe(result);
            };

            foreach (var l in _all)
                foreach (var r in _all)
                    check(l, r);
        }

        [Fact]
        public void An_ActorContext_must_select_actors_by_their_root_anchored_relative_path()
        {
            Action<IActorRef, IActorRef> check = (looker, result) =>
            {
                AskNode(looker, new SelectString(result.Path.ToStringWithoutAddress())).ShouldBe(result);
                AskNode(looker, new SelectString("/" + result.Path.Elements.Join("/") + "/")).ShouldBe(result);
            };

            foreach (var l in _all)
                foreach (var r in _all)
                    check(l, r);
        }

        [Fact]
        public void An_ActorContext_must_select_actors_by_their_relative_path()
        {
            Action<IActorRef, IActorRef, string[]> check = (looker, result, elements) =>
            {
                AskNode(looker, new SelectString(elements.Join("/"))).ShouldBe(result);
                AskNode(looker, new SelectString(elements.Join("/") + "/")).ShouldBe(result);
            };

            check(_c1, User, new[] { ".." });

            foreach (var l in new[] { _c1, _c2 })
                foreach (var r in _all)
                {
                    var elements = new List<string> { ".." };
                    elements.AddRange(r.Path.Elements.Drop(1));
                    check(l, r, elements.ToArray());
                }

            check(_c21, User, new[] { "..", ".." });
            check(_c21, Root, new[] { "..", "..", ".." });
            check(_c21, Root, new[] { "..", "..", "..", ".." });
        }

        [Fact]
        public void An_ActorContext_must_find_system_generated_actors()
        {
            Action<IActorRef> check = target =>
            {
                foreach (var looker in _all)
                {
                    AskNode(looker, new SelectPath(target.Path)).ShouldBe(target);
                    AskNode(looker, new SelectString(target.Path.ToString())).ShouldBe(target);
                    AskNode(looker, new SelectString(target.Path.ToString() + "/")).ShouldBe(target);
                }
                if (!Equals(target, Root))
                    AskNode(_c1, new SelectString("../../" + target.Path.Elements.Join("/") + "/")).ShouldBe(target);
            };

            new[] { Root, System, User }.ForEach(check);
        }

        [Fact]
        public void An_ActorContext_must_return_deadLetters_or_ActorIdentity_None_respectively_for_non_existing_paths()
        {
            Action<IActorRef, IQuery> checkOne = (looker, query) =>
            {
                var lookup = AskNode(looker, query);
                lookup.ShouldBe(null);
            };

            Action<IActorRef> check = looker =>
            {
                new IQuery[]
                {
                    new SelectString("a/b/c"),
                    new SelectString("akka://all-systems/Nobody"),
                    new SelectPath(User.Path / "hallo"),
                    new SelectPath(looker.Path / "hallo"),
                    new SelectPath(looker.Path / new []{"a","b"}),
                }.ForEach(t => checkOne(looker, t));
            };

            _all.ForEach(check);
        }


        [Fact]
        public void An_ActorSelection_must_send_messages_directly()
        {
            new ActorSelection(_c1, "").Tell(new GetSender(TestActor));
            ExpectMsg(TestActor);
            LastSender.ShouldBe(_c1);
        }

        [Fact]
        public void An_ActorSelection_must_send_messages_to_string_path()
        {
            Sys.ActorSelection("/user/c2/c21").Tell(new GetSender(TestActor));
            ExpectMsg(TestActor);
            LastSender.ShouldBe(_c21);
        }

        [Fact]
        public void An_ActorSelection_must_send_messages_to_actor_path()
        {
            Sys.ActorSelection(_c2.Path / "c21").Tell(new GetSender(TestActor));
            ExpectMsg(TestActor);
            LastSender.ShouldBe(_c21);
        }

        [Fact]
        public void An_ActorSelection_must_send_messages_with_correct_sender()
        {
            new ActorSelection(_c21, "../../*").Tell(new GetSender(TestActor), _c1);
            //Three messages because the selection includes the TestActor, GetSender -> TestActor + response from c1 and c2 to TestActor
            var actors = ReceiveWhile(_ => LastSender, msgs: 3).Distinct();
            actors.ShouldAllBeEquivalentTo(new[] { _c1, _c2 });
            ExpectNoMsg(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void An_ActorSelection_must_drop_messages_which_cannot_be_delivered()
        {
            new ActorSelection(_c21, "../../*/c21").Tell(new GetSender(TestActor), _c2);

            var actors = ReceiveWhile(_ => LastSender, msgs: 2).Distinct();
            actors.Should().HaveCount(1).And.Subject.First().ShouldBe(_c21);
            ExpectNoMsg(TimeSpan.FromSeconds(1));

        }

        [Fact]
        public void An_ActorSelection_must_resolve_one_actor_with_timeout()
        {
            var s = Sys.ActorSelection("user/c2");
            s.ResolveOne(Dilated(TimeSpan.FromSeconds(1))).Result.ShouldBe(_c2);
        }

        [Fact]
        public void An_ActorSelection_must_resolve_non_existing_with_failure()
        {
            var task = Sys.ActorSelection("user/none").ResolveOne(Dilated(TimeSpan.FromSeconds(1)));
            task.Invoking(t => t.Wait()).ShouldThrow<ActorNotFoundException>();
        }

        [Fact]
        public void An_ActorSelection_must_compare_equally()
        {
            new ActorSelection(_c21, "../*/hello").ShouldBe(new ActorSelection(_c21, "../*/hello"));
            new ActorSelection(_c21, "../*/hello").GetHashCode().ShouldBe(new ActorSelection(_c21, "../*/hello").GetHashCode());
            new ActorSelection(_c2, "../*/hello").ShouldNotBe(new ActorSelection(_c21, "../*/hello"));
            new ActorSelection(_c2, "../*/hello").GetHashCode().ShouldNotBe(new ActorSelection(_c21, "../*/hello").GetHashCode());
            new ActorSelection(_c21, "../*/hell").ShouldNotBe(new ActorSelection(_c21, "../*/hello"));
            new ActorSelection(_c21, "../*/hell").GetHashCode().ShouldNotBe(new ActorSelection(_c21, "../*/hello").GetHashCode());
        }

        [Fact]
        public void An_ActorSelection_must_print_nicely()
        {
            var expected = $"ActorSelection[Anchor(akka://{Sys.Name}/user/c2/c21#{_c21.Path.Uid}), Path(/../*/hello)]";
            new ActorSelection(_c21, "../*/hello").ToString().ShouldBe(expected);
        }

        [Fact(Skip = "ActorSelection doesn't support serializable format by itself")]
        public void An_ActorSelection_must_have_a_stringly_serializable_path()
        {

        }

        [Fact]
        public void An_ActorSelection_must_send_ActorSelection_targeted_to_missing_actor_to_deadLetters()
        {
            var p = CreateTestProbe();
            Sys.EventStream.Subscribe(p.Ref, typeof(DeadLetter));
            Sys.ActorSelection("/user/missing").Tell("boom", TestActor);
            var d = p.ExpectMsg<DeadLetter>();
            d.Message.ShouldBe("boom");
            d.Sender.ShouldBe(TestActor);
            d.Recipient.Path.ToStringWithoutAddress().ShouldBe("/user/missing");
        }

        [Theory]
        [InlineData("/user/foo/*/bar")]
        [InlineData("/user/foo/bar/*")]
        public void Bugfix3420_A_wilcard_ActorSelection_that_selects_no_actors_must_go_to_DeadLetters(string actorPathStr)
        {
            var actorA = Sys.ActorOf(act =>
            {
                act.ReceiveAny((o, context) =>
                {
                    context.ActorSelection(actorPathStr).Tell(o);
                });
            }, "a");

            Sys.EventStream.Subscribe(TestActor, typeof(DeadLetter));

            // deliver two ActorSelections - one from outside any actors, one from inside
            // they have different anchors to start with, so the results may differ
            Sys.ActorSelection(actorPathStr).Tell("foo");
            ExpectMsg<DeadLetter>().Message.Should().Be("foo");

            actorA.Tell("foo");
            ExpectMsg<DeadLetter>().Message.Should().Be("foo");
        }

        [Fact]
        public void An_ActorSelection_must_identify_actors_with_wildcard_selection_correctly()
        {
            var creator = CreateTestProbe();
            var top = Sys.ActorOf(Props, "a");
            var b1 = top.Ask<IActorRef>(new Create("b1"), TimeSpan.FromSeconds(3)).Result;
            var b2 = top.Ask<IActorRef>(new Create("b2"), TimeSpan.FromSeconds(3)).Result;
            var c = b2.Ask<IActorRef>(new Create("c"), TimeSpan.FromSeconds(3)).Result;
            var d = c.Ask<IActorRef>(new Create("d"), TimeSpan.FromSeconds(3)).Result;

            var probe = CreateTestProbe();
            Sys.ActorSelection("/user/a/*").Tell(new Identify(1), probe.Ref);
            probe.ReceiveN(2)
                .Cast<ActorIdentity>()
                .Select(i => i.Subject)
                .ShouldAllBeEquivalentTo(new[] { b1, b2 });
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(200));

            Sys.ActorSelection("/user/a/b1/*").Tell(new Identify(2), probe.Ref);
            probe.ExpectMsg<ActorIdentity>().ShouldBeEquivalentTo(new ActorIdentity(2, null));

            Sys.ActorSelection("/user/a/*/c").Tell(new Identify(3), probe.Ref);
            probe.ExpectMsg<ActorIdentity>().ShouldBeEquivalentTo(new ActorIdentity(3, c));
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(200));

            Sys.ActorSelection("/user/a/b2/*/d").Tell(new Identify(4), probe.Ref);
            probe.ExpectMsg<ActorIdentity>().ShouldBeEquivalentTo(new ActorIdentity(4, d));
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(200));

            Sys.ActorSelection("/user/a/*/*/d").Tell(new Identify(5), probe.Ref);
            probe.ExpectMsg<ActorIdentity>().ShouldBeEquivalentTo(new ActorIdentity(5, d));
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(200));

            Sys.ActorSelection("/user/a/*/c/*").Tell(new Identify(6), probe.Ref);
            probe.ExpectMsg<ActorIdentity>().ShouldBeEquivalentTo(new ActorIdentity(6, d));
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(200));

            Sys.ActorSelection("/user/a/b2/*/d/e").Tell(new Identify(7), probe.Ref);
            probe.ExpectMsg<ActorIdentity>().ShouldBeEquivalentTo(new ActorIdentity(7, null));
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(200));

            Sys.ActorSelection("/user/a/*/c/d/e").Tell(new Identify(8), probe.Ref);
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
        }

        [Fact]
        public void An_ActorSelection_must_forward_to_selection()
        {
            _c2.Tell(new Forward("c21", "hello"), TestActor);
            ExpectMsg("hello");
            LastSender.ShouldBe(_c21);
        }

        [Theory]
        [InlineData("worker", "w.rker", false)]
        [InlineData("w..ker", "w..ker", true)]
        [InlineData("w^rker", "w^rker", true)]
        [InlineData("w$rker", "w$rker", true)]
        [InlineData("w$rk", "w$rker", false)]
        [InlineData("work.r", "*.r", true)]
        [InlineData("work.r", "*k.", false)]
        [InlineData("work.r", "*k.r", true)]
        [InlineData("worker", "*ke", false)]
        [InlineData("worker", "*ker", true)]
        [InlineData("worker", "*ker*", true)]
        [InlineData("worker", "^worker", false)]
        [InlineData("worker", "^worker$", false)]
        [InlineData("worker", "ker*", false)]
        [InlineData("worker", "worker", true)]
        [InlineData("worker", "worker$", false)]
        [InlineData("worker", "worker*", true)]
        public void Selections_are_implicitly_anchored(string data, string pattern, bool isMatch)
        {
            var predicate = isMatch ? "matches" : "does not match";
            WildcardMatch.Like(data, pattern).ShouldBe(isMatch, $"'{pattern}' {predicate} '{data}'");
        }

        #region Test classes

        private sealed class Create
        {
            public Create(string child)
            {
                Child = child;
            }

            public string Child { get; }
        }

        private interface IQuery { }

        private sealed class SelectString : IQuery
        {
            public SelectString(string path)
            {
                Path = path;
            }

            public string Path { get; }
        }

        private sealed class SelectPath : IQuery
        {
            public SelectPath(ActorPath path)
            {
                Path = path;
            }

            public ActorPath Path { get; }
        }

        private sealed class GetSender : IQuery
        {
            public GetSender(IActorRef to)
            {
                To = to;
            }

            public IActorRef To { get; }
        }

        private sealed class Forward : IQuery
        {
            public Forward(string path, object message)
            {
                Path = path;
                Message = message;
            }

            public string Path { get; }

            public object Message { get; }
        }

        private static readonly Props Props = Props.Create<Node>();

        private sealed class Node : ReceiveActor
        {
            public Node()
            {
                Receive<Create>(c => Sender.Tell(Context.ActorOf(Props, c.Child)));
                Receive<SelectString>(s => Sender.Tell(Context.ActorSelection(s.Path)));
                Receive<SelectPath>(s => Sender.Tell(Context.ActorSelection(s.Path)));
                Receive<GetSender>(g => g.To.Tell(Sender));
                Receive<Forward>(f => Context.ActorSelection(f.Path).Tell(f.Message, Sender));
                ReceiveAny(msg => Sender.Tell(msg));
            }
        }

        #endregion
    }
}

