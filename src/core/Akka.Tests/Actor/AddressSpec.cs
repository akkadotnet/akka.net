//-----------------------------------------------------------------------
// <copyright file="AddressSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
    }
}
