using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Tests.Actor
{
    
    public class ActorSelectionSpec : AkkaSpec
    {
        [Fact]
        public void CanResolveChildPath()
        {
            var selection = sys.ActorSelection("user/test");
            selection.Tell("hello");
            expectMsg("hello");
        }

        [Fact]
        public void CanResolveUpAndDownPath()
        {
            var selection = sys.ActorSelection("user/test/../../user/test");
            selection.Tell("hello");
            expectMsg("hello");
        }

        [Fact]
        public void CanAskActorSelection()
        {
            var selection = sys.ActorSelection("user/echo");
            var task = selection.Ask("hello");
            expectMsg("hello");
            task.Wait();
            Assert.Equal("hello", task.Result);
        }

        [Fact]
        public async Task CanResolveOne()
        {
            var selection = sys.ActorSelection("user/test");
            var one = await selection.ResolveOne(TimeSpan.FromSeconds(1));            
            Assert.NotNull(one);
        }

        [Fact]
        public async Task CanNotResolveOneWhenNoMatch()
        {
            var selection = sys.ActorSelection("user/nonexisting");

            //xUnit 2 will have Assert.ThrowsAsync<TException>();
            AkkaSpecExtensions.ThrowsAsync<ActorNotFoundException>(async () => await selection.ResolveOne(TimeSpan.FromSeconds(1)));  
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
            var contextActor = sys.ActorOf<ActorContextSelectionActor>();
            contextActor.Tell(new Tuple<string,string>("/user/test", "hello"));
            expectMsg("hello");
        }

        [Fact]
        public void CanResolveRelativeActorPathInActorContext()
        {
            var contextActor = sys.ActorOf<ActorContextSelectionActor>();
            contextActor.Tell(new Tuple<string, string>("../test/../../user/test", "hello"));
            expectMsg("hello");
        }

        #endregion
    }
}
