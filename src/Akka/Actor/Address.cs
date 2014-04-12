using System;
using System.Linq;
using System.Text;

namespace Akka.Actor
{
    /// <summary>
    ///     Class Address.
    /// </summary>
    public class Address : ICloneable
    {
        /// <summary>
        ///     Pseudo address for all systems
        /// </summary>
        public static readonly Address AllSystems = new Address("akka", "all-systems");

        /// <summary>
        ///     To string
        /// </summary>
        private Lazy<string> toString;

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
            CreateLazyToString();
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

        private void CreateLazyToString()
        {
            toString = new Lazy<string>(() =>
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
            return toString.Value;
        }

        /// <summary>
        ///     Returns a hash code for this instance.
        /// </summary>
        /// <returns>A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table.</returns>
        public override int GetHashCode()
        {
            return ToString().GetHashCode();
        }

        public object Clone()
        {
            return new Address(Protocol, System, Host, Port);
        }

        public Address Copy(string protocol = null, string system = null, string host = null, int? port = null)
        {
            return new Address(protocol ?? Protocol, system ?? System, host ?? Host, port ?? Port);
        }

        //TODO: implement real equals checks instead
        /// <summary>
        ///     Determines whether the specified <see cref="object" /> is equal to this instance.
        /// </summary>
        /// <param name="obj">The object to compare with the current object.</param>
        /// <returns><c>true</c> if the specified <see cref="object" /> is equal to this instance; otherwise, <c>false</c>.</returns>
        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;

            return ToString() == obj.ToString();
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
            if (!protocol.ToLowerInvariant().StartsWith("akka"))
                protocol = string.Format("akka.{0}", protocol);

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
    }
}