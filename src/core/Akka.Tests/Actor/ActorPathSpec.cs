using Akka.TestKit;
using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Tests.Actor
{
    
    public class ActorPathSpec : AkkaSpec 
    {
        [Fact]
        public void SupportsParsingItsStringRep()
        {
            var path = new RootActorPath(new Address("akka.tcp", "mysys")) / "user";
            ActorPathParse(path.ToString()).ShouldBe(path);
        }

        private ActorPath ActorPathParse(string path)
        {
            ActorPath actorPath;
            if(ActorPath.TryParse(path, out actorPath))
                return actorPath;
            throw new UriFormatException();
        }

        [Fact]
        public void ActorPath_Parse_HandlesCasing_ForLocal()
        {
            const string uriString = "aKKa://sYstEm/pAth1/pAth2";
            var actorPath = ActorPathParse(uriString);

            // "Although schemes are case insensitive, the canonical form is lowercase and documents that
            //  specify schemes must do so with lowercase letters.  An implementation should accept 
            //  uppercase letters as equivalent to lowercase in scheme names (e.g., allow "HTTP"
            //  as well as "http") for the sake of robustness but should only produce lowercase scheme names 
            //  for consistency."   rfc3986 
            Assert.True(actorPath.Address.Protocol.Equals("akka", StringComparison.Ordinal), "protocol should be lowercase");
            
            //In Akka, at least the system name is case-sensitive, see http://doc.akka.io/docs/akka/current/additional/faq.html#what-is-the-name-of-a-remote-actor            
            Assert.True(actorPath.Address.System.Equals("sYstEm", StringComparison.Ordinal),  "system");

            var elements = actorPath.Elements.ToList();
            elements.Count.ShouldBe(2,"number of elements in path");
            Assert.True("pAth1".Equals(elements[0], StringComparison.Ordinal), "first path element");
            Assert.True("pAth2".Equals(elements[1], StringComparison.Ordinal), "second path element");
            Assert.Equal(actorPath.ToString(),"akka://sYstEm/pAth1/pAth2");
        }
        [Fact]
        public void ActorPath_Parse_HandlesCasing_ForRemote()
        {
            const string uriString = "aKKa://sYstEm@hOst:4711/pAth1/pAth2";
            var actorPath = ActorPathParse(uriString);

            // "Although schemes are case insensitive, the canonical form is lowercase and documents that
            //  specify schemes must do so with lowercase letters.  An implementation should accept 
            //  uppercase letters as equivalent to lowercase in scheme names (e.g., allow "HTTP"
            //  as well as "http") for the sake of robustness but should only produce lowercase scheme names 
            //  for consistency."   rfc3986 
            Assert.True(actorPath.Address.Protocol.Equals("akka", StringComparison.Ordinal), "protocol should be lowercase");

            //In Akka, at least the system name is case-sensitive, see http://doc.akka.io/docs/akka/current/additional/faq.html#what-is-the-name-of-a-remote-actor            
            Assert.True(actorPath.Address.System.Equals("sYstEm", StringComparison.Ordinal), "system");

            //According to rfc3986 host is case insensitive, but should be produced as lowercase
            Assert.True(actorPath.Address.Host.Equals("host", StringComparison.Ordinal), "host");
            actorPath.Address.Port.ShouldBe(4711, "port");
            var elements = actorPath.Elements.ToList();
            elements.Count.ShouldBe(2, "number of elements in path");
            Assert.True("pAth1".Equals(elements[0], StringComparison.Ordinal), "first path element");
            Assert.True("pAth2".Equals(elements[1], StringComparison.Ordinal), "second path element");
            Assert.Equal(actorPath.ToString(), "akka://sYstEm@host:4711/pAth1/pAth2");
        }

        [Fact]
        public void SupportsParsingRemotePaths()
        {
            var remote = "akka://sys@host:1234/some/ref";
            var parsed = ActorPathParse(remote);
            parsed.ToString().ShouldBe(remote);
        }

        [Fact]
        public void ReturnFalsUponMalformedPath()
        {
            ActorPath ignored;
            ActorPath.TryParse("", out ignored).ShouldBe(false);
            ActorPath.TryParse("://hallo", out ignored).ShouldBe(false);
            ActorPath.TryParse("s://dd@:12", out ignored).ShouldBe(false);
            ActorPath.TryParse("s://dd@h:hd", out ignored).ShouldBe(false);
            ActorPath.TryParse("a://l:1/b", out ignored).ShouldBe(false);
        }

        [Fact]
        public void CreateCorrectToString()
        {
            var a = new Address("akka.tcp", "mysys");
            //TODO: there must be a / after system name
            new RootActorPath(a).ToString().ShouldBe("akka.tcp://mysys/");
            (new RootActorPath(a) / "user").ToString().ShouldBe("akka.tcp://mysys/user");
            (new RootActorPath(a) / "user" / "foo").ToString().ShouldBe("akka.tcp://mysys/user/foo");
            (new RootActorPath(a) / "user" / "foo" / "bar").ToString().ShouldBe("akka.tcp://mysys/user/foo/bar");
        }

        [Fact]
        public void CreateCorrectToStringWithoutAddress()
        {
            var a = new Address("akka.tcp", "mysys");
            //TODO: there must be a / after system name
            new RootActorPath(a).ToStringWithoutAddress().ShouldBe("/");
            (new RootActorPath(a) / "user").ToStringWithoutAddress().ShouldBe("/user");
            (new RootActorPath(a) / "user" / "foo").ToStringWithoutAddress().ShouldBe("/user/foo");
            (new RootActorPath(a) / "user" / "foo" / "bar").ToStringWithoutAddress().ShouldBe("/user/foo/bar");
        }

        [Fact]
        public void CreateCorrectToStringWithAddress()
        {
            var local = new Address("akka.tcp", "mysys");
            var a = new Address("akka.tcp", "mysys", "aaa", 2552);
            var b = new Address("akka.tcp", "mysys", "bb", 2552);
            var c = new Address("akka.tcp", "mysys", "cccc", 2552);
            var d = new Address("akka.tcp", "mysys", "192.168.107.1", 2552);
            var root = new RootActorPath(local);
            root.ToStringWithAddress(a).ShouldBe("akka.tcp://mysys@aaa:2552/");
            (root / "user").ToStringWithAddress(a).ShouldBe("akka.tcp://mysys@aaa:2552/user");
            (root / "user" / "foo").ToStringWithAddress(a).ShouldBe("akka.tcp://mysys@aaa:2552/user/foo");

            root.ToStringWithAddress(b).ShouldBe("akka.tcp://mysys@bb:2552/");
            (root / "user").ToStringWithAddress(b).ShouldBe("akka.tcp://mysys@bb:2552/user");
            (root / "user" / "foo").ToStringWithAddress(b).ShouldBe("akka.tcp://mysys@bb:2552/user/foo");

            root.ToStringWithAddress(c).ShouldBe("akka.tcp://mysys@cccc:2552/");
            (root / "user").ToStringWithAddress(c).ShouldBe("akka.tcp://mysys@cccc:2552/user");
            (root / "user" / "foo").ToStringWithAddress(c).ShouldBe("akka.tcp://mysys@cccc:2552/user/foo");


            root.ToStringWithAddress(d).ShouldBe("akka.tcp://mysys@192.168.107.1:2552/");
            (root / "user").ToStringWithAddress(d).ShouldBe("akka.tcp://mysys@192.168.107.1:2552/user");
            (root / "user" / "foo").ToStringWithAddress(d).ShouldBe("akka.tcp://mysys@192.168.107.1:2552/user/foo");

            var rootA = new RootActorPath(a);
            rootA.ToStringWithAddress(b).ShouldBe("akka.tcp://mysys@aaa:2552/");
            (rootA / "user").ToStringWithAddress(b).ShouldBe("akka.tcp://mysys@aaa:2552/user");
            (rootA / "user" / "foo").ToStringWithAddress(b).ShouldBe("akka.tcp://mysys@aaa:2552/user/foo");

        }


        /*
 "have correct path elements" in {
      (RootActorPath(Address("akka.tcp", "mysys")) / "user" / "foo" / "bar").elements.toSeq should be(Seq("user", "foo", "bar"))
    }
         */

        [Fact]
        public void HaveCorrectPathElements()
        {
            (new RootActorPath(new Address("akka.tcp", "mysys")) / "user" / "foo" / "bar").Elements.ShouldOnlyContainInOrder(new[] { "user", "foo", "bar" });
        }

        [Fact]
        public void PathsWithDifferentAddressesAndSameElementsShouldNotBeEqual()
        {
            ActorPath path1 = null;
            ActorPath path2 = null;
            ActorPath.TryParse("akka.tcp://remotesystem@localhost:8080/user",out path1);
            ActorPath.TryParse("akka://remotesystem/user", out path2);

            Assert.NotEqual(path2, path1);
        }

        [Fact]
        public void PathsWithSameAddressesAndSameElementsShouldNotBeEqual()
        {
            ActorPath path1 = null;
            ActorPath path2 = null;
            ActorPath.TryParse("akka.tcp://remotesystem@localhost:8080/user", out path1);
            ActorPath.TryParse("akka.tcp://remotesystem@localhost:8080/user", out path2);

            Assert.Equal(path2, path1);
        }
    }
}
