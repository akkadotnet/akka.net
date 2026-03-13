//-----------------------------------------------------------------------
// <copyright file="Dns.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using Akka.Actor;
using Akka.Configuration;
using Akka.Routing;

namespace Akka.IO
{
    /// <summary>
    /// Base class for DNS resolution backends.
    /// </summary>
    public abstract class DnsBase
    {
        /// <summary>
        /// Returns a cached DNS resolution for the given name, or null if not cached.
        /// </summary>
        /// <param name="name">The DNS name to look up in the cache.</param>
        /// <returns>The cached DNS resolution, or null if not found.</returns>
        public virtual Dns.Resolved Cached(string name)
        {
            return null;
        }
        /// <summary>
        /// Attempts to resolve the DNS name, using the cache if possible, otherwise triggers a DNS query.
        /// </summary>
        /// <param name="name">The DNS name to resolve.</param>
        /// <param name="system">The actor system to use for resolution.</param>
        /// <param name="sender">The actor requesting the resolution.</param>
        /// <returns>The resolved DNS entry, or null if not cached.</returns>
        public virtual Dns.Resolved Resolve(string name, ActorSystem system, IActorRef sender)
        {
            var ret = Cached(name);
            if (ret == null)
                Dns.Instance.Apply(system).Manager.Tell(new Dns.Resolve(name), sender);
            return ret;
        }
    }

    /// <summary>
    /// Extension for DNS resolution support in Akka.NET.
    /// </summary>
    public class Dns : ExtensionIdProvider<DnsExt>
    {
        /// <summary>
        /// The singleton instance of the Dns extension.
        /// </summary>
        public static readonly Dns Instance = new();

        /// <summary>
        /// Base class for DNS-related commands.
        /// </summary>
        public abstract class Command : INoSerializationVerificationNeeded
        { }

        /// <summary>
        /// Command to resolve a DNS name.
        /// </summary>
        public class Resolve : Command, IConsistentHashable
        {
            /// <summary>
            /// Creates a new DNS resolve command for the given name.
            /// </summary>
            /// <param name="name">The DNS name to resolve.</param>
            public Resolve(string name)
            {
                Name = name;
                ConsistentHashKey = name;
            }

            /// <summary>
            /// The consistent hash key for this command (the DNS name).
            /// </summary>
            public object ConsistentHashKey { get; private set; }
            /// <summary>
            /// The DNS name to resolve.
            /// </summary>
            public string Name { get; private set; }
        }

        /// <summary>
        /// Result of a DNS resolution.
        /// </summary>
        public class Resolved : Command
        {
            private readonly IPAddress _addr;

            public Resolved(string name, Exception ex) : this(name, null, null)
            {
                Exception = ex;
            }
            
            /// <summary>
            /// Creates a new resolved DNS entry.
            /// </summary>
            /// <param name="name">The DNS name that was resolved.</param>
            /// <param name="ipv4">The resolved IPv4 addresses.</param>
            /// <param name="ipv6">The resolved IPv6 addresses.</param>
            public Resolved(string name, IEnumerable<IPAddress> ipv4, IEnumerable<IPAddress> ipv6)
            {
                Name = name;
                Ipv4 = ipv4?.ToImmutableList() ?? ImmutableList<IPAddress>.Empty;
                Ipv6 = ipv6?.ToImmutableList() ?? ImmutableList<IPAddress>.Empty;

                _addr = Ipv4.FirstOrDefault() ?? Ipv6.FirstOrDefault();
            }

            public bool IsSuccess => Exception == null;
            
            public Exception Exception { get; }

            /// <summary>
            /// The DNS name that was resolved.
            /// </summary>
            public string Name { get; }
            /// <summary>
            /// The resolved IPv4 addresses.
            /// </summary>
            public IEnumerable<IPAddress> Ipv4 { get; }
            /// <summary>
            /// The resolved IPv6 addresses.
            /// </summary>
            public IEnumerable<IPAddress> Ipv6 { get; }

            /// <summary>
            /// The first resolved address, or throws if resolution failed.
            /// </summary>
            public IPAddress Addr
            {
                get
                {
                    if(Exception != null)
                        ExceptionDispatchInfo.Capture(Exception).Throw();
                    else
                        if (_addr == null) throw new Exception("Unknown host");
                    
                    return _addr;
                }
            }

            /// <summary>
            /// Creates a new resolved DNS entry from a set of addresses.
            /// </summary>
            /// <param name="name">The DNS name that was resolved.</param>
            /// <param name="addresses">The resolved addresses.</param>
            /// <returns>A new <see cref="Resolved"/> instance.</returns>
            public static Resolved Create(string name, IEnumerable<IPAddress> addresses)
            {
                /*
                 * Materialize addresses into a list here so we can avoid multiple enumeration.
                 * 
                 * Yes, allocates a list but the results of this operation are cached anyway.
                 * The cost of missing the correct DNS entry carries a much higher performance cost.
                 */
                var addressM = addresses.ToList();
                var ipv4 = addressM.Where(x => x.AddressFamily == AddressFamily.InterNetwork).ToList();
                var ipv6 = addressM.Where(x => x.AddressFamily == AddressFamily.InterNetworkV6).ToList();
                return new Resolved(name, ipv4, ipv6);
            }
        }

