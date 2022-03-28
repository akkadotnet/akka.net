//-----------------------------------------------------------------------
// <copyright file="ActorSelectionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
using System.Threading.Tasks;
using Akka.Util;
using static FluentAssertions.FluentActions;

namespace Akka.Tests.Actor
{

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

        private async Task<IActorRef> Identify(ActorSelection selection)
        {
            var idProbe = CreateTestProbe();
            selection.Tell(new Identify(selection), idProbe.Ref);
            var result = (await idProbe.ExpectMsgAsync<ActorIdentity>()).Subject;
            var asked = await selection.Ask<ActorIdentity>(new Identify(selection));
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

        private async Task<IActorRef> Identify(string path) => await Identify(Sys.ActorSelection(path));

        private async Task<IActorRef> Identify(ActorPath path) => await Identify(Sys.ActorSelection(path));

        private async Task<IActorRef> AskNode(IActorRef node, IQuery query)
        {
            var result = await node.Ask(query);

            if (result is IActorRef actorRef)
                return actorRef;

            return result is ActorSelection selection ? await Identify(selection) : null;
        }

        [Fact]
        public async Task An_ActorSystem_must_select_actors_by_their_path()
        {
            var c1 = await Identify(_c1.Path);
            c1.ShouldBe(_c1);

            var c2 = await Identify(_c2.Path);
            c2.ShouldBe(_c2);

            var c21 = await Identify(_c21.Path);
            c21.ShouldBe(_c21);
            
            c1 = await Identify("user/c1");
            c1.ShouldBe(_c1);
            
            c2 = await Identify("user/c2");
            c2.ShouldBe(_c2);

            c21 = await Identify("user/c2/c21");
            c21.ShouldBe(_c21);
        }

        [Fact]
        public async Task An_ActorSystem_must_select_actors_by_their_string_path_representation()
        {
            var c1 = await Identify(_c1.Path.ToString());
            c1.ShouldBe(_c1);
            
            var c2 = await Identify(_c2.Path.ToString());
            c2.ShouldBe(_c2);

            var c21 = await Identify(_c21.Path.ToString());
            c21.ShouldBe(_c21);

            c1 = await Identify(_c1.Path.ToStringWithoutAddress());
            c1.ShouldBe(_c1);
            
            c2 = await Identify(_c2.Path.ToStringWithoutAddress());
            c2.ShouldBe(_c2);

            c21 = await Identify(_c21.Path.ToStringWithoutAddress());
            c21.ShouldBe(_c21);
        }

        [Fact]
        public async Task An_ActorSystem_must_take_actor_incarnation_into_account_when_comparing_actor_references()
        {
            const string name = "abcdefg";
            var a1 = Sys.ActorOf(Props, name);
            Watch(a1);
            a1.Tell(PoisonPill.Instance);
            var msg = await ExpectMsgAsync<Terminated>();
            msg.ActorRef.ShouldBe(a1);

            //not equal because it's terminated
            var id = await Identify(a1.Path);
            id.ShouldBe(null);

            var a2 = Sys.ActorOf(Props, name);
            a2.Path.ShouldBe(a1.Path);
            a2.Path.ToString().ShouldBe(a1.Path.ToString());
            a2.ShouldNotBe(a1);
            a2.ToString().ShouldNotBe(a1.ToString());

            Watch(a2);
            a2.Tell(PoisonPill.Instance);
            msg = await ExpectMsgAsync<Terminated>();
            msg.ActorRef.ShouldBe(a2);
        }

        [Fact]
        public async Task An_ActorSystem_must_select_actors_by_their_root_anchored_relative_path()
        {
            var actorRef = await Identify(_c1.Path.ToStringWithoutAddress());
            actorRef.ShouldBe(_c1);
            
            actorRef = await Identify(_c2.Path.ToStringWithoutAddress());
            actorRef.ShouldBe(_c2);
            
            actorRef = await Identify(_c21.Path.ToStringWithoutAddress());
            actorRef.ShouldBe(_c21);
        }

        [Fact]
        public async Task An_ActorSystem_must_select_actors_by_their_relative_path()
        {
            var c1 = await Identify(_c1.Path.Elements.Join("/"));
            c1.ShouldBe(_c1);
            
            var c2 = await Identify(_c2.Path.Elements.Join("/"));
            c2.ShouldBe(_c2);

            var c21 = await Identify(_c21.Path.Elements.Join("/"));
            c21.ShouldBe(_c21);
        }

        [Fact]
        public async Task An_ActorSystem_must_select_system_generated_actors()
        {
            var user = await Identify("/user");
            user.ShouldBe(User);
            
            var system = await Identify("/system");
            system.ShouldBe(System);
            
            system = await Identify(System.Path);
            system.ShouldBe(System);
            
            system = await Identify(System.Path.ToStringWithoutAddress());
            system.ShouldBe(System);
            
            var root = await Identify("/");
            root.ShouldBe(Root);
            
            //We return Nobody for an empty path 
            //Identify("").ShouldBe(Root);
            var nobody = await Identify("");
            nobody.ShouldBe(Nobody.Instance);
            
            root = await Identify(new RootActorPath(Root.Path.Address));
            root.ShouldBe(Root);
            
            root = await Identify("..");
            root.ShouldBe(Root);

            root = await Identify(Root.Path);
            root.ShouldBe(Root);
            
            root = await Identify(Root.Path.ToStringWithoutAddress());
            root.ShouldBe(Root);
            
            user = await Identify("user");
            user.ShouldBe(User);
            
            system = await Identify("system");
            system.ShouldBe(System);
            
            user = await Identify("user/");
            user.ShouldBe(User);
            
            system = await Identify("system/");
            system.ShouldBe(System);
        }

        [Fact]
        public async Task An_ActorSystem_must_return_ActorIdentity_None_respectively_for_non_existing_paths_and_DeadLetters()
        {
            var none = await Identify("a/b/c");
            none.ShouldBe(null);
            
            none = await Identify("a/b/c");
            none.ShouldBe(null);
            
            none = await Identify("akka://all-systems/Nobody");
            none.ShouldBe(null);
            
            none = await Identify("akka://all-systems/user");
            none.ShouldBe(null);
            
            none = await Identify("user/hallo");
            none.ShouldBe(null);
            
            var nobody = await Identify("foo://user");
            nobody.ShouldBe(Nobody.Instance);
            
            nobody = await Identify("/deadLetters");
            nobody.ShouldBe(Nobody.Instance);
            
            nobody = await Identify("deadLetters");
            nobody.ShouldBe(Nobody.Instance);
            
            nobody = await Identify("deadLetters/");
            nobody.ShouldBe(Nobody.Instance);
        }


        [Fact]
        public async Task An_ActorContext_must_select_actors_by_their_path()
        {
            async Task Check(IActorRef looker, IActorRef result)
            {
                var node = await AskNode(looker, new SelectPath(result.Path));
                node.ShouldBe(result);
            }

            foreach (var l in _all)
                foreach (var r in _all)
                    await Check(l, r);
        }

        [Fact]
        public async Task An_ActorContext_must_select_actor_by_their_string_path_representation()
        {
            async Task Check(IActorRef looker, IActorRef result)
            {
                var node = await AskNode(looker, new SelectString(result.Path.ToStringWithoutAddress()));
                node.ShouldBe(result);
                
                // with trailing /
                node = await AskNode(looker, new SelectString(result.Path.ToStringWithoutAddress() + "/"));
                node.ShouldBe(result);
            }

            foreach (var l in _all)
                foreach (var r in _all)
                    await Check(l, r);
        }

        [Fact]
        public async Task An_ActorContext_must_select_actors_by_their_root_anchored_relative_path()
        {
            async Task Check(IActorRef looker, IActorRef result)
            {
                var node = await AskNode(looker, new SelectString(result.Path.ToStringWithoutAddress()));
                node.ShouldBe(result);
                
                node = await AskNode(looker, new SelectString("/" + result.Path.Elements.Join("/") + "/"));
                node.ShouldBe(result);
            }

            foreach (var l in _all)
                foreach (var r in _all)
                    await Check(l, r);
        }

        [Fact]
        public async Task An_ActorContext_must_select_actors_by_their_relative_path()
        {
            async Task Check(IActorRef looker, IActorRef result, string[] elements)
            {
                var node = await AskNode(looker, new SelectString(elements.Join("/")));
                node.ShouldBe(result);
                
                node = await AskNode(looker, new SelectString(elements.Join("/") + "/"));
                node.ShouldBe(result);
            }

            await Check(_c1, User, new[] { ".." });

            foreach (var l in new[] { _c1, _c2 })
                foreach (var r in _all)
                {
                    var elements = new List<string> { ".." };
                    elements.AddRange(r.Path.Elements.Drop(1));
                    await Check(l, r, elements.ToArray());
                }

            await Check(_c21, User, new[] { "..", ".." });
            await Check(_c21, Root, new[] { "..", "..", ".." });
            await Check(_c21, Root, new[] { "..", "..", "..", ".." });
        }

        [Fact]
        public async Task An_ActorContext_must_find_system_generated_actors()
        {
            async Task Check(IActorRef target)
            {
                foreach (var looker in _all)
                {
                    var node = await AskNode(looker, new SelectPath(target.Path));
                    node.ShouldBe(target);
                    
                    node = await AskNode(looker, new SelectString(target.Path.ToString()));
                    node.ShouldBe(target);
                    
                    node = await AskNode(looker, new SelectString(target.Path + "/"));
                    node.ShouldBe(target);
                }
                if (!Equals(target, Root))
                {
                    var node = await AskNode(_c1, new SelectString("../../" + target.Path.Elements.Join("/") + "/"));
                    node.ShouldBe(target);
                }
            }

            foreach (var actorRef in new[] { Root, System, User })
            {
                await Check(actorRef);
            }
        }

        [Fact]
        public async Task An_ActorContext_must_return_deadLetters_or_ActorIdentity_None_respectively_for_non_existing_paths()
        {
            async Task CheckOne(IActorRef looker, IQuery query)
            {
                var lookup = await AskNode(looker, query);
                lookup.ShouldBe(null);
            }

            async Task Check(IActorRef looker)
            {
                var queries = new IQuery[]
                {
                    new SelectString("a/b/c"),
                    new SelectString("akka://all-systems/Nobody"),
                    new SelectPath(User.Path / "hallo"),
                    new SelectPath(looker.Path / "hallo"),
                    new SelectPath(looker.Path / new []{"a","b"}),
                };
                
                foreach (var query in queries)
                {
                    await CheckOne(looker, query);
                }    
            }

            foreach (var actorRef in _all)
            {
                await Check(actorRef);
            }
        }


        [Fact]
        public async Task An_ActorSelection_must_send_messages_directly()
        {
            new ActorSelection(_c1, "").Tell(new GetSender(TestActor));
            await ExpectMsgAsync(TestActor);
            LastSender.ShouldBe(_c1);
        }

        [Fact]
        public async Task An_ActorSelection_must_send_messages_to_string_path()
        {
            Sys.ActorSelection("/user/c2/c21").Tell(new GetSender(TestActor));
            await ExpectMsgAsync(TestActor);
            LastSender.ShouldBe(_c21);
        }

        [Fact]
        public async Task An_ActorSelection_must_send_messages_to_actor_path()
        {
            Sys.ActorSelection(_c2.Path / "c21").Tell(new GetSender(TestActor));
            await ExpectMsgAsync(TestActor);
            LastSender.ShouldBe(_c21);
        }

        [Fact]
        public async Task An_ActorSelection_must_send_messages_with_correct_sender()
        {
            new ActorSelection(_c21, "../../*").Tell(new GetSender(TestActor), _c1);
            //Three messages because the selection includes the TestActor, GetSender -> TestActor + response from c1 and c2 to TestActor
            var actors = (await ReceiveWhileAsync(_ => LastSender, msgs: 3).ToListAsync()).Distinct();
            actors.Should().BeEquivalentTo(_c1, _c2);
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public async Task An_ActorSelection_must_drop_messages_which_cannot_be_delivered()
        {
            new ActorSelection(_c21, "../../*/c21").Tell(new GetSender(TestActor), _c2);

            var actors = (await ReceiveWhileAsync(_ => LastSender, msgs: 2).ToListAsync()).Distinct();
            actors.Should().HaveCount(1).And.Subject.First().ShouldBe(_c21);
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));

        }

