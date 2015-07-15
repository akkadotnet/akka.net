//-----------------------------------------------------------------------
// <copyright file="AddressConverters.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using Akka.Actor;

namespace Akka.Remote.AkkaIOTransport
{
    static class AddressConverters
    {
        public static Address ToAddress(this EndPoint endpoint, ActorSystem system)
        {
            var dns = endpoint as DnsEndPoint;
            if (dns != null)
                return new Address(AkkaIOTransport.Protocal, system.Name, dns.Host, dns.Port);
            var ip = endpoint as IPEndPoint;
            if (ip != null)
                return new Address(AkkaIOTransport.Protocal, system.Name, "127.0.0.1", ip.Port);
            throw new ArgumentException("endpoint");
        }

        public static EndPoint ToEndpoint(this Address address)
        {
            return new DnsEndPoint(address.Host, address.Port.GetValueOrDefault(9099), AddressFamily.InterNetwork);
        }
    }
}