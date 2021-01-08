//-----------------------------------------------------------------------
// <copyright file="TestUtils.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using static Akka.IO.IpExtensions;

namespace Akka.Tests.IO
{
    public static class TestUtils
    {
        public static IPEndPoint TemporaryServerAddress(string hostName = "127.0.0.1", bool udp = false)
        {
            var host = new IPEndPoint(IPAddress.Parse(hostName), 0);
            using (var socket = new Socket(
                udp ? SocketType.Dgram : SocketType.Stream,
                udp ? ProtocolType.Udp : ProtocolType.Tcp))
            {
                socket.Bind(host);
                return new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)socket.LocalEndPoint).Port);
            }
        }
        public static IEnumerable<IPEndPoint> TemporaryServerAddresses(int numberOfAddresses, string hostName = "127.0.0.1", bool udp = false)
        {
            return Enumerable.Range(0, numberOfAddresses).Select(i => TemporaryServerAddress(hostName, udp));
        }

        public static bool Is(this EndPoint ep1, EndPoint ep2)
        {
            var ip1 = ep1 as IPEndPoint;
            var ip2 = ep2 as IPEndPoint;
            return ip1 != null && ip2 != null && ip1.Port == ip2.Port && ip1.Address.MapToIPv4().Equals(ip2.Address.MapToIPv4());
        }
    }
}
