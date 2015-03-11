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
        ///     Pseudo address for all systems
        /// </summary>
        public static readonly Address AllSystems = new Address("akka", "all-systems");

        /// <summary>
        ///     To string
        /// </summary>
        private readonly Lazy<string> _toString;

        private readonly string _host;
        private readonly int? _port;
        private readonly string _system;
        private readonly string _protocol;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Address" /> class.
        /// </summary>
        /// <param name="protocol">The protocol.</param>
        /// <param name="system">The system.</param>
        /// <param name="host">The host.</param>
        /// <param name="port">The port.</param>
        public Address(string protocol, string system, string host = null, int? port = null)
        {
            _protocol = protocol;
            _system = system;
            _host = host;
            _port = port;
            _toString = CreateLazyToString();
        }

        /// <summary>
        ///     Gets the host.
        /// </summary>
        /// <value>The host.</value>
        public string Host
        {
            get { return _host; }
        }

        /// <summary>
        ///     Gets the port.
        /// </summary>
        /// <value>The port.</value>
        public int? Port
        {
            get { return _port; }
        }

        /// <summary>
        ///     Gets the system.
        /// </summary>
        /// <value>The system.</value>
        public string System
        {
            get { return _system; }
        }

        /// <summary>
        ///     Gets the protocol.
        /// </summary>
        /// <value>The protocol.</value>
        public string Protocol
        {
            get { return _protocol; }
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

        /// <summary>
        ///     Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <returns>A <see cref="string" /> that represents this instance.</returns>
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

        public Address Copy(string protocol = null, string system = null, string host = null, int? port = null)
        {
            return new Address(protocol ?? Protocol, system ?? System, host ?? Host, port ?? Port);
        }


        public static bool operator ==(Address left, Address right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(Address left, Address right)
        {
            return !Equals(left, right);
        }

        /// <summary>
        ///     Hosts the port.
        /// </summary>
        /// <returns>System.String.</returns>
        public string HostPort()
        {
            return ToString().Substring(Protocol.Length + 3);
        }

        #region Static Methods

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
            //if (!protocol.ToLowerInvariant().StartsWith("akka"))
            //    protocol = string.Format("akka.{0}", protocol);

            if (string.IsNullOrEmpty(uri.UserInfo))
            {
                string systemName = uri.Host;
                return new Address(protocol, systemName, null, null);
            }
            else
            {
                string systemName = uri.UserInfo;
                string host = uri.Host;
                int port = uri.Port;
                return new Address(protocol, systemName, host, port);
            }
        }

        #endregion

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
                if (!Uri.IsWellFormedUriString(addr, UriKind.RelativeOrAbsolute))
                {
                    //hack to cause the URI not to explode when we're only given an actor name
                    finalAddr = "/" + addr;
                }
                var uri = new Uri(finalAddr, UriKind.RelativeOrAbsolute);
                if (uri.IsAbsoluteUri) return null;

                return finalAddr.Split('/').SkipWhile(string.IsNullOrEmpty);
            }
            catch (UriFormatException ex)
            {
                return null;
            }
        }
    }
}