        [Fact]
        public async Task An_ActorSelection_must_resolve_one_actor_with_timeout()
        {
            var s = Sys.ActorSelection("user/c2");
            (await s.ResolveOne(Dilated(TimeSpan.FromSeconds(1)))).ShouldBe(_c2);
        }

        [Fact]
        public async Task An_ActorSelection_must_resolve_non_existing_with_failure()
        {
            await Awaiting(async () =>
            {
                await Sys.ActorSelection("user/none").ResolveOne(Dilated(TimeSpan.FromSeconds(1)));
            }).Should().ThrowAsync<ActorNotFoundException>();
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
        public async Task An_ActorSelection_must_send_ActorSelection_targeted_to_missing_actor_to_deadLetters()
        {
            var p = CreateTestProbe();
            Sys.EventStream.Subscribe(p.Ref, typeof(DeadLetter));
            Sys.ActorSelection("/user/missing").Tell("boom", TestActor);
            var d = await p.ExpectMsgAsync<DeadLetter>();
            d.Message.ShouldBe("boom");
            d.Sender.ShouldBe(TestActor);
            d.Recipient.Path.ToStringWithoutAddress().ShouldBe("/user/missing");
        }

        [Theory]
        [InlineData("/user/foo/*/bar")]
        [InlineData("/user/foo/bar/*")]
        public async Task Bugfix3420_A_wilcard_ActorSelection_that_selects_no_actors_must_go_to_DeadLetters(string actorPathStr)
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
            var msg = await ExpectMsgAsync<DeadLetter>();
            msg.Message.Should().Be("foo");

            actorA.Tell("foo");
            msg = await ExpectMsgAsync<DeadLetter>();
            msg.Message.Should().Be("foo");
        }

