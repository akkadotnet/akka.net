//-----------------------------------------------------------------------
// <copyright file="ServiceDiscovery.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;

namespace Akka.Discovery
{
    /// <summary>
    /// Implement to provide a service discovery method
    /// </summary>
    public abstract class ServiceDiscovery
    {
        public class Resolved : IDeadLetterSuppression, INoSerializationVerificationNeeded, IEquatable<Resolved>
        {
            public string ServiceName { get; }
            public ImmutableList<ResolvedTarget> Addresses { get; }

            /// <summary>
            /// Result of a successful resolve request
            /// </summary>
            /// <param name="serviceName">TBD</param>
            /// <param name="addresses">TBD</param>
            public Resolved(string serviceName, IEnumerable<ResolvedTarget> addresses)
            {
                ServiceName = serviceName;
                Addresses = addresses != null ? addresses.ToImmutableList() : ImmutableList<ResolvedTarget>.Empty;
            }

            public override string ToString() => $"Resolved({ServiceName},{string.Join(", ", Addresses)})";

            public bool Equals(Resolved other)
            {
                if (other is null) return false;
                if (ReferenceEquals(this, other)) return true;
                return ServiceName == other.ServiceName && Addresses.SequenceEqual(other.Addresses);
            }

            public override bool Equals(object obj)
            {
                if (obj is null) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj.GetType() == GetType() && Equals((Resolved)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((ServiceName?.GetHashCode() ?? 0) * 397) ^ (Addresses?.GetHashCode() ?? 0);
                }
            }
        }

        public class ResolvedTarget : INoSerializationVerificationNeeded, IEquatable<ResolvedTarget>
        {
            public string Host { get; }
            public int? Port { get; }
            public IPAddress Address { get; }

            /// <summary>
            /// Resolved target host, with optional port and the IP address.
            /// </summary>
            /// <param name="host">The hostname or the IP address of the target.</param>
            /// <param name="port">Optional port number.</param>
            /// <param name="address">Optional IP address of the target. This is used during cluster bootstrap when available.</param>
            public ResolvedTarget(string host, int? port = null, IPAddress address = null)
            {
                Host = host;
                Port = port;
                Address = address;
            }

            public override string ToString() => $"ResolvedTarget({Host}{Port}{Address})";

            public bool Equals(ResolvedTarget other)
            {
                if (other is null) return false;
                if (ReferenceEquals(this, other)) return true;
                return Host == other.Host && Port == other.Port && Equals(Address, other.Address);
            }

            public override bool Equals(object obj)
            {
                if (obj is null) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj.GetType() == GetType() && Equals((ResolvedTarget)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = Host?.GetHashCode() ?? 0;
                    hashCode = (hashCode * 397) ^ (Port?.GetHashCode() ?? 0);
                    hashCode = (hashCode * 397) ^ (Address?.GetHashCode() ?? 0);
                    return hashCode;
                }
            }
        }

        /// <summary>
        /// Perform lookup using underlying discovery implementation.
        /// </summary>
        /// <param name="lookup">A service discovery lookup.</param>
        /// <param name="resolveTimeout">Timeout. Up to the discovery-method to adhere to this</param>
        public abstract Task<Resolved> Lookup(Lookup lookup, TimeSpan resolveTimeout);

        /// <summary>
        /// <para>Perform lookup using underlying discovery implementation.</para>
        /// <para>
        /// While the implementation may provide other settings and ways to configure timeouts,
        /// the passed `resolveTimeout` should never be exceeded, as it signals the application's
        /// eagerness to wait for a result for this specific lookup.
        /// </para>
        /// </summary>
        /// <param name="serviceName">A name, see discovery-method's docs for how this is interpreted.</param>
        /// <param name="resolveTimeout">Timeout. Up to the discovery-method to adhere to this</param>
        public Task<Resolved> Lookup(string serviceName, TimeSpan resolveTimeout) => Lookup(new Lookup(serviceName), resolveTimeout);
    }

