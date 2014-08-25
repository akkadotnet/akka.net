using System;
using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor
{
    public class ActorRefSpec
    {
        //TODO: A lot is missing in ActorRefSpec

        [Fact]
        public void An_ActorRef_should_equal_itself()
        {
            var equalTestActorRef = new EqualTestActorRef(new RootActorPath(new Address("akka", "test")));

            equalTestActorRef.Equals(equalTestActorRef).ShouldBeTrue();
            // ReSharper disable EqualExpressionComparison
            (equalTestActorRef == equalTestActorRef).ShouldBeTrue();
            (equalTestActorRef != equalTestActorRef).ShouldBeFalse();
            // ReSharper restore EqualExpressionComparison
        }

        [Fact]
        public void An_ActorRef_should_equal_another_ActorRef_instance_with_same_path()
        {
            var actorPath1 = (new RootActorPath(new Address("akka", "test")) / "name").WithUid(4711);
            var actorPath2 = (new RootActorPath(new Address("akka", "test")) / "name").WithUid(4711);
            var equalTestActorRef1 = new EqualTestActorRef(actorPath1);
            var equalTestActorRef2 = new EqualTestActorRef(actorPath2);

            equalTestActorRef1.Equals(equalTestActorRef2).ShouldBeTrue();
            // ReSharper disable EqualExpressionComparison
            (equalTestActorRef1 == equalTestActorRef2).ShouldBeTrue();
            (equalTestActorRef1 != equalTestActorRef2).ShouldBeFalse();
            // ReSharper restore EqualExpressionComparison
        }

        [Fact]
        public void An_ActorRef_should_not_equal_another_ActorRef_when_path_differs()
        {
            var referencePath = (new RootActorPath(new Address("akka", "test")) / "name").WithUid(4711);
            var path1 = (new RootActorPath(new Address("akka", "test")) / "name").WithUid(42);
            var path2 = (new RootActorPath(new Address("akka", "test")) / "name2").WithUid(4711);
            var path3 = (new RootActorPath(new Address("akka", "test2")) / "name").WithUid(4711);
            var refActorRef = new EqualTestActorRef(referencePath);
            var ref1 = new EqualTestActorRef(path1);
            var ref2 = new EqualTestActorRef(path2);
            var ref3 = new EqualTestActorRef(path3);

            refActorRef.Equals(ref1).ShouldBeFalse();
            refActorRef.Equals(ref2).ShouldBeFalse();
            refActorRef.Equals(ref3).ShouldBeFalse();
            // ReSharper disable EqualExpressionComparison
            (refActorRef == ref1).ShouldBeFalse();
            (refActorRef != ref1).ShouldBeTrue();
            (refActorRef == ref2).ShouldBeFalse();
            (refActorRef != ref2).ShouldBeTrue();
            (refActorRef == ref3).ShouldBeFalse();
            (refActorRef != ref3).ShouldBeTrue();
            // ReSharper restore EqualExpressionComparison
        }

        private class EqualTestActorRef : ActorRef
        {
            private ActorPath _path;

            public EqualTestActorRef(ActorPath path)
            {
                _path = path;
            }

            public override ActorPath Path { get { return _path; } }

            protected override void TellInternal(object message, ActorRef sender)
            {
                throw new NotImplementedException();
            }
        }
    }
}