//-----------------------------------------------------------------------
// <copyright file="AddressConverters.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
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

    public static class IPExtensions
    {
        internal static object GetInstanceField(Type type, object instance, string fieldName)
        {
            BindingFlags bindFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

            FieldInfo field = type.GetField(fieldName, bindFlags);
            return field.GetValue(instance);
        }

        public static IPAddress MapToIPv4(this IPAddress ipa)
        {
            ushort[] m_Numbers = GetInstanceField(typeof(IPAddress), ipa, "m_Numbers") as ushort[];

            if (m_Numbers == null)
                throw new Exception("IPAddress.m_Numbers not found");

            if (ipa.AddressFamily == AddressFamily.InterNetwork)
                return ipa;

            if (ipa.AddressFamily != AddressFamily.InterNetworkV6)
                throw new Exception("Only AddressFamily.InterNetworkV6 can be converted to IPv4");

            // Cast the ushort values to a uint and mask with unsigned literal before bit shifting.
            // Otherwise, we can end up getting a negative value for any IPv4 address that ends with
            // a byte higher than 127 due to sign extension of the most significant 1 bit.
            long address = (((m_Numbers[6] & 0x0000FF00u) >> 8) | ((m_Numbers[6] & 0x000000FFu) << 8)) |
                    ((((m_Numbers[7] & 0x0000FF00u) >> 8) | ((m_Numbers[7] & 0x000000FFu) << 8)) << 16);

            return new IPAddress(address);
        }
    }
}