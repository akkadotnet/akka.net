// //-----------------------------------------------------------------------
// // <copyright file="DnsHelpers.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Remote.Transport
{
    public static class DnsHelpers
    {
        public static async Task<IPEndPoint> ResolveNameAsync(
            DnsEndPoint address, AddressFamily addressFamily)
        {

            var resolved = await Dns.GetHostEntryAsync(address.Host)
                .ConfigureAwait(false);
            var found =
                resolved.AddressList.LastOrDefault(a =>
                    a.AddressFamily == addressFamily);
            if (found == null)
            {
                throw new KeyNotFoundException(
                    $"Couldn't resolve IP endpoint from provided DNS name '{address}' with address family of '{addressFamily}'");
            }

            return new IPEndPoint(found, address.Port);
        }

        public static Address MapSocketToAddress(IPEndPoint socketAddress,
            string schemeIdentifier, string systemName, string hostName = null,
            int? publicPort = null)
        {
            try
            {
                return socketAddress == null
                    ? null
                    : new Address(schemeIdentifier, systemName,
                        SafeMapHostName(hostName) ??
                        SafeMapIPv6(socketAddress.Address),
                        publicPort ?? socketAddress.Port);
            }
            catch (Exception e)
            {
                global::System.Console.WriteLine(e);
                throw;
            }

        }

        internal static string SafeMapHostName(string hostName)
        {
            IPAddress ip;
            return !string.IsNullOrEmpty(hostName) &&
                   IPAddress.TryParse(hostName, out ip)
                ? SafeMapIPv6(ip)
                : hostName;
        }

        private static string SafeMapIPv6(IPAddress ip) =>
            ip.AddressFamily == AddressFamily.InterNetworkV6
                ? "[" + ip + "]"
                : ip.ToString();
        /// <summary>
        /// Maps an Akka.NET address to correlated <see cref="EndPoint"/>.
        /// </summary>
        /// <param name="address">Akka.NET fully qualified node address.</param>
        /// <exception cref="ArgumentException">Thrown if address port was not provided.</exception>
        /// <returns><see cref="IPEndPoint"/> for IP-based addresses, <see cref="DnsEndPoint"/> for named addresses.</returns>
        public static EndPoint AddressToSocketAddress(Address address)
        {
            if (address.Port == null) throw new ArgumentException($"address port must not be null: {address}");
            EndPoint listenAddress;
            IPAddress ip;
            if (IPAddress.TryParse(address.Host, out ip))
            {
                listenAddress = new IPEndPoint(ip, (int)address.Port);
            }
            else
            {
                // DNS resolution will be performed by the transport
                listenAddress = new DnsEndPoint(address.Host, (int)address.Port);
            }
            return listenAddress;
        }
    }
}