//-----------------------------------------------------------------------
// <copyright file="ActorPathSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Akka.Actor;
using Akka.TestKit;
using Xunit;
using FluentAssertions;

namespace Akka.Tests.Actor
{
    public class ActorPathSpec
    {
        [Fact]
        public void SupportsParsingItsStringRep()
        {
            var path = new RootActorPath(new Address("akka.tcp", "mysys")) / "user";
            ActorPathParse(path.ToString()).ShouldBe(path);
        }
        
        [Theory]
        [InlineData(1)]
        [InlineData(100)]
        [InlineData(int.MaxValue)]
        public void SupportsParsingItsStringRepWithUid(int uid)
        {
            var path = new RootActorPath(new Address("akka.tcp", "mysys", "localhost", 9110)) / "user";
            var pathWithUid = path.WithUid(uid);
            ActorPathParse(pathWithUid.ToSerializationFormat()).ShouldBe(pathWithUid);
        }

        public static IEnumerable<object[]> WideUids()
        {
            foreach (var uid in new[] { int.MaxValue + 1L, 4_294_967_296L, 1_000_000_000_000_000L, long.MaxValue })
                yield return new object[] { uid };
        }

        [Theory(DisplayName = "Should_RoundTripUid_When_UidDoesNotFitInInt")]
        [MemberData(nameof(WideUids))]
        public void Should_RoundTripUid_When_UidDoesNotFitInInt(long uid)
        {
            var path = new RootActorPath(new Address("akka.tcp", "mysys", "localhost", 9110)) / "user" / "child";
            var pathWithUid = path.WithUid(uid);
            var serialized = pathWithUid.ToSerializationFormat();
            serialized.Should().EndWith("#" + uid);

            ActorPath.Parse(serialized).Uid.Should().Be(uid);
            ActorPath.Parse(serialized).Should().Be(pathWithUid);

            ActorPath.TryParse(serialized, out var tryParsed).Should().BeTrue();
            tryParsed.Uid.Should().Be(uid);

            ActorPath.TryParse(new RootActorPath(pathWithUid.Address), pathWithUid.ToStringWithoutAddress() + "#" + uid, out var relative)
                .Should().BeTrue();
            relative.Uid.Should().Be(uid);
        }

        [Theory(DisplayName = "Should_ParseUid_When_ChildElementWithUidDoesNotFitInInt")]
        [MemberData(nameof(WideUids))]
        public void Should_ParseUid_When_ChildElementWithUidDoesNotFitInInt(long uid)
        {
            var child = new RootActorPath(new Address("akka", "mysys")) / $"child#{uid}";
            child.Name.Should().Be("child");
            child.Uid.Should().Be(uid);
        }

        [Theory(DisplayName = "Should_RejectUid_When_UidFragmentIsNegative")]
        [InlineData("-1")]
        [InlineData("-5")]
        [InlineData("-0")]
        [InlineData("-2147483648")]
        [InlineData("-9223372036854775808")]
        public void Should_RejectUid_When_UidFragmentIsNegative(string uid)
        {
            // a non-numeric fragment such as "#abc" throws on these code paths, so a negative one must too
            var root = new RootActorPath(new Address("akka", "mysys"));
            ((Action)(() => ActorPath.TryParse($"akka://mysys/user/child#{uid}", out _))).Should().Throw<Exception>();
            ((Action)(() => ActorPath.Parse($"akka://mysys/user/child#{uid}"))).Should().Throw<Exception>();
            ((Action)(() => ActorPath.TryParse(root, $"/user/child#{uid}", out _))).Should().Throw<Exception>();
            ((Action)(() => { var _ = root / $"child#{uid}"; })).Should().Throw<Exception>();
        }

        [Theory(DisplayName = "Should_RejectUid_When_UidIsNegative")]
        [InlineData(-1L)]
        [InlineData(-5L)]
        [InlineData(long.MinValue)]
        public void Should_RejectUid_When_UidIsNegative(long uid)
        {
            var parent = new RootActorPath(new Address("akka", "mysys")) / "user";
            foreach (var create in new Action[] { () => parent.WithUid(uid), () => parent.Root.WithUid(uid), () => _ = new ChildActorPath(parent, "child", uid) })
                create.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("uid");
        }

        [Fact(DisplayName = "Should_AcceptUid_When_UidIsZeroOrMaxValue")]
        public void Should_AcceptUid_When_UidIsZeroOrMaxValue()
        {
            var path = new RootActorPath(new Address("akka", "mysys")) / "user";
            path.WithUid(0).Uid.Should().Be(0);
            var wide = path.WithUid(long.MaxValue);
            ActorPath.Parse(wide.ToSerializationFormat()).Should().Be(wide);
        }

