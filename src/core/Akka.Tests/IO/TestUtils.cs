//-----------------------------------------------------------------------
// <copyright file="TestUtils.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Net;

namespace Akka.Tests.IO
{
    public static class TestUtils
    {
        public static bool Is(this EndPoint ep1, EndPoint ep2)
        {
            return ep1 is IPEndPoint ip1 && ep2 is IPEndPoint ip2 && ip1.Port == ip2.Port && ip1.Address.MapToIPv4().Equals(ip2.Address.MapToIPv4());
        }
    }
}