    /// <summary>
    /// A service lookup. It is up to each method to decide what to do with the optional portName and protocol fields.
    /// For example `portName` could be used to distinguish between Akka remoting ports and HTTP ports.
    /// </summary>
    public class Lookup : INoSerializationVerificationNeeded, IEquatable<Lookup>
    {
        private static readonly Regex srvQueryRegex = new Regex(@"^_(.+?)\._(.+?)\.(.+?)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        // Validates domain name:
        // (as defined in https://tools.ietf.org/html/rfc1034)
        // 
        // - a label has 1 to 63 chars
        // - valid chars for a label are: a-z, A-Z, 0-9 and -
        // - a label can't start with a 'hyphen' (-)
        // - a label can't start with a 'digit' (0-9)
        // - a label can't end with a 'hyphen' (-)
        // - labels are separated by a 'dot' (.)
        // 
        //  Starts with a label:
        //  Label Pattern: (?![0-9-])[A-Za-z0-9-]{1,63}(?<!-)
        //       (?![0-9-]) => negative look ahead, first char can't be hyphen (-) or digit (0-9)
        //       [A-Za-z0-9-]{1,63} => digits, letters and hyphen, from 1 to 63
        //       (?<!-) => negative look behind, last char can't be hyphen (-)
        // 
        //  A label can be followed by other labels:
        //     Pattern: (\.(?![0-9-])[A-Za-z0-9-]{1,63}(?<!-)))*
        //       . => separated by a . (dot)
        //       label pattern => (?![0-9-])[A-Za-z0-9-]{1,63}(?<!-)
        //       * => match zero or more times 
        private static readonly Regex domainNameRegex = new Regex(@"^((?![0-9-])[A-Za-z0-9-]{1,63}(?<!-))((\.(?![0-9-])[A-Za-z0-9-]{1,63}(?<!-)))*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>
        /// Create a service Lookup with <paramref name="serviceName"/>, optional <paramref name="portName"/> and optional <paramref name="protocol"/>.
        /// Use <see cref="WithPortName"/> and <see cref="WithProtocol"/> to provide optional <paramref name="portName"/> and <paramref name="protocol"/>.
        /// </summary>
        /// <param name="serviceName">Must not be 'null' or an empty string</param>
        /// <param name="portName">Optional port name</param>
        /// <param name="protocol">Optional protocol</param>
        public Lookup(string serviceName, string portName = null, string protocol = null)
        {
            if (string.IsNullOrEmpty(serviceName)) throw new ArgumentNullException(nameof(serviceName), "cannot be null or empty");

            ServiceName = serviceName;
            PortName = portName;
            Protocol = protocol;
        }

        public string ServiceName { get; }
        public string PortName { get; }
        public string Protocol { get; }

        /// <summary>
        /// Which port for a service e.g. Akka remoting or HTTP.
        /// Maps to "service" for an SRV records.
        /// </summary>
        public Lookup WithPortName(string portName) => Copy(portName: portName);

        /// <summary>
        /// Which protocol e.g. TCP or UDP.
        /// Maps to "protocol" for SRV records.
        /// </summary>
        public Lookup WithProtocol(string protocol) => Copy(protocol: protocol);

        /// <summary>
        /// <para>
        /// Create a service Lookup from a string with format:
        /// _portName._protocol.serviceName.
        /// (as specified by https://www.ietf.org/rfc/rfc2782.txt)
        /// </para>
        /// <para>
        /// If the passed string conforms with this format, a SRV Lookup is returned.
        /// The serviceName part must be a valid domain name.
        /// (as defined in https://tools.ietf.org/html/rfc1034)
        /// </para>
        /// The string is parsed and dismembered to build a Lookup as following:
        /// Lookup(serviceName).WithPortName(portName).WithProtocol(protocol)
        /// </summary>
        /// <exception cref="ArgumentNullException">If the passed string is null.</exception>
        /// <exception cref="ArgumentException">If the string does not conform with the SRV format.</exception>
        public static Lookup ParseSrv(string srv)
        {
            if (srv == null)
                throw new ArgumentNullException(nameof(srv), "Unable to create Lookup from passed SRV string. Passed value is 'null'");

            var match = srvQueryRegex.Match(srv);
            if (match.Success && IsValidDomainName(match.Groups[3].Value))
            {
                return new Lookup(match.Groups[3].Value, match.Groups[1].Value, match.Groups[2].Value);
            }

            throw new ArgumentException($"Unable to create Lookup from passed SRV string, invalid format: {srv}");
        }

        /// <summary>
        /// Returns true if passed string conforms with SRV format. Otherwise returns false.
        /// </summary>
        public static bool IsValid(string srv)
        {
            if (string.IsNullOrEmpty(srv)) return false;

            var match = srvQueryRegex.Match(srv);
            return match.Success && IsValidDomainName(match.Groups[3].Value);
        }

        private static bool IsValidDomainName(string name) => domainNameRegex.IsMatch(name);

        public override string ToString() => $"Lookup({ServiceName}{PortName}{Protocol})";

        public Lookup Copy(string serviceName = null, string portName = null, string protocol = null) =>
            new Lookup(serviceName ?? ServiceName, portName ?? PortName, protocol ?? Protocol);

        public bool Equals(Lookup other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return ServiceName == other.ServiceName && PortName == other.PortName && Protocol == other.Protocol;
        }

        public override bool Equals(object obj)
        {
            if (obj is null) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj.GetType() == GetType() && Equals((Lookup)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = ServiceName?.GetHashCode() ?? 0;
                hashCode = (hashCode * 397) ^ (PortName?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ (Protocol?.GetHashCode() ?? 0);
                return hashCode;
            }
        }
    }
}
