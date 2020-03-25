//-----------------------------------------------------------------------
// <copyright file="LeaseUsageSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Coordination
{
    /// <summary>
    /// Lease settings for use in singleton and sharding
    /// </summary>
    public sealed class LeaseUsageSettings
    {
        /// <summary>
        /// Config path of the lease to be taken
        /// </summary>
        public string LeaseImplementation { get; }

        /// <summary>
        /// The interval between retries for acquiring the lease
        /// </summary>
        public TimeSpan LeaseRetryInterval { get; }

        /// <summary>
        /// Creates a new <see cref="LeaseUsageSettings"/> instance.
        /// </summary>
        /// <param name="leaseImplementation">TConfig path of the lease to be taken</param>
        /// <param name="leaseRetryInterval">The interval between retries for acquiring the lease</param>
        public LeaseUsageSettings(string leaseImplementation, TimeSpan leaseRetryInterval)
        {
            LeaseImplementation = leaseImplementation;
            LeaseRetryInterval = leaseRetryInterval;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"LeaseUsageSettings({ LeaseImplementation }, { LeaseRetryInterval })";
        }
    }
}
