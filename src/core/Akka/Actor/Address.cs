//-----------------------------------------------------------------------
// <copyright file="Address.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Akka.Util;

namespace Akka.Actor
{
    /// <summary>
    ///  The address specifies the physical location under which an Actor can be
    ///  reached. Examples are local addresses, identified by the <see cref="ActorSystem"/>'s
    /// name, and remote addresses, identified by protocol, host and port.
    ///  
    /// This class is sealed to allow use as a case class (copy method etc.); if
    /// for example a remote transport would want to associate additional
    /// information with an address, then this must be done externally.
    /// </summary>
    public sealed class Address : ICloneable, IEquatable<Address>, ISurrogated
    {
        /// <summary>
        /// Pseudo address for all systems
        /// </summary>
        public static readonly Address AllSystems = new Address("akka", "all-systems");

        private readonly Lazy<string> _toString;
        private readonly string _host;
        private readonly int? _port;
        private readonly string _system;
        private readonly string _protocol;

        public Address(string protocol, string system, string host = null, int? port = null)
        {
            _protocol = protocol;
            _system = system;
            _host = host != null ? host.ToLowerInvariant() : null;
            _port = port;
            _toString = CreateLazyToString();
        }

        public string Host
        {
            get { return _host; }
        }

        public int? Port
        {
            get { return _port; }
        }

        public string System
        {
            get { return _system; }
        }

        public string Protocol
        {
            get { return _protocol; }
        }

        public bool HasLocalScope
        {
            get { return _host == null; }
        }

        public bool HasGlobalScope
        {
            get { return _host != null; }
        }

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

        public override string ToString()
        {
            return _toString.Value;
        }

        public bool Equals(Address other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return string.Equals(Host, other.Host) && Port == other.Port && string.Equals(System, other.System) && string.Equals(Protocol, other.Protocol);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Address)obj);
        }

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

        public object Clone()
        {
            return new Address(Protocol, System, Host, Port);
        }

        public Address WithProtocol(string protocol)
        {
            return new Address(protocol, System, Host, Port);
        }

        public Address WithSystem(string system)
        {
            return new Address(Protocol, system, Host, Port);
        }

        public Address WithHost(string host = null)
        {
            return new Address(Protocol, System, host, Port);
        }

        public Address WithPort(int? port = null)
        {
            return new Address(Protocol, System, Host, port);
        }

        public static bool operator ==(Address left, Address right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(Address left, Address right)
        {
            return !Equals(left, right);
        }

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
                var port = uri.Port;

                return new Address(protocol, systemName, host, port);
            }
        }

        public class AddressSurrogate : ISurrogate
        {
            public string Protocol { get; set; }
            public string System { get; set; }
            public string Host { get; set; }
            public int? Port { get; set; }
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new Address(Protocol,System,Host,Port);
            }
        }

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
        public static IEnumerable<string> Unapply(string addr)
        {
            try
            {
                var finalAddr = addr;
                // need to add a special case for URI fragments containing #, since those don't get counted
                // as relative URIs by C#
                if(Uri.IsWellFormedUriString(addr, UriKind.Absolute) || (!Uri.IsWellFormedUriString(addr, UriKind.Relative) 
                    && !addr.Contains("#"))) return null;
                if(!addr.StartsWith("/"))
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

