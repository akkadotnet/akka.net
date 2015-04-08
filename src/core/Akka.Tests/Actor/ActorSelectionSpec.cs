using System;
using System.Threading.Tasks;
using Akka.Actor;
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
        public void CanResolveChildPath()
        {
            var selection = Sys.ActorSelection("user/test");
            selection.Tell("hello");
            ExpectMsg("hello");
        }

        [Fact]
        public void CanResolveUpAndDownPath()
        {
            var selection = Sys.ActorSelection("user/test/../../user/test");
            selection.Tell("hello");
            ExpectMsg("hello");
        }

        [Fact]
        public void CanResolveWildcardPartialAsterisk()
        {
            Sys.ActorSelection("user/te*st").Tell("hello1");
            ExpectMsg("hello1");
        }

        [Fact]
        public void CanResolveWildcardFullAsterisk()
        {
            Sys.ActorSelection("user/*").Tell("hello3");
            ExpectMsg("hello3");
        }

        [Fact]
        public void CanResolveWildcardQuestionMark()
        {
            Sys.ActorSelection("user/t?st").Tell("hello2");
            ExpectMsg("hello2");
        }

        [Fact]
        public void CanNotResolveWildcardWhenNoMatch()
        {
            Sys.ActorSelection("user/foo*").Tell("hello3");
            ExpectNoMsg(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void CanAskActorSelection()
        {
            var selection = Sys.ActorSelection("user/echo");
            var task = selection.Ask("hello");
            ExpectMsg("hello");
            task.Wait();
            Assert.Equal("hello", task.Result);
        }

        [Fact]
        public async Task CanResolveOne()
        {
            var selection = Sys.ActorSelection("user/test");
            var one = await selection.ResolveOne(TimeSpan.FromSeconds(1));            
            Assert.NotNull(one);
        }

        [Fact]
        public async Task CanNotResolveOneWhenNoMatch()
        {
            var selection = Sys.ActorSelection("user/nonexisting");

            //xUnit 2 will have Assert.ThrowsAsync<TException>();
            await AkkaSpecExtensions.ThrowsAsync<ActorNotFoundException>(async () => await selection.ResolveOne(TimeSpan.FromSeconds(1)));  
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
        public void CanResolveAbsoluteActorPathInActorContext()
        {
            var contextActor = Sys.ActorOf<ActorContextSelectionActor>();
            contextActor.Tell(new Tuple<string,string>("/user/test", "hello"));
            ExpectMsg("hello");
        }

        [Fact]
        public void CanResolveRelativeActorPathInActorContext()
        {
            var contextActor = Sys.ActorOf<ActorContextSelectionActor>();
            contextActor.Tell(new Tuple<string, string>("../test/../../user/test", "hello"));
            ExpectMsg("hello");
        }

        #endregion

    }
}