        private ActorPath ActorPathParse(string path)
        {
            if (ActorPath.TryParse(path, out var actorPath))
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
            Assert.True(actorPath.Address.System.Equals("sYstEm", StringComparison.Ordinal), "system");

            var elements = actorPath.Elements.ToList();
            elements.Count.ShouldBe(2, "number of elements in path");
            Assert.True("pAth1".Equals(elements[0], StringComparison.Ordinal), "first path element");
            Assert.True("pAth2".Equals(elements[1], StringComparison.Ordinal), "second path element");
            Assert.Equal("akka://sYstEm/pAth1/pAth2", actorPath.ToString());
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
            Assert.Equal("akka://sYstEm@host:4711/pAth1/pAth2", actorPath.ToString());
        }

        [Fact]
        public void Supports_parsing_remote_paths()
        {
            var remote = "akka://sys@host:1234/some/ref";
            var parsed = ActorPathParse(remote);
            parsed.ToString().ShouldBe(remote);
        }

        /// <summary>
        /// Reproduction for https://github.com/akkadotnet/akka.net/issues/5083
        /// </summary>
        [Fact]
        public void Supports_parsing_remote_FQDN_paths()
        {
            var remote = "akka://sys@host.domain.com:1234/some/ref";
            var parsed = ActorPathParse(remote);
            parsed.ToString().ShouldBe(remote);
        }

        [Fact]
        public void Supports_rebase_a_path()
        {
            var path = "akka://sys@host:1234/";
            ActorPath.TryParse(path, out var root).ShouldBe(true);
            root.ToString().ShouldBe(path);

            ActorPath.TryParse(root, "/", out var newPath).ShouldBe(true);
            newPath.ShouldBe(root);

            var uri1 = "/abc/def";
            ActorPath.TryParse(root, uri1, out newPath).ShouldBe(true);
            newPath.ToStringWithAddress().ShouldBe($"{path}{uri1.Substring(1)}");
            newPath.ParentOf(-2).ShouldBe(root);

            var uri2 = "/def";
            ActorPath.TryParse(newPath, uri2, out newPath).ShouldBe(true);
            newPath.ToStringWithAddress().ShouldBe($"{path}{uri1.Substring(1)}{uri2}");
            newPath.ParentOf(-3).ShouldBe(root);
        }

        [Fact]
        public void Return_false_upon_malformed_path()
        {
            ActorPath.TryParse("", out _).ShouldBe(false);
            ActorPath.TryParse("://hallo", out _).ShouldBe(false);
            ActorPath.TryParse("s://dd@:12", out _).ShouldBe(false);
            ActorPath.TryParse("s://dd@h:hd", out _).ShouldBe(false);
            ActorPath.TryParse("a://l:1/b", out _).ShouldBe(false);
            ActorPath.TryParse("akka:/", out _).ShouldBe(false);
        }

        [Fact]
        public void Supports_jumbo_actor_name_length()
        {
            var prefix = "akka://sys@host.domain.com:1234/some/ref/";
            var nameSize = 10 * 1024 * 1024; //10MB

            var sb = new StringBuilder(nameSize + prefix.Length);
            sb.Append(prefix);
            sb.Append('a', nameSize); //10MB
            var path = sb.ToString();

            ActorPath.TryParse(path, out var actorPath).ShouldBe(true);
            actorPath.Name.Length.ShouldBe(nameSize);
            actorPath.Name.All(n => n == 'a').ShouldBe(true);

            var result = actorPath.ToStringWithAddress();
            result.ShouldBe(path);
        }

        [Fact]
        public void Create_correct_ToString()
        {
            var a = new Address("akka.tcp", "mysys");
            //TODO: there must be a / after system name
            new RootActorPath(a).ToString().ShouldBe("akka.tcp://mysys/");
            (new RootActorPath(a) / "user").ToString().ShouldBe("akka.tcp://mysys/user");
            (new RootActorPath(a) / "user" / "foo").ToString().ShouldBe("akka.tcp://mysys/user/foo");
            (new RootActorPath(a) / "user" / "foo" / "bar").ToString().ShouldBe("akka.tcp://mysys/user/foo/bar");
        }

        [Fact]
        public void Create_correct_ToString_without_address()
        {
            var a = new Address("akka.tcp", "mysys");
            //TODO: there must be a / after system name
            new RootActorPath(a).ToStringWithoutAddress().ShouldBe("/");
            (new RootActorPath(a) / "user").ToStringWithoutAddress().ShouldBe("/user");
            (new RootActorPath(a) / "user" / "foo").ToStringWithoutAddress().ShouldBe("/user/foo");
            (new RootActorPath(a) / "user" / "foo" / "bar").ToStringWithoutAddress().ShouldBe("/user/foo/bar");
        }

        [Fact]
        public void Create_correct_ToString_with_address()
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

