using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Akka.Actor;
using Akka.Dispatch;
using Akka.TestKit.TestActors;
using Akka.TestKit.VsTest;
using Autofac;

namespace Akka.DI.AutoFac.Tests
{
    [TestClass]
    public class Autofac_StashMailboxSpec : Akka.TestKit.VsTest.TestKit
    {

        [TestMethod]
        public void When_creating_actor_marked_with_WithUnboundedStash_a_mailbox_which_supports_unbounded_stash_is_created()
        {
            var builder = new ContainerBuilder();
            builder.RegisterType<UnboundedStashActor>();

            var container = builder.Build();
            var propsResolver = new AutoFacDependencyResolver(container, this.Sys);

            var actorRef = ActorOf(propsResolver.Create<UnboundedStashActor>());
            var intRef = (LocalActorRef)actorRef;

            Assert.IsInstanceOfType(intRef.Cell.Mailbox, typeof(UnboundedDequeBasedMailbox));
        }

        [TestMethod]
        public void When_creating_normal_actor_Then_a_normal_mailbox_is_created()
        {
            var builder = new ContainerBuilder();
            builder.RegisterType<UnboundedStashActor>();

            var container = builder.Build();
            var propsResolver = new AutoFacDependencyResolver(container, this.Sys);

            var actorRef = ActorOf(propsResolver.Create<UnboundedStashActor>());
            var intRef = (LocalActorRef)actorRef;

            Assert.IsInstanceOfType(intRef.Cell.Mailbox, typeof(UnboundedMailbox));
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
