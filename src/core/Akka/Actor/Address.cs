//-----------------------------------------------------------------------
// <copyright file="Address.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Akka.Util;

namespace Akka.Actor
{
    /// <summary>
    /// The address specifies the physical location under which an Actor can be
    /// reached. Examples are local addresses, identified by the <see cref="ActorSystem"/>'s
    /// name, and remote addresses, identified by protocol, host and port.
    ///  
    /// This class is sealed to allow use as a case class (copy method etc.); if
    /// for example a remote transport would want to associate additional
    /// information with an address, then this must be done externally.
    /// </summary>
    public sealed class Address : IEquatable<Address>, IComparable<Address>, IComparable, ISurrogated
#if CLONEABLE
        , ICloneable
#endif
    {
        #region comparer

        private sealed class AddressComparer : IComparer<Address>
        {
            public int Compare(Address x, Address y)
            {
                if (x == null) throw new ArgumentNullException(nameof(x));
                if (y == null) throw new ArgumentNullException(nameof(y));

                if (ReferenceEquals(x, y)) return 0;

                var result = string.CompareOrdinal(x.Protocol, y.Protocol);
                if (result != 0) return result;
                result = string.CompareOrdinal(x.System, y.System);
                if (result != 0) return result;
                result = string.CompareOrdinal(x.Host ?? "", y.Host ?? "");
                if (result != 0) return result;
                result = (x.Port ?? 0).CompareTo(y.Port ?? 0);
                return result;
            }
        }

        #endregion

        /// <summary>
        /// An <see cref="Address"/> comparer. Compares two addresses by their protocol, name, host and port.
        /// </summary>
        public static readonly IComparer<Address> Comparer = new AddressComparer();

        /// <summary>
        /// Pseudo address for all systems
        /// </summary>
        public static readonly Address AllSystems = new Address("akka", "all-systems");

        private readonly Lazy<string> _toString;
        private readonly string _host;
        private readonly int? _port;
        private readonly string _system;
        private readonly string _protocol;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="protocol">TBD</param>
        /// <param name="system">TBD</param>
        /// <param name="host">TBD</param>
        /// <param name="port">TBD</param>
        public Address(string protocol, string system, string host = null, int? port = null)
        {
            _protocol = protocol;
            _system = system;
            _host = host?.ToLowerInvariant();
            _port = port;
            _toString = CreateLazyToString();
        }

        /// <summary>
        /// TBD
        /// </summary>
        public string Host => _host;

        /// <summary>
        /// TBD
        /// </summary>
        public int? Port => _port;

        /// <summary>
        /// TBD
        /// </summary>
        public string System => _system;

        /// <summary>
        /// TBD
        /// </summary>
        public string Protocol => _protocol;

        /// <summary>
        /// Returns true if this Address is only defined locally. It is not safe to send locally scoped addresses to remote
        ///  hosts. See also <see cref="HasGlobalScope"/>
        /// </summary>
        public bool HasLocalScope => string.IsNullOrEmpty(Host);

        /// <summary>
        /// Returns true if this Address is usable globally. Unlike locally defined addresses <see cref="HasLocalScope"/>
        /// addresses of global scope are safe to sent to other hosts, as they globally and uniquely identify an addressable
        /// entity.
        /// </summary>
        public bool HasGlobalScope => !string.IsNullOrEmpty(Host);

        private Lazy<string> CreateLazyToString()
        {
            return new Lazy<string>(() =>
            {
                var sb = new StringBuilder();
                sb.AppendFormat("{0}://{1}", Protocol, System);
                if (!string.IsNullOrWhiteSpace(Host))
                    sb.AppendFormat("@{0}", Host);
                if (Port.HasValue)
                    sb.AppendFormat(":{0}", Port.Value);

                return sb.ToString();
            }, true);
        }

        /// <summary>
        /// Compares current address with provided one by their protocol, name, host and port.
        /// </summary>
        /// <param name="other">Other address to compare with.</param>
        /// <returns></returns>
        public int CompareTo(Address other)
        {
            return Comparer.Compare(this, other);
        }

        /// <inheritdoc/>
        public override string ToString() => _toString.Value;

        /// <inheritdoc/>
        public bool Equals(Address other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Port == other.Port && string.Equals(Host, other.Host) && string.Equals(System, other.System) && string.Equals(Protocol, other.Protocol);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is Address && Equals((Address)obj);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Host != null ? Host.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ Port.GetHashCode();
                hashCode = (hashCode * 397) ^ (System != null ? System.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Protocol != null ? Protocol.GetHashCode() : 0);
                return hashCode;
            }
        }

        int IComparable.CompareTo(object obj)
        {
            if (obj is Address address) return CompareTo(address);

            throw new ArgumentException($"Cannot compare {nameof(Address)} with instance of type '{obj?.GetType().FullName ?? "null"}'.");
        }

        /// <summary>
        /// Creates a new copy with the same properties as the current address.
        /// </summary>
        /// <returns>A new copy of the current address</returns>
        public object Clone()
        {
            return new Address(Protocol, System, Host, Port);
        }

        /// <summary>
        /// Creates a new <see cref="Address"/> with a given <paramref name="protocol"/>.
        /// </summary>
        /// <note>
        /// This method is immutable and returns a new instance of the address.
        /// </note>
        /// <param name="protocol">The protocol used to configure the new address.</param>
        /// <returns>A new address with the provided <paramref name="protocol" />.</returns>
        public Address WithProtocol(string protocol)
        {
            return new Address(protocol, System, Host, Port);
        }