        /// <summary>
        /// Reproduces https://github.com/akkadotnet/akka.net/issues/2151
        /// </summary>
        [Fact]
        public void Fix2151_not_throw_NRE_on_RootActorPath_ElementsWithUid()
        {
            var a = new Address("akka.tcp", "mysys");
            var rootA = new RootActorPath(a);
            var uid = rootA.ElementsWithUid;
            Assert.True(uid.Count == 0); // RootActorPaths return no elements
        }


        /*
 "have correct path elements" in {
      (RootActorPath(Address("akka.tcp", "mysys")) / "user" / "foo" / "bar").elements.toSeq should be(Seq("user", "foo", "bar"))
    }
         */

        [Fact]
        public void Have_correct_path_elements()
        {
            (new RootActorPath(new Address("akka.tcp", "mysys")) / "user" / "foo" / "bar").Elements.ShouldOnlyContainInOrder(new[] { "user", "foo", "bar" });
        }

        [Fact]
        public void ToSerializationFormatWithAddress_should_use_each_address_given_in_turn()
        {
            var path = (new RootActorPath(new Address("akka", "mysys")) / "user" / "foo").WithUid(42);
            var a = new Address("akka", "mysys", "host-a", 1000);
            var b = new Address("akka", "mysys", "host-b", 2000);

            path.ToSerializationFormatWithAddress(a).ShouldBe("akka://mysys@host-a:1000/user/foo#42");
            path.ToSerializationFormatWithAddress(a).ShouldBe("akka://mysys@host-a:1000/user/foo#42");
            path.ToSerializationFormatWithAddress(b).ShouldBe("akka://mysys@host-b:2000/user/foo#42");
            path.ToSerializationFormatWithAddress(a).ShouldBe("akka://mysys@host-a:1000/user/foo#42");
        }

        [Fact]
        public void Paths_with_different_addresses_and_same_elements_should_not_be_equal()
        {
            ActorPath.TryParse("akka.tcp://remotesystem@localhost:8080/user", out var path1);
            ActorPath.TryParse("akka://remotesystem/user", out var path2);

            Assert.NotEqual(path2, path1);
        }

        [Fact]
        public void Paths_with_same_addresses_and_same_elements_should_not_be_equal()
        {
            ActorPath.TryParse("akka.tcp://remotesystem@localhost:8080/user", out var path1);
            ActorPath.TryParse("akka.tcp://remotesystem@localhost:8080/user", out var path2);

            Assert.Equal(path2, path1);
        }

        [Theory]
        [InlineData("$NamesMayNotStartWithDollar", false)]
        [InlineData("NamesMayContain$Dollar", true)]
        [InlineData("-$NamesMayContain", true)]
        [InlineData("(42)", true)]
        [InlineData("%32", true)]
        [InlineData("reliableEndpointWriter-akka.tcp%3a%2f%2fRemoteSystem%40localhost%3a22760-1", true)]
        [InlineData(@"SomeCharacters;::@&=+,.!~*'_", true)]
        [InlineData("/NamesMayNotStartWithSlash", false)]
        [InlineData("NamesMayNotContain/Slash", false)]
        [InlineData("EscapingWithOtherNumberCharacters-%Ⅷ⅓", false)]
        public void Validate_element_parts(string element, bool matches)
        {
            ActorPath.IsValidPathElement(element).ShouldBe(matches);
        }

        [Fact]
        public void ValidateElementPartsComprehensive()
        {
            // NOTE: $ is not a valid starting character
            var valid = Enumerable.Range(0, 128).Select(i => (char)i).Where(c =>
                c is >= 'a' and <= 'z' || 
                c is >= 'A' and <= 'Z' || 
                c is >= '0' and <= '9' ||
                ActorPath.ValidSymbols.Contains(c)
                && c is not '$'
            ).ToArray();

            foreach (var c in Enumerable.Range(0, 2048).Select(i => (char)i))
            {
                if(valid.Contains(c))
                    ActorPath.IsValidPathElement(new string(new[] { c })).Should().BeTrue();
                else
                    ActorPath.IsValidPathElement(new string(new[] { c })).Should().BeFalse();
            }

            // $ after a valid character should be valid
            foreach (var c in valid)
            {
                ActorPath.IsValidPathElement(new string(new[] { c, '$' })).Should().BeTrue();
            }
        }

        [Theory]
        [InlineData("$NamesMayNotNormallyStartWithDollarButShouldBeOkWHenEncoded")]
        [InlineData("NamesMayContain$Dollar")]
        [InlineData("ÎnternationalCharactersÅÄÖØê")]
        [InlineData("ReservedCharacters:;/?:@&=+$,")]
        [InlineData("Using parenthesis(4711)")]
        public void Validate_that_url_encoded_values_are_valid_element_parts(string element)
        {
            var urlEncode = System.Net.WebUtility.UrlEncode(element);
            Debug.WriteLine("Encoded \"{0}\" to \"{1}\"", element, urlEncode);
            ActorPath.IsValidPathElement(urlEncode).ShouldBeTrue();
        }
    }
}

