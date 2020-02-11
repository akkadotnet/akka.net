//-----------------------------------------------------------------------
// <copyright file="AkkaConfigurationFactory.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Hocon;
using Akka.Configuration.Hocon;

namespace Akka.Configuration
{
    /// <summary>
    ///     This class contains methods used to retrieve configuration information
    ///     from a variety of sources including user-supplied strings, configuration
    ///     files and assembly resources.
    /// </summary>
    public static class AkkaConfigurationFactory
    {
        public static readonly Config DefaultConfig = ConfigurationFactory.FromResource<AkkaAsemblyMarker>("Akka.Configuration.Pigeon.conf");

        public static readonly string[] DefaultHoconFilePaths = { "app.conf", "app.hocon" };

        public static Config Load()
        {
            // attempt to load .hocon files first
            foreach (var path in DefaultHoconFilePaths.Where(x => File.Exists(x)))
                return ConfigurationFactory.FromFile(path);

            // if we made it this far: no default HOCON files found. Check app.config
            var def = ConfigurationFactory.Load("hocon"); // new default
            if (!def.IsNullOrEmpty())
                return def;

            def = ConfigurationFactory.Load("akka"); // old Akka.NET-specific default
            if (!def.IsNullOrEmpty())
                return def;

            return Config.Empty;
        }

        /// <summary>
        ///     Retrieves the default configuration that Akka.NET uses
        ///     when no configuration has been defined.
        /// </summary>
        /// <returns>The configuration that contains default values for all options.</returns>
        public static Config Default()
        {
            return DefaultConfig;
        }

    }
}
