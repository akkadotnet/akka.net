//-----------------------------------------------------------------------
// <copyright file="SocketUtil.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using System.Net;
using System.Net.Sockets;
using Akka.Configuration;

namespace Akka.MultiNode.TestAdapter.Helpers
{
    internal static class SocketUtil
    {
        public static IPEndPoint TemporaryTcpAddress(string hostName)
        {
            if (!IPAddress.TryParse(hostName, out var address))
            {
                // If hostName isn't an IP, its probably a dns address, try to resolve it.
                var addresses = Dns.GetHostAddresses(hostName);
                address =
                    addresses.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork) ??
                    addresses.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetworkV6) ??
                    throw new ConfigurationException(
                        $"Failed to look up IPv4 or IPv6 address for host name [{hostName}]");
            }

            return TemporaryTcpAddress(address);
        }
        
        public static IPEndPoint TemporaryTcpAddress(IPAddress address)
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                var endpoint = new IPEndPoint(address, 0);
                socket.Bind(endpoint);
                return (IPEndPoint) socket.LocalEndPoint;
            }
        }
    }
}