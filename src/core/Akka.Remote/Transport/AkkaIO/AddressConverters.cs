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
    /// This class contains extension methods used to convert <see cref="EndPoint">EndPoints</see>
    /// to <see cref="Address">Addresses</see> and vise versa.
    /// </summary>
    internal static class AddressConverters
    {
        /// <summary>
        /// Creates a new <see cref="Address"/> from a specific <paramref name="endpoint"/>.
        /// </summary>
        /// <param name="endpoint">The endpoint to convert to an address</param>
        /// <param name="system">The actor system to use when creating the address.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified endpoint is neither a <see cref="DnsEndPoint"/> nor a <see cref="IPEndPoint"/>.
        /// </exception>
        /// <returns>A new address based on the specified endpoint.</returns>
        public static Address ToAddress(this EndPoint endpoint, ActorSystem system)
        {
            var dns = endpoint as DnsEndPoint;
            if (dns != null)
                return new Address(AkkaIOTransport.Protocal, system.Name, dns.Host, dns.Port);
            var ip = endpoint as IPEndPoint;
            if (ip != null)
                return new Address(AkkaIOTransport.Protocal, system.Name, IpExtensions.MapToIPv4(ip.Address).ToString(), ip.Port);
            throw new ArgumentException("The specified endpoint needs to be either a DnsEndPoint or an IPEndPoint.", nameof(endpoint));
        }

        /// <summary>
        /// Creates a new <see cref="EndPoint"/> from a specific <paramref name="address"/>.
        /// </summary>
        /// <param name="address">The address to convert to an endpoint.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified address is invalid.
        /// </exception>
        /// <returns>The new endpoint based on the specified address.</returns>
        public static EndPoint ToEndpoint(this Address address)
        {
            if (address == null || address.Host == null || !address.Port.HasValue)
                throw new ArgumentException("Invalid address", nameof(address));
            return new DnsEndPoint(address.Host, address.Port.Value, AddressFamily.InterNetwork);
        }
    }
}