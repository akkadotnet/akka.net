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
            ActorPath.Parse(path.ToString()).ShouldBe(path);
        }

        [TestMethod]
        public void SupportsParsingRemotePaths()
        {
            var remote = "akka://sys@host:1234/some/ref";
            var parsed = ActorPath.Parse(remote);
            parsed.ToString().ShouldBe(remote);
        }

        [TestMethod]
        public void ThrowExceptionUponMalformedPath()
        {
            intercept<UriFormatException>(() => ActorPath.Parse(""));
            intercept<UriFormatException>(() => ActorPath.Parse("://hallo"));
            intercept<UriFormatException>(() => ActorPath.Parse("s://dd@:12"));
            intercept<UriFormatException>(() => ActorPath.Parse("s://dd@h:hd"));
            intercept<UriFormatException>(() => ActorPath.Parse("a://l:1/b"));
        }

        [TestMethod]
        public void CreateCorrectToString()
        {
            var a = new Address("akka.tcp", "mysys");
            //TODO: there must be a / after system name
           // new RootActorPath(a).ToString().ShouldBe("akka.tcp://mysys/");
            (new RootActorPath(a) / "user").ToString().ShouldBe("akka.tcp://mysys/user");
            (new RootActorPath(a) / "user" / "foo").ToString().ShouldBe("akka.tcp://mysys/user/foo");
            (new RootActorPath(a) / "user" / "foo" / "bar").ToString().ShouldBe("akka.tcp://mysys/user/foo/bar");
        }

        [TestMethod]
        public void CreateCorrectToStringWithoutAddress()
        {
            var a = new Address("akka.tcp", "mysys");
            //TODO: there must be a / after system name
            // new RootActorPath(a).ToStringWithoutAddress().ShouldBe("/");
            (new RootActorPath(a) / "user").ToStringWithoutAddress().ShouldBe("/user");
            (new RootActorPath(a) / "user" / "foo").ToStringWithoutAddress().ShouldBe("/user/foo");
            (new RootActorPath(a) / "user" / "foo" / "bar").ToStringWithoutAddress().ShouldBe("/user/foo/bar");
        }
    }
}
