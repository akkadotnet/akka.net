//-----------------------------------------------------------------------
// <copyright file="StashMailboxSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Dispatch.MessageQueues;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Tests.Actor
{
    public class StashMailboxSpec : AkkaSpec
    {
        [Fact]
        public void When_creating_normal_actor_Then_a_normal_mailbox_is_created()
        {
            var actorRef = ActorOf<BlackHoleActor>();
            var intRef = (RepointableActorRef)actorRef;
            intRef.MailboxType.GetType().ShouldBe(typeof(UnboundedMailbox));
        }

        [Fact]
        public void When_creating_actor_marked_with_WithUnboundedStash_a_mailbox_which_supports_unbounded_stash_is_created()
        {
            var actorRef = ActorOf<UnboundedStashActor>();
            var intRef = (RepointableActorRef)actorRef;
            intRef.MailboxType.GetType().ShouldBe(typeof(UnboundedDequeBasedMailbox));
        }

        [Fact]
        public void When_creating_actor_marked_with_WithBoundedStash_a_mailbox_which_supports_unbounded_stash_is_created()
        {
            var actorRef = ActorOf<BoundedStashActor>();
            var intRef = (RepointableActorRef)actorRef;
            intRef.MailboxType.GetType().ShouldBe(typeof(BoundedDequeBasedMailbox));
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

