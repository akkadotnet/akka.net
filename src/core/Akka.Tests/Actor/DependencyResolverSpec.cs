using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor
{
    public class DependencyResolverSpec : AkkaSpec
    {
        [Fact]
        public void DependencyResolver_must_resolve_ActorSystem()
        {
            var system = Sys.DependencyResolver.Resolve(typeof(ActorSystem));
            system.ShouldNotBe(null);
            system.ShouldBe(Sys);
        }

        [Fact]
        public void DependencyResolver_must_resolve_ExtendedActorSystem()
        {
            var system = Sys.DependencyResolver.Resolve(typeof(ExtendedActorSystem));
            system.ShouldNotBe(null);
            system.ShouldBe(Sys);
        }
    }
}