        /// <summary>
        /// Creates a new <see cref="Address"/> with a given <paramref name="system"/>.
        /// </summary>
        /// <note>
        /// This method is immutable and returns a new instance of the address.
        /// </note>
        /// <param name="system">The system used to configure the new address.</param>
        /// <returns>A new address with the provided <paramref name="system" />.</returns>
        public Address WithSystem(string system)
        {
            return new Address(Protocol, system, Host, Port);
        }

        /// <summary>
        /// Creates a new <see cref="Address"/> with a given <paramref name="host"/>.
        /// </summary>
        /// <note>
        /// This method is immutable and returns a new instance of the address.
        /// </note>
        /// <param name="host">The host used to configure the new address.</param>
        /// <returns>A new address with the provided <paramref name="host" />.</returns>
        public Address WithHost(string host = null)
        {
            return new Address(Protocol, System, host, Port);
        }

        /// <summary>
        /// Creates a new <see cref="Address"/> with a given <paramref name="port"/>.
        /// </summary>
        /// <note>
        /// This method is immutable and returns a new instance of the address.
        /// </note>
        /// <param name="port">The port used to configure the new address.</param>
        /// <returns>A new address with the provided <paramref name="port" />.</returns>
        public Address WithPort(int? port = null)
        {
            return new Address(Protocol, System, Host, port);
        }

        /// <summary>
        /// Compares two specified addresses for equality.
        /// </summary>
        /// <param name="left">The first address used for comparison</param>
        /// <param name="right">The second address used for comparison</param>
        /// <returns><c>true</c> if both addresses are equal; otherwise <c>false</c></returns>
        public static bool operator ==(Address left, Address right)
        {
            return Equals(left, right);
        }

        /// <summary>
        /// Compares two specified addresses for inequality.
        /// </summary>
        /// <param name="left">The first address used for comparison</param>
        /// <param name="right">The second address used for comparison</param>
        /// <returns><c>true</c> if both addresses are not equal; otherwise <c>false</c></returns>
        public static bool operator !=(Address left, Address right)
        {
            return !Equals(left, right);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public string HostPort()
        {
            return ToString().Substring(Protocol.Length + 3);
        }

        /// <summary>
        /// Parses a new <see cref="Address"/> from a given string
        /// </summary>
        /// <param name="address">The address to parse</param>
        /// <returns>A populated <see cref="Address"/> object with host and port included, if available</returns>
        /// <exception cref="UriFormatException">Thrown if the address is not able to be parsed</exception>
        public static Address Parse(string address)
        {
            var uri = new Uri(address);
            var protocol = uri.Scheme;

            if (string.IsNullOrEmpty(uri.UserInfo))
            {
                var systemName = uri.Host;
                
                return new Address(protocol, systemName);
            }
            else
            {
                var systemName = uri.UserInfo;
                var host = uri.Host;
                /*
                 * Aaronontheweb: in the event that an Address is passed in with port 0, the Uri converts it to -1 (which is invalid.)
                 */
                var port = uri.Port;

                return new Address(protocol, systemName, host, port);
            }
        }

        /// <summary>
        /// This class represents a surrogate of an <see cref="Address"/>.
        /// Its main use is to help during the serialization process.
        /// </summary>
        public class AddressSurrogate : ISurrogate
        {
            /// <summary>
            /// TBD
            /// </summary>
            public string Protocol { get; set; }
            /// <summary>
            /// TBD
            /// </summary>
            public string System { get; set; }
            /// <summary>
            /// TBD
            /// </summary>
            public string Host { get; set; }
            /// <summary>
            /// TBD
            /// </summary>
            public int? Port { get; set; }
            /// <summary>
            /// Creates a <see cref="Address"/> encapsulated by this surrogate.
            /// </summary>
            /// <param name="system">The actor system that owns this router.</param>
            /// <returns>The <see cref="Address"/> encapsulated by this surrogate.</returns>
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new Address(Protocol, System, Host, Port);
            }
        }

        /// <summary>
        /// Creates a surrogate representation of the current <see cref="Address"/>.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The surrogate representation of the current <see cref="Address"/>.</returns>
        public ISurrogate ToSurrogate(ActorSystem system)
        {
            return new AddressSurrogate()
            {
                Host = Host,
                Port = Port,
                System = System,
                Protocol = Protocol
            };
        }
    }

    /// <summary>
    /// Extractor class for so-called "relative actor paths" - as in "relative URI", not
    /// "relative to some other actors."
    /// 
    /// Examples:
    /// 
    ///  * "grand/child"
    ///  * "/user/hello/world"
    /// </summary>
    public static class RelativeActorPath
    {

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="addr">TBD</param>
        /// <returns>TBD</returns>
        public static IEnumerable<string> Unapply(string addr)
        {
            try
            {
                Uri uri;
                bool isRelative = Uri.TryCreate(addr, UriKind.Relative, out uri);
                if (!isRelative) return null;

                var finalAddr = addr;
                if (!addr.StartsWith("/"))
                {
                    //hack to cause the URI not to explode when we're only given an actor name
                    finalAddr = "/" + addr;
                }

                return finalAddr.Split('/').SkipWhile(string.IsNullOrEmpty);
            }
            catch (UriFormatException)
            {
                return null;
            }
        }
    }
}