        [Fact]
        public async Task An_ActorSelection_must_identify_actors_with_wildcard_selection_correctly()
        {
            var creator = CreateTestProbe();
            var top = Sys.ActorOf(Props, "a");
            var b1 = await top.Ask<IActorRef>(new Create("b1"), TimeSpan.FromSeconds(3));
            var b2 = await top.Ask<IActorRef>(new Create("b2"), TimeSpan.FromSeconds(3));
            var c = await b2.Ask<IActorRef>(new Create("c"), TimeSpan.FromSeconds(3));
            var d = await c.Ask<IActorRef>(new Create("d"), TimeSpan.FromSeconds(3));

            var probe = CreateTestProbe();
            Sys.ActorSelection("/user/a/*").Tell(new Identify(1), probe.Ref);
            var received = await probe.ReceiveNAsync(2, default)
                .Cast<ActorIdentity>()
                .Select(i => i.Subject)
                .ToListAsync();
            received.Should().BeEquivalentTo(new[] { b1, b2 });
            await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));

            Sys.ActorSelection("/user/a/b1/*").Tell(new Identify(2), probe.Ref);
            var identity = await probe.ExpectMsgAsync<ActorIdentity>();
            identity.Should().BeEquivalentTo(new ActorIdentity(2, null));

            Sys.ActorSelection("/user/a/*/c").Tell(new Identify(3), probe.Ref);
            identity = await probe.ExpectMsgAsync<ActorIdentity>();
            identity.Should().BeEquivalentTo(new ActorIdentity(3, c));
            await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));

            Sys.ActorSelection("/user/a/b2/*/d").Tell(new Identify(4), probe.Ref);
            identity = await probe.ExpectMsgAsync<ActorIdentity>();
            identity.Should().BeEquivalentTo(new ActorIdentity(4, d));
            await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));

            Sys.ActorSelection("/user/a/*/*/d").Tell(new Identify(5), probe.Ref);
            identity = await probe.ExpectMsgAsync<ActorIdentity>();
            identity.Should().BeEquivalentTo(new ActorIdentity(5, d));
            await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));

            Sys.ActorSelection("/user/a/*/c/*").Tell(new Identify(6), probe.Ref);
            identity = await probe.ExpectMsgAsync<ActorIdentity>();
            identity.Should().BeEquivalentTo(new ActorIdentity(6, d));
            await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));

            Sys.ActorSelection("/user/a/b2/*/d/e").Tell(new Identify(7), probe.Ref);
            identity = await probe.ExpectMsgAsync<ActorIdentity>();
            identity.Should().BeEquivalentTo(new ActorIdentity(7, null));
            await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));

            Sys.ActorSelection("/user/a/*/c/d/e").Tell(new Identify(8), probe.Ref);
            await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500));
        }

        [Fact]
        public async Task An_ActorSelection_must_identify_actors_with_double_wildcard_selection_correctly()
        {
            var creator = CreateTestProbe();
            var top = Sys.ActorOf(Props, "a");
            var b1 = await top.Ask<IActorRef>(new Create("b1"), TimeSpan.FromSeconds(3));
            var b2 = await top.Ask<IActorRef>(new Create("b2"), TimeSpan.FromSeconds(3));
            var b3 = await top.Ask<IActorRef>(new Create("b3"), TimeSpan.FromSeconds(3));
            var c1 = await b2.Ask<IActorRef>(new Create("c1"), TimeSpan.FromSeconds(3));
            var c2 = await b2.Ask<IActorRef>(new Create("c2"), TimeSpan.FromSeconds(3));
            var d = await c1.Ask<IActorRef>(new Create("d"), TimeSpan.FromSeconds(3));

            var probe = CreateTestProbe();

            // grab everything below /user/a
            Sys.ActorSelection("/user/a/**").Tell(new Identify(1), probe.Ref);
            var received = await probe.ReceiveNAsync(6, default)
                .Cast<ActorIdentity>()
                .Select(i => i.Subject)
                .ToListAsync();
            received.Should().BeEquivalentTo(b1, b2, b3, c1, c2, d);
            await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500));

            // grab everything below /user/a/b2
            Sys.ActorSelection("/user/a/b2/**").Tell(new Identify(2), probe.Ref);
            received = await probe.ReceiveNAsync(3, default)
                .Cast<ActorIdentity>()
                .Select(i => i.Subject)
                .ToListAsync();
            received.Should().BeEquivalentTo(c1, c2, d);
            await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500));

            // nothing under /user/a/b2/c1/d
            Sys.ActorSelection("/user/a/b2/c1/d/**").Tell(new Identify(3), probe.Ref);
            await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500));

            Invoking(() => Sys.ActorSelection("/user/a/**/d").Tell(new Identify(4), probe.Ref))
                .Should().Throw<IllegalActorNameException>();
        }

        [Fact]
        public async Task An_ActorSelection_must_forward_to_selection()
        {
            _c2.Tell(new Forward("c21", "hello"), TestActor);
            await ExpectMsgAsync("hello");
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

