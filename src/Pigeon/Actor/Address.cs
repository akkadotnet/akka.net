using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Actor
{
    /// <summary>
    /// Class Address.
    /// </summary>
    public class Address
    {
        /// <summary>
        /// Pseudo address for all systems
        /// </summary>
        public static readonly Address AllSystems = new Address("akka", "all-systems");

        /// <summary>
        /// Initializes a new instance of the <see cref="Address"/> class.
        /// </summary>
        /// <param name="protocol">The protocol.</param>
        /// <param name="system">The system.</param>
        /// <param name="host">The host.</param>
        /// <param name="port">The port.</param>
        public Address(string protocol,string system, string host=null, int? port=null)
        {
            this.Protocol = protocol;
            this.System = system;
            this.Host = host;
            this.Port = port;
            toString = new Lazy<string>(() =>
            {
                var sb = new StringBuilder();
                sb.AppendFormat("{0}://{1}", this.Protocol, this.System);
                if (!string.IsNullOrWhiteSpace(Host))
                    sb.AppendFormat("@{0}", Host);
                if (Port.HasValue)
                    sb.AppendFormat(":{0}", Port.Value);

                return sb.ToString();
            }, true);
        }

        /// <summary>
        /// To string
        /// </summary>
        private Lazy<string> toString;
        /// <summary>
        /// Gets the host.
        /// </summary>
        /// <value>The host.</value>
        public string Host { get; private set; }
        /// <summary>
        /// Gets the port.
        /// </summary>
        /// <value>The port.</value>
        public int? Port { get; private set; }
        /// <summary>
        /// Gets the system.
        /// </summary>
        /// <value>The system.</value>
        public string System { get; private set; }
        /// <summary>
        /// Gets the protocol.
        /// </summary>
        /// <value>The protocol.</value>
        public string Protocol { get; private set; }

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>A <see cref="System.String" /> that represents this instance.</returns>
        public override string ToString()
        {
            return toString.Value;
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table.</returns>
        public override int GetHashCode()
        {
            return ToString().GetHashCode();
        }

        //TODO: implement real equals checks instead
        /// <summary>
        /// Determines whether the specified <see cref="System.Object" /> is equal to this instance.
        /// </summary>
        /// <param name="obj">The object to compare with the current object.</param>
        /// <returns><c>true</c> if the specified <see cref="System.Object" /> is equal to this instance; otherwise, <c>false</c>.</returns>
        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;

            return this.ToString() == obj.ToString();
        }

        /// <summary>
        /// Hosts the port.
        /// </summary>
        /// <returns>System.String.</returns>
        public string HostPort()
        {
            return this.ToString().Substring(Protocol.Length + 3);
        }
    }
}
