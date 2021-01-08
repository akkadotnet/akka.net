//-----------------------------------------------------------------------
// <copyright file="LeaseSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Configuration;

namespace Akka.Coordination
{
    /// <summary>
    /// The settings used for the <see cref="Lease"/>
    /// </summary>
    public sealed class LeaseSettings
    {
        /// <summary>
        /// Creates a new <see cref="LeaseSettings"/> instance.
        /// </summary>
        /// <param name="config">Lease config</param>
        /// <param name="leaseName">Lease name</param>
        /// <param name="ownerName">Lease owner</param>
        /// <returns>The requested settings.</returns>
        public static LeaseSettings Create(Config config, string leaseName, string ownerName)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<LeaseSettings>();

            return new LeaseSettings(leaseName, ownerName, TimeoutSettings.Create(config), config);
        }

        /// <summary>
        /// Lease name
        /// </summary>
        public string LeaseName { get; }

        /// <summary>
        /// Lease owner
        /// </summary>
        public string OwnerName { get; }

        /// <summary>
        /// Timeout settings
        /// </summary>
        public TimeoutSettings TimeoutSettings { get; }

        /// <summary>
        /// Lease config
        /// </summary>
        public Config LeaseConfig { get; }

        /// <summary>
        /// Lease implementation type
        /// </summary>
        public Type LeaseType { get; }

        /// <summary>
        /// Creates a new instance of the <see cref="LeaseSettings"/>.
        /// </summary>
        /// <param name="leaseName">Lease name</param>
        /// <param name="ownerName">Lease owner</param>
        /// <param name="timeoutSettings">Timeout settings</param>
        /// <param name="leaseConfig">Lease config</param>
        public LeaseSettings(string leaseName, string ownerName, TimeoutSettings timeoutSettings, Config leaseConfig)
        {
            LeaseName = leaseName;
            OwnerName = ownerName;
            TimeoutSettings = timeoutSettings;
            LeaseConfig = leaseConfig;

            var leaseClassName = leaseConfig.GetString("lease-class", null);
            if (string.IsNullOrEmpty(leaseClassName))
                throw new ArgumentException("lease-class must not be empty");

            LeaseType = Type.GetType(leaseClassName, true);
        }

        /// <summary>
        /// Create a <see cref="LeaseSettings"/> with specified <see cref="TimeoutSettings"/>.
        /// </summary>
        /// <param name="timeoutSettings">timeout settings</param>
        /// <returns></returns>
        public LeaseSettings WithTimeoutSettings(TimeoutSettings timeoutSettings)
        {
            return new LeaseSettings(LeaseName, OwnerName, timeoutSettings, LeaseConfig);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"LeaseSettings({ LeaseName }, { OwnerName }, { TimeoutSettings })";
        }
    }
}
