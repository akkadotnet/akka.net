//-----------------------------------------------------------------------
// <copyright file="Settings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Configuration;

namespace Akka.Persistence.Custom
{
    /// <summary>
    /// Configuration settings representation targeting SQLite journal actor.
    /// </summary>
    public class JournalSettings
    {
        /// <summary>
        /// Connection string used to access a SQLite instance.
        /// </summary>
        public string ConnectionString { get; }

        /// <summary>
        /// Connection timeout for SQLite related operations.
        /// </summary>
        public TimeSpan ConnectionTimeout { get; }
        
        /// <summary>
        /// Flag determining in in case of event journal or metadata table missing, they should be automatically initialized.
        /// </summary>
        public bool AutoInitialize { get; }

        /// <summary>
        /// The default serializer being used if no type match override is specified
        /// </summary>
        public string DefaultSerializer { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="JournalSettings"/> class.
        /// </summary>
        /// <param name="config">The configuration used to configure the settings.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="config"/> is undefined.
        /// </exception>
        public JournalSettings(Config config)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<JournalSettings>();

            ConnectionString = config.GetString("connection-string");
            ConnectionTimeout = config.GetTimeSpan("connection-timeout");
            AutoInitialize = config.GetBoolean("auto-initialize");
            DefaultSerializer = config.GetString("serializer", null);
        }
    }

    /// <summary>
    /// Configuration settings representation targeting Sql Server snapshot store actor.
    /// </summary>
    public class SnapshotStoreSettings
    {
        /// <summary>
        /// Connection string used to access a persistent SQL Server instance.
        /// </summary>
        public string ConnectionString { get; }

        /// <summary>
        /// Connection timeout for SQL Server related operations.
        /// </summary>
        public TimeSpan ConnectionTimeout { get; }

        /// <summary>
        /// Flag determining in in case of snapshot store table missing, they should be automatically initialized.
        /// </summary>
        public bool AutoInitialize { get; }

        /// <summary>
        /// The default serializer being used if no type match override is specified
        /// </summary>
        public string DefaultSerializer { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SnapshotStoreSettings"/> class.
        /// </summary>
        /// <param name="config">The configuration used to configure the settings.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="config"/> is undefined.
        /// </exception>
        public SnapshotStoreSettings(Config config)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<SnapshotStoreSettings>();

            ConnectionString = config.GetString("connection-string");
            ConnectionTimeout = config.GetTimeSpan("connection-timeout");
            AutoInitialize = config.GetBoolean("auto-initialize");
            DefaultSerializer = config.GetString("serializer", null);
        }
    }
}
