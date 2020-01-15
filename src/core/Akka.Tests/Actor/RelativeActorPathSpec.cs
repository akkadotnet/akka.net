//-----------------------------------------------------------------------
// <copyright file="RelativeActorPathSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
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
        public void RelativeActorPath_starting_with_slash_must_match_single_name()
        {
            Elements("/foo").ShouldBe(new List<string>() { "foo" });
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
            var result = Elements("foo/bar/baz#1234").ToList();
            result.ShouldBe(new List<string>() { "foo", "bar", "baz#1234" });
        }

        [Fact]
        public void RelativeActorPath_must_not_match_absolute_path()
        {
            var name = "akka://ClusterSystem@127.0.0.1:2552";
            Elements(name).ShouldBe(new List<string>());
        }

        [Fact]
        public void RelativeActorPath_must_not_match_absolute_path_with_uid_fragment()
        {
            var name = "akka://ClusterSystem@127.0.0.1:2552/user/foo/bar/baz#1234";
            Elements(name).ShouldBe(new List<string>());
        }

    }
}

