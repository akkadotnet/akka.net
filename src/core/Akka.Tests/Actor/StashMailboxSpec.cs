using System;
using Akka.Actor;
using Akka.Dispatch;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Xunit;

namespace Akka.Tests.Actor
{
    public class StashMailboxSpec : AkkaSpec
    {
        [Fact]
        public void When_creating_normal_actor_Then_a_normal_mailbox_is_created()
        {
            var actorRef = ActorOf<BlackHoleActor>();
            var intRef = (LocalActorRef)actorRef;
            intRef.Cell.Mailbox.GetType().ShouldBe(typeof(UnboundedMailbox));
        }

        [Fact]
        public void When_creating_actor_marked_with_WithUnboundedStash_a_mailbox_which_supports_unbounded_stash_is_created()
        {
            var actorRef = ActorOf<UnboundedStashActor>();
            var intRef = (LocalActorRef)actorRef;
            intRef.Cell.Mailbox.GetType().ShouldBe(typeof(UnboundedDequeBasedMailbox));
        }

        [Fact(Skip = "We do not have a BoundedDequeBasedMailbox yet")] //TODO: Remove Skip when we have a BoundedDequeBasedMailbox
        public void When_creating_actor_marked_with_WithBoundedStash_a_mailbox_which_supports_unbounded_stash_is_created()
        {
            var actorRef = ActorOf<BoundedStashActor>();
            var intRef = (LocalActorRef)actorRef;
            //intRef.Cell.Mailbox.GetType().ShouldBe(typeof(BoundedDequeBasedMailbox));
            throw new Exception("Incomplete. Remove the comment on the line above this, and remove this line, when we have BoundedDequeBasedMailbox");
        }

        private class UnboundedStashActor : BlackHoleActor, IWithUnboundedStash
        {
            public IStash Stash { get; set; }
        }
        private class BoundedStashActor : BlackHoleActor, IWithBoundedStash
        {
            public IStash Stash { get; set; }
        }
    }
}