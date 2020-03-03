//-----------------------------------------------------------------------
// <copyright file="ConfigurationFactory.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Hocon; using Akka.Configuration;
using ConfigurationException = Hocon.ConfigurationException;

namespace Akka.Configuration
{
    /// <summary>
    ///     This class contains methods used to retrieve configuration information
    ///     from a variety of sources including user-supplied strings, configuration
    ///     files and assembly resources.
    /// </summary>
    public static class ConfigurationFactory
    {
        private static readonly Config DefaultConfig = FromResource("Akka.Configuration.Pigeon.conf");
        private static readonly string[] DefaultHoconFilePaths = { "app.conf", "app.hocon" };

        /// <summary>
        ///     Generates an empty configuration.
        /// </summary>
        public static Config Empty => Config.Empty;

        /// <summary>
        ///     Generates a configuration defined in the supplied
        ///     HOCON (Human-Optimized Config Object Notation) string.
        /// </summary>
        /// <param name="hocon">A string that contains configuration options to use.</param>
        /// <param name="includeCallback">callback used to resolve includes</param>
        /// <returns>The configuration defined in the supplied HOCON string.</returns>
        public static Config ParseString(string hocon, HoconIncludeCallbackAsync includeCallback)
            => new Config(HoconParser.Parse(hocon, includeCallback));

        /// <summary>
        ///     Generates a configuration defined in the supplied
        ///     HOCON (Human-Optimized Config Object Notation) string.
        /// </summary>
        /// <param name="hocon">A string that contains configuration options to use.</param>
        /// <returns>The configuration defined in the supplied HOCON string.</returns>
        public static Config ParseString(string hocon)
            => ParseString(hocon, null);

        /// <summary>
        ///     Loads a configuration named "akka" defined in the current application's
        ///     configuration file, e.g. app.config or web.config.
        /// </summary>
        /// <returns>
        ///     The configuration defined in the configuration file. If the section
        ///     "akka" is not found, this returns an empty Config.
        /// </returns>
        public static Config Load()
        {
            return HoconConfigurationFactory.Default();
        }

        /// <summary>
        ///     Loads a configuration with the given `sectionName` defined in the current application's
        ///     configuration file, e.g. app.config or web.config.
        /// </summary>
        /// <param name="sectionName">The name of the section to load.</param>
        /// <returns>
        ///     The configuration defined in the configuration file. If the section
        ///     is not found, this returns an empty Config.
        /// </returns>
        public static Config Load(string sectionName)
        {
            var section = (HoconConfigurationSection)ConfigurationManager.GetSection(sectionName) ??
                          new HoconConfigurationSection();
            var config = section.Config;

            return config;
        }

        /// <summary>
        ///     Parses a HOCON file from the filesystem.
        /// </summary>
        /// <param name="filePath">The path to the file.</param>
        /// <returns>A parsed HOCON configuration object.</returns>
        /// <throws>ConfigurationException, when the supplied filePath can't be found.</throws>
        public static Config FromFile(string filePath)
        {
            if (File.Exists(filePath)) return ParseString(File.ReadAllText(filePath));

            throw new ConfigurationException($"No HOCON file at {filePath} could be found.");
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

        /// <summary>
        ///     Retrieves a configuration defined in a resource of the
        ///     current executing assembly.
        /// </summary>
        /// <param name="resourceName">The name of the resource that contains the configuration.</param>
        /// <returns>The configuration defined in the current executing assembly.</returns>
        internal static Config FromResource(string resourceName)
        {
            var assembly = typeof(ConfigurationFactory).GetTypeInfo().Assembly;
            return FromResource(resourceName, assembly);
        }

        /// <summary>
        ///     Retrieves a configuration defined in a resource of the
        ///     assembly containing the supplied instance object.
        /// </summary>
        /// <param name="resourceName">The name of the resource that contains the configuration.</param>
        /// <param name="instanceInAssembly">An instance object located in the assembly to search.</param>
        /// <returns>The configuration defined in the assembly that contains the instanced object.</returns>
        public static Config FromResource(string resourceName, object instanceInAssembly)
        {
            var type = instanceInAssembly as Type;
            if (type != null)
                return FromResource(resourceName, type.GetTypeInfo().Assembly);
            var assembly = instanceInAssembly as Assembly;
            if (assembly != null)
                return FromResource(resourceName, assembly);
            return FromResource(resourceName, instanceInAssembly.GetType().GetTypeInfo().Assembly);
        }

        /// <summary>
        ///     Retrieves a configuration defined in a resource of the assembly
        ///     containing the supplied type <typeparamref name="TAssembly" />.
        /// </summary>
        /// <typeparam name="TAssembly">A type located in the assembly to search.</typeparam>
        /// <param name="resourceName">The name of the resource that contains the configuration.</param>
        /// <returns>The configuration defined in the assembly that contains the type <typeparamref name="TAssembly" />.</returns>
        public static Config FromResource<TAssembly>(string resourceName)
        {
            return FromResource(resourceName, typeof(TAssembly).GetTypeInfo().Assembly);
        }

        /// <summary>
        ///     Retrieves a configuration defined in a resource of the supplied assembly.
        /// </summary>
        /// <param name="resourceName">The name of the resource that contains the configuration.</param>
        /// <param name="assembly">The assembly that contains the given resource.</param>
        /// <returns>The configuration defined in the assembly that contains the given resource.</returns>
        public static Config FromResource(string resourceName, Assembly assembly)
        {
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                Debug.Assert(stream != null, "stream != null");
                using (var reader = new StreamReader(stream))
                {
                    var result = reader.ReadToEnd();
                    return ParseString(result);
                }
            }
        }
    }
}
