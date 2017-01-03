//-----------------------------------------------------------------------
// <copyright file="AddressConverters.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using Akka.Remote;
using Akka.Actor;

namespace Akka.Remote.Transport.AkkaIO
{
    /// <summary>
    /// TBD
    /// </summary>
    internal static class AddressConverters
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="endpoint">TBD</param>
        /// <param name="system">TBD</param>
        /// <exception cref="ArgumentException">TbD</exception>
        /// <returns>TBD</returns>
        public static Address ToAddress(this EndPoint endpoint, ActorSystem system)
        {
            var dns = endpoint as DnsEndPoint;
            if (dns != null)
                return new Address(AkkaIOTransport.Protocal, system.Name, dns.Host, dns.Port);
            var ip = endpoint as IPEndPoint;
            if (ip != null)
                return new Address(AkkaIOTransport.Protocal, system.Name, IpExtensions.MapToIPv4(ip.Address).ToString(), ip.Port);
            throw new ArgumentException("endpoint");
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="address">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <returns>TBD</returns>
        public static EndPoint ToEndpoint(this Address address)
        {
            if (address == null || address.Host == null || !address.Port.HasValue)
                throw new ArgumentException("Invalid address", "address");
            return new DnsEndPoint(address.Host, address.Port.Value, AddressFamily.InterNetwork);
        }
    }
}