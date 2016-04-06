//-----------------------------------------------------------------------
// <copyright file="AddressConverters.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using Akka.Actor;

namespace Akka.Remote.Transport.AkkaIO
{
    internal static class AddressConverters
    {
        public static Address ToAddress(this EndPoint endpoint, ActorSystem system)
        {
            var dns = endpoint as DnsEndPoint;
            if (dns != null)
                return new Address(AkkaIOTransport.Protocal, system.Name, dns.Host, dns.Port);
            var ip = endpoint as IPEndPoint;
            if (ip != null)
                return new Address(AkkaIOTransport.Protocal, system.Name, ip.Address.MapToIPv4().ToString(), ip.Port);
            throw new ArgumentException("endpoint");
        }

        public static EndPoint ToEndpoint(this Address address)
        {
            if (address == null || address.Host == null || !address.Port.HasValue)
                throw new ArgumentException("Invalid address", "address");
            return new DnsEndPoint(address.Host, address.Port.Value, AddressFamily.InterNetwork);
        }
    }
}