using Akka.Actor.Internals;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor
{
    
    public class ActorRefProviderSpec : AkkaSpec
    {
        [Fact]
        public void CanResolveActorRef()
        {
            var path = TestActor.Path.ToString();
            var resolved = ((ActorSystemImpl)Sys).Provider.ResolveActorRef(path);
            Assert.Same(TestActor, resolved);
        }
    }
}
