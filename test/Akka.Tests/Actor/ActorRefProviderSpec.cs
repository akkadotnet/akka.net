using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Tests.Actor
{
    [TestClass]
    public class ActorRefProviderSpec : AkkaSpec
    {
        [TestMethod]
        public void CanResolveActorRef()
        {
            var path = testActor.Path.ToString();
            var resolved = sys.Provider.ResolveActorRef(path);
            Assert.AreSame(testActor, resolved);
        }
    }
}
