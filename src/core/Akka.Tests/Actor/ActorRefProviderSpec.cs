using Akka.Actor;
using Akka.Actor.Internals;
using Akka.TestKit;
using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Tests.Actor
{
    
    public class ActorRefProviderSpec : AkkaSpec
    {
        [Fact]
        public void CanResolveActorRef()
        {
            var path = testActor.Path.ToString();
            var resolved = ((ActorSystemImpl)sys).Provider.ResolveActorRef(path);
            Assert.Same(testActor, resolved);
        }
    }
}
