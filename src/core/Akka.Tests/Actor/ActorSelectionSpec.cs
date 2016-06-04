//-----------------------------------------------------------------------
// <copyright file="ActorSelectionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Xunit;

namespace Akka.Tests.Actor
{
    
    public class ActorSelectionSpec : AkkaSpec
    {
        // ReSharper disable NotAccessedField.Local
        private IActorRef _echoActor;
        private IActorRef _selectionTestActor;
        // ReSharper restore NotAccessedField.Local

        public ActorSelectionSpec()
            : base("akka.test.default-timeout = 5 s")
        {
            _echoActor = Sys.ActorOf(EchoActor.Props(this), "echo");
            _selectionTestActor = CreateTestActor("test");
        }

        [Fact]
        public void Can_resolve_child_path()
        {
            var selection = Sys.ActorSelection("user/test");
            selection.Tell("hello");
            ExpectMsg("hello");
        }

        [Fact]
        public void Can_resolve_absolute_path()
        {
            var actorPath = new RootActorPath(TestActor.Path.Address) / "user" / "test";
            var selection = Sys.ActorSelection(actorPath);
            selection.Tell("hello");
            ExpectMsg("hello");
        }

        [Fact]
        public void Can_resolve_up_and_down_path()
        {
            var selection = Sys.ActorSelection("user/test/../../user/test");
            selection.Tell("hello");
            ExpectMsg("hello");
        }

        [Fact]
        public void Can_resolve_wildcard_partial_asterisk()
        {
            Sys.ActorSelection("user/te*st").Tell("hello1");
            ExpectMsg("hello1");
        }

        [Fact]
        public void Can_resolve_wildcard_full_asterisk()
        {
            Sys.ActorSelection("user/*").Tell("hello3");
            ExpectMsg("hello3");
        }

        [Fact]
        public void Can_resolve_wildcard_question_mark()
        {
            Sys.ActorSelection("user/t?st").Tell("hello2");
            ExpectMsg("hello2");
        }

        [Fact]
        public void Can_not_resolve_wildcard_when_no_match()
        {
            Sys.ActorSelection("user/foo*").Tell("hello3");
            ExpectNoMsg(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void Can_Ask_actor_selection()
        {
            var selection = Sys.ActorSelection("user/echo");
            var task = selection.Ask("hello");
            ExpectMsg("hello");
            task.Wait();
            Assert.Equal("hello", task.Result);
        }

        [Fact]
        public async Task Can_ResolveOne()
        {
            var selection = Sys.ActorSelection("user/test");
            var one = await selection.ResolveOne(TimeSpan.FromSeconds(1));
            Assert.NotNull(one);
        }

        [Fact]
        public async Task Can_not_ResolveOne_when_no_match()
        {
            var selection = Sys.ActorSelection("user/nonexisting");

            //xUnit 2 will have Assert.ThrowsAsync<TException>();
            await AkkaSpecExtensions.ThrowsAsync<ActorNotFoundException>(async () => await selection.ResolveOne(TimeSpan.FromSeconds(1)));  
        }

        [Fact]
        public void ActorSelection_must_send_ActorSelection_targeted_to_missing_actor_to_DeadLetters()
        {
            var p = CreateTestProbe();
            Sys.EventStream.Subscribe(p.Ref, typeof(DeadLetter));
            Sys.ActorSelection("/user/missing").Tell("boom", TestActor);
            var d = p.ExpectMsg<DeadLetter>();
            d.Message.ShouldBe("boom");
            d.Sender.ShouldBe(TestActor);
            d.Recipient.Path.ToStringWithoutAddress().ShouldBe("/user/missing");
        }

        #region Tests for verifying that ActorSelections made within an ActorContext can be resolved

        /// <summary>
        /// Accepts a Tuple containing a string representation of an ActorPath and a message, respectively
        /// </summary>
        public class ActorContextSelectionActor : TypedActor, IHandle<Tuple<string, string>>
        {
            public void Handle(Tuple<string, string> message)
            {
                var testActorSelection = Context.ActorSelection(message.Item1);
                testActorSelection.Tell(message.Item2);
            }
        }

        [Fact]
        public void Can_resolve_absolute_actor_path_in_actor_context()
        {
            var contextActor = Sys.ActorOf<ActorContextSelectionActor>();
            contextActor.Tell(new Tuple<string,string>("/user/test", "hello"));
            ExpectMsg("hello");
        }

        [Fact]
        public void Can_resolve_relative_actor_path_in_actor_context()
        {
            var contextActor = Sys.ActorOf<ActorContextSelectionActor>();
            contextActor.Tell(new Tuple<string, string>("../test/../../user/test", "hello"));
            ExpectMsg("hello");
        }

        #endregion

    }
}

