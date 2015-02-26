using System;
using System.Collections.Generic;
using System.Net;
using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor
{
    public class RelativeActorPathSpec
    {
        protected IEnumerable<string> Elements(string path)
        {
            return RelativeActorPath.Unapply(path) ?? new List<string>();
        }

        [Fact]
        public void RelativeActorPath_must_match_single_name()
        {
            Elements("foo").ShouldBe(new List<string>(){"foo"});
        }

        [Fact]
        public void RelativeActorPath_must_match_path_separated_names()
        {
            Elements("foo/bar/baz").ShouldBe(new List<string>() { "foo", "bar", "baz" });
        }

        [Fact]
        public void RelativeActorPath_must_match_url_encoded_name()
        {
            var name = WebUtility.UrlEncode("akka://ClusterSystem@127.0.0.1:2552");
            Elements(name).ShouldBe(new List<string>() { name });
        }

        [Fact]
        public void RelativeActorPath_must_match_path_with_uid_fragment()
        {
            Elements("foo/bar/baz#1234").ShouldBe(new List<string>() { "foo", "bar", "baz#1234" });
        }
    }
}