        /// <summary>
        /// Returns a cached DNS resolution for the given name, or null if not cached.
        /// </summary>
        /// <param name="name">The DNS name to look up in the cache.</param>
        /// <param name="system">The actor system to use for resolution.</param>
        /// <returns>The cached DNS resolution, or null if not found.</returns>
        public static Resolved Cached(string name, ActorSystem system)
        {
            return Instance.Apply(system).Cache.Cached(name);
        }

        /// <summary>
        /// Attempts to resolve the DNS name, using the cache if possible, otherwise triggers a DNS query.
        /// </summary>
        /// <param name="name">The DNS name to resolve.</param>
        /// <param name="system">The actor system to use for resolution.</param>
        /// <param name="sender">The actor requesting the resolution.</param>
        /// <returns>The resolved DNS entry, or null if not cached.</returns>
        public static Resolved ResolveName(string name, ActorSystem system, IActorRef sender)
        {
            return Instance.Apply(system).Cache.Resolve(name, system, sender);
        }

        /// <inheritdoc/>
        public override DnsExt CreateExtension(ExtendedActorSystem system)
        {
            return new DnsExt(system);
        }
    }

    /// <summary>
    /// The Akka.IO DNS extension that manages DNS resolution settings, caching, and the DNS manager actor.
    /// </summary>
    public class DnsExt : IOExtension
    {
        /// <summary>
        /// Configuration settings for the DNS extension.
        /// </summary>
        public class DnsSettings
        {
            /// <summary>
            /// Creates a new <see cref="DnsSettings"/> instance from the provided configuration.
            /// </summary>
            /// <param name="config">The HOCON configuration for the DNS extension.</param>
            public DnsSettings(Config config)
            {
                if (config.IsNullOrEmpty())
                    throw ConfigurationException.NullOrEmptyConfig<DnsSettings>();

                Dispatcher = config.GetString("dispatcher", null);
                Resolver = config.GetString("resolver", null);
                ResolverConfig = config.GetConfig(Resolver);
                ProviderObjectName = ResolverConfig.GetString("provider-object", null);
            }

            /// <summary>
            /// The dispatcher to use for DNS resolution actors.
            /// </summary>
            public string Dispatcher { get; private set; }
            /// <summary>
            /// The name of the configured DNS resolver.
            /// </summary>
            public string Resolver { get; private set; }
            /// <summary>
            /// The HOCON configuration section for the selected resolver.
            /// </summary>
            public Config ResolverConfig { get; private set; }
            /// <summary>
            /// The fully qualified type name of the <see cref="IDnsProvider"/> implementation.
            /// </summary>
            public string ProviderObjectName { get; private set; }
        }
        
        private readonly ExtendedActorSystem _system;
        private IActorRef _manager;

        /// <summary>
        /// Initializes the DNS extension for the specified actor system, loading settings and creating the DNS provider.
        /// </summary>
        /// <param name="system">The actor system this extension is being created for.</param>
        public DnsExt(ExtendedActorSystem system)
        {
            _system = system;

            var config = system.Settings.Config.GetConfig("akka.io.dns");
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<DnsSettings>("akka.io.dns");

            Settings = new DnsSettings(config);
            //TODO: system.dynamicAccess.getClassFor[DnsProvider](Settings.ProviderObjectName).get.newInstance()
            Provider = (IDnsProvider) Activator.CreateInstance(Type.GetType(Settings.ProviderObjectName));
            Cache = Provider.Cache;
        }

        /// <inheritdoc/>
        public override IActorRef Manager
        {
            get
            {
                return _manager = _manager ?? _system.SystemActorOf(Props.Create(Provider.ManagerClass, this)
                                                                         .WithDeploy(Deploy.Local)
                                                                         .WithDispatcher(Settings.Dispatcher));
            }
        }

        /// <summary>
        /// Returns the DNS manager actor reference.
        /// </summary>
        /// <returns>The DNS manager <see cref="IActorRef"/>.</returns>
        public IActorRef GetResolver()
        {
            return _manager;
        }

        /// <summary>
        /// The DNS configuration settings.
        /// </summary>
        public DnsSettings Settings { get; private set; }
        /// <summary>
        /// The DNS cache used for storing and retrieving resolved DNS entries.
        /// </summary>
        public DnsBase Cache { get; private set; }
        /// <summary>
        /// The DNS provider that supplies the resolver actor and cache implementations.
        /// </summary>
        public IDnsProvider Provider { get; private set; }
    }
}
