//-----------------------------------------------------------------------
// <copyright file="AddressSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor
{
    public class AddressSpec
    {
        [Fact]
        public void Host_is_lowercased_when_created()
        {
            var address = new Address("akka", "test", "HOSTNAME");
            address.Host.ShouldBe("hostname");
        }

        [Theory]
        [InlineData("akka://sys@host:1234/abc/def/", true, "akka://sys@host:1234", "/abc/def/")]
        [InlineData("akka://sys/abc/def/", true, "akka://sys", "/abc/def/")]
        [InlineData("akka://host:1234/abc/def/", true, "akka://host:1234", "/abc/def/")]
        [InlineData("akka://sys@host:1234", true, "akka://sys@host:1234", "/")]
        [InlineData("akka://sys@host:1234/", true, "akka://sys@host:1234", "/")]
        [InlineData("akka://sys@host/abc/def/", false, "", "")]
        public void Supports_parse_full_actor_path(string path, bool valid, string expectedAddress, string expectedUri)
        {
            Address.TryParse(path, out var address, out var absolutUri).ShouldBe(valid);
            if(valid)
            {
                address.ToString().ShouldBe(expectedAddress);
                absolutUri.ToString().ShouldBe(expectedUri);
            }                        
        }
    }
}

