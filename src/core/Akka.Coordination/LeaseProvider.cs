//-----------------------------------------------------------------------
// <copyright file="LeaseProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;

namespace Akka.Coordination
{
    /// <summary>
    /// Lease extension for distributed lock
    /// </summary>
    public class LeaseProviderExtensionProvider : ExtensionIdProvider<LeaseProvider>
    {
        /// <summary>
        /// Creates the lease extension using a given actor system.
        /// </summary>
        /// <param name="system">The actor system to use when creating the extension.</param>
        /// <returns>The extension created using the given actor system.</returns>
        public override LeaseProvider CreateExtension(ExtendedActorSystem system)
        {
            var extension = new LeaseProvider(system);
            return extension;
        }
    }

    /// <summary>
    /// This class represents an <see cref="ActorSystem"/> extension used for distributed lock within the actor system.
    /// </summary>
    public class LeaseProvider : IExtension
    {
        private class LeaseKey : IEquatable<LeaseKey>
        {
            public string LeaseName { get; }
            public string ConfigPath { get; }
            public string ClientName { get; }

            public LeaseKey(string leaseName, string configPath, string clientName)
            {
                LeaseName = leaseName;
                ConfigPath = configPath;
                ClientName = clientName;
            }

            public bool Equals(LeaseKey other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(this, other)) return true;

                return Equals(LeaseName, other.LeaseName) && Equals(ConfigPath, other.ConfigPath) && Equals(ClientName, other.ClientName);
            }

            public override bool Equals(object obj) => obj is LeaseKey lk && Equals(lk);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = LeaseName.GetHashCode();
                    hashCode = (hashCode * 397) ^ ConfigPath.GetHashCode();
                    hashCode = (hashCode * 397) ^ ClientName.GetHashCode();
                    return hashCode;
                }
            }

            public override string ToString() => $"LeaseKey({LeaseName}, {ConfigPath}, {ClientName})";
        }

        /// <summary>
        /// Retrieves the extension from the specified actor system.
        /// </summary>
        /// <param name="system">The actor system from which to retrieve the extension.</param>
        /// <returns>The extension retrieved from the given actor system.</returns>
        public static LeaseProvider Get(ActorSystem system)
        {
            return system.WithExtension<LeaseProvider, LeaseProviderExtensionProvider>();
        }

        private readonly ExtendedActorSystem _system;
        private readonly ConcurrentDictionary<LeaseKey, Lease> leases = new ConcurrentDictionary<LeaseKey, Lease>();

        private ILoggingAdapter _log;

        private ILoggingAdapter Log { get { return _log ?? (_log = Logging.GetLogger(_system, "LeaseProvider")); } }

        /// <summary>
        /// Initializes a new instance of the <see cref="LeaseProvider"/> class.
        /// </summary>
        /// <param name="system">The actor system that hosts the lease.</param>
        public LeaseProvider(ExtendedActorSystem system)
        {
            _system = system;
            _system.Settings.InjectTopLevelFallback(DefaultConfig());
        }

        /// <summary>
        /// Retrieves the default lease options that Akka.NET uses when no configuration has been defined.
        /// </summary>
        /// <returns>The configuration that contains default values for all lease options.</returns>
        public static Config DefaultConfig()
        {
            return ConfigurationFactory.FromResource<LeaseProvider>("Akka.Coordination.reference.conf");
        }

        /// <summary>
        /// The configuration define at <paramref name="configPath"/> must have a property `lease-class` that defines
        /// the fully qualified class name of the <see cref="Lease"/> implementation.
        /// The class must implement <see cref="Lease"/> and have constructor with <see cref="LeaseSettings"/> parameter and
        /// optionally <see cref="ActorSystem"/> parameter.
        /// </summary>
        /// <param name="leaseName">the name of the lease resource</param>
        /// <param name="configPath">the path of configuration for the lease</param>
        /// <param name="ownerName">the owner that will `acquire` the lease, e.g. hostname and port of the ActorSystem</param>
        /// <returns></returns>
        public Lease GetLease(string leaseName, string configPath, string ownerName)
        {
            var leaseKey = new LeaseKey(leaseName, configPath, ownerName);

            return leases.GetOrAdd(leaseKey, lk =>
            {
                var leaseConfig = _system.Settings.Config
                    .GetConfig(configPath)
                    .WithFallback(_system.Settings.Config.GetConfig("akka.coordination.lease"));

                var settings = LeaseSettings.Create(leaseConfig, leaseName, ownerName);

                try
                {
                    try
                    {
                        return (Lease)Activator.CreateInstance(settings.LeaseType, settings, _system);
                    }
                    catch
                    {
                        return (Lease)Activator.CreateInstance(settings.LeaseType, settings);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(
                      ex,
                      "Invalid lease configuration for leaseName [{0}], configPath [{1}] lease-class [{2}]. " +
                      "The class must implement scaladsl.Lease or javadsl.Lease and have constructor with LeaseSettings parameter and " +
                      "optionally ActorSystem parameter.",
                      settings.LeaseName,
                      configPath,
                      settings.LeaseType);

                    throw;
                }
            });
        }
    }
}
