//-----------------------------------------------------------------------
// <copyright file="IPExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

namespace Akka.Remote
{
    /// <summary>
    /// Used primarily for Mono support for IP type mapping
    /// 
    /// INTERNAL API
    /// </summary>
    internal static class IpExtensions
    {
        /// <summary>
        /// Reads a field by name using reflection, including non-public instance and static fields.
        /// </summary>
        /// <param name="type">The type that declares the field.</param>
        /// <param name="instance">The object from which to read the field value.</param>
        /// <param name="fieldName">The field's name.</param>
        /// <returns>The value of the field on <paramref name="instance"/>.</returns>
        internal static object GetInstanceField(Type type, object instance, string fieldName)
        {
            BindingFlags bindFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

            FieldInfo field = type.GetField(fieldName, bindFlags);
            return field.GetValue(instance);
        }

        /// <summary>
        /// Converts an IPv6 address to an IPv4 address using its final 32 bits, leaving IPv4 addresses unchanged.
        /// </summary>
        /// <param name="ipa">The IP address to map.</param>
        /// <returns>The corresponding IPv4 address.</returns>
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

        /// <summary>
        /// Maps an IPv4 address to its IPv4-mapped IPv6 representation, leaving IPv6 addresses unchanged.
        /// </summary>
        /// <param name="ipa">The IP address to map.</param>
        /// <returns>The corresponding IPv4-mapped IPv6 address.</returns>
        public static IPAddress MapToIPv6(this IPAddress ipa)
        {
            if (ipa.AddressFamily == AddressFamily.InterNetworkV6)
                return ipa;
            if (ipa.AddressFamily != AddressFamily.InterNetwork)
                throw new Exception("Only AddressFamily.InterNetworkV4 can be converted to IPv6");

            byte[] ipv4Bytes = ipa.GetAddressBytes();
            byte[] ipv6Bytes = new byte[16] {
                0,0, 0,0, 0,0, 0,0, 0,0, 0xFF,0xFF,
                ipv4Bytes [0], ipv4Bytes [1], ipv4Bytes [2], ipv4Bytes [3]
            };
            return new IPAddress(ipv6Bytes);
        }
    }
}
