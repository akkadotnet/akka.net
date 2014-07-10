using System;
using System.Linq;
using System.Text;

namespace Akka.Actor
{
    /// <summary>
    ///     Class Address.
    /// </summary>
    public class Address : ICloneable, IEquatable<Address>
    {
        /// <summary>
        ///     Pseudo address for all systems
        /// </summary>
        public static readonly Address AllSystems = new Address("akka", "all-systems");

        /// <summary>
        ///     To string
        /// </summary>
        private readonly Lazy<string> _toString;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Address" /> class.
        /// </summary>
        /// <param name="protocol">The protocol.</param>
        /// <param name="system">The system.</param>
        /// <param name="host">The host.</param>
        /// <param name="port">The port.</param>
        public Address(string protocol, string system, string host = null, int? port = null)
        {
            Protocol = protocol;
            System = system;
            Host = host;
            Port = port;
            _toString = CreateLazyToString();
        }

        /// <summary>
        ///     Gets the host.
        /// </summary>
        /// <value>The host.</value>
        public string Host { get; private set; }

        /// <summary>
        ///     Gets the port.
        /// </summary>
        /// <value>The port.</value>
        public int? Port { get; private set; }

        /// <summary>
        ///     Gets the system.
        /// </summary>
        /// <value>The system.</value>
        public string System { get; private set; }

        /// <summary>
        ///     Gets the protocol.
        /// </summary>
        /// <value>The protocol.</value>
        public string Protocol { get; private set; }


        private Lazy<string> CreateLazyToString()
        {
            return new Lazy<string>(() =>
            {
                var sb = new StringBuilder();
                sb.AppendFormat("{0}://{1}", Protocol, System);
                if(!string.IsNullOrWhiteSpace(Host))
                    sb.AppendFormat("@{0}", Host);
                if(Port.HasValue)
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
            if(ReferenceEquals(null, other)) return false;
            if(ReferenceEquals(this, other)) return true;
            return string.Equals(Host, other.Host) && Port == other.Port && string.Equals(System, other.System) && string.Equals(Protocol, other.Protocol);
        }

        public override bool Equals(object obj)
        {
            if(ReferenceEquals(null, obj)) return false;
            if(ReferenceEquals(this, obj)) return true;
            if(obj.GetType() != this.GetType()) return false;
            return Equals((Address) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Host != null ? Host.GetHashCode() : 0);
                hashCode = (hashCode*397) ^ Port.GetHashCode();
                hashCode = (hashCode*397) ^ (System != null ? System.GetHashCode() : 0);
                hashCode = (hashCode*397) ^ (Protocol != null ? Protocol.GetHashCode() : 0);
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

            if(string.IsNullOrEmpty(uri.UserInfo))
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
    }
}