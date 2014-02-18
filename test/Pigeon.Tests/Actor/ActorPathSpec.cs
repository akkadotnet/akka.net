using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pigeon.Actor;

namespace Pigeon.Tests.Actor
{
    [TestClass]
    public class ActorPathSpec : AkkaSpec 
    {
        [TestMethod]
        public void SupportsParsingItsStringRep()
        {
            var path = new RootActorPath(new Address("akka.tcp", "mysys")) / "user";
            ActorPath.Parse(path.ToString(), sys).Then(Assert.AreEqual, path);
        }

        [TestMethod]
        public void SupportsParsingRemotePaths()
        {
            var remote = "akka://sys@host:1234/some/ref";
            var parsed = ActorPath.Parse(remote, sys);
            parsed.ToString().Then(Assert.AreEqual, remote);
        }

        //[TestMethod]
        //public void ThrowExceptionUponMalformedPath()
        //{
        //    intercept<UriFormatException>(() => ActorPath.Parse("", sys));
        //    intercept<UriFormatException>(() => ActorPath.Parse("://hallo", sys));
        //    intercept<UriFormatException>(() => ActorPath.Parse("s://dd@:12", sys));
        //    intercept<UriFormatException>(() => ActorPath.Parse("s://dd@h:hd", sys));
        //    intercept<UriFormatException>(() => ActorPath.Parse("a://l:1/b", sys));
        //}
    }
}
