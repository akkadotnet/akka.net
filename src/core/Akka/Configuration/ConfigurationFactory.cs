//-----------------------------------------------------------------------
// <copyright file="ConfigurationFactory.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
#if !DNXCORE50
using System.Configuration;
#endif
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Akka.Configuration.Hocon;

namespace Akka.Configuration
{
    /// <summary>
    /// This class contains methods used to retrieve configuration information
    /// from a variety of sources including user-supplied strings, configuration
    /// files and assembly resources.
    /// </summary>
    public class ConfigurationFactory
    {
        /// <summary>
        /// Generates an empty configuration.
        /// </summary>
        public static Config Empty
        {
            get { return ParseString(""); }
        }

        /// <summary>
        /// Generates a configuration defined in the supplied
        /// HOCON (Human-Optimized Config Object Notation) string.
        /// </summary>
        /// <param name="hocon">A string that contains configuration options to use.</param>
        /// <returns>The configuration defined in the supplied HOCON string.</returns>
        public static Config ParseString(string hocon)
        {
            HoconRoot res = Parser.Parse(hocon);
            return new Config(res);
        }

#if !DNXCORE50
        /// <summary>
        /// Loads a configuration defined in the current application's
        /// configuration file, e.g. app.config or web.config
        /// </summary>
        /// <returns>The configuration defined in the configuration file.</returns>
        public static Config Load()
        {
            var section = (AkkaConfigurationSection)ConfigurationManager.GetSection("akka") ?? new AkkaConfigurationSection();
            var config = section.AkkaConfig;

            return config;
        }
#endif

        /// <summary>
        /// Retrieves the default configuration that Akka.NET uses
        /// when no configuration has been defined.
        /// </summary>
        /// <returns>The configuration that contains default values for all options.</returns>
        public static Config Default()
        {
            return FromResource("Akka.Configuration.Pigeon.conf");
        }

        /// <summary>
        /// Retrieves a configuration defined in a resource of the
        /// current executing assembly.
        /// </summary>
        /// <param name="resourceName">The name of the resource that contains the configuration.</param>
        /// <returns>The configuration defined in the current executing assembly.</returns>
        internal static Config FromResource(string resourceName)
        {
#if !DNXCORE50
            Assembly assembly = Assembly.GetExecutingAssembly();
#else
            Assembly assembly = typeof(ConfigurationFactory).GetTypeInfo().Assembly;
#endif

            return FromResource(resourceName, assembly);
        }

        /// <summary>
        /// Retrieves a configuration defined in a resource of the
        /// assembly containing the supplied instance object.
        /// </summary>
        /// <param name="resourceName">The name of the resource that contains the configuration.</param>
        /// <param name="instanceInAssembly">An instance object located in the assembly to search.</param>
        /// <returns>The configuration defined in the assembly that contains the instanced object.</returns>
        public static Config FromResource(string resourceName, object instanceInAssembly)
        {
            var type = instanceInAssembly as Type;
            if(type != null)
                return FromResource(resourceName, type.GetTypeInfo().Assembly);
            var assembly = instanceInAssembly as Assembly;
            if(assembly != null)
                return FromResource(resourceName, assembly);
            return FromResource(resourceName, instanceInAssembly.GetType().GetTypeInfo().Assembly);
        }

        /// <summary>
        /// Retrieves a configuration defined in a resource of the assembly
        /// containing the supplied type <typeparamref name="TAssembly"/>.
        /// </summary>
        /// <typeparam name="TAssembly">A type located in the assembly to search.</typeparam>
        /// <param name="resourceName">The name of the resource that contains the configuration.</param>
        /// <returns>The configuration defined in the assembly that contains the type <typeparamref name="TAssembly"/>.</returns>
        public static Config FromResource<TAssembly>(string resourceName)
        {
            return FromResource(resourceName, typeof(TAssembly).GetTypeInfo().Assembly);
        }

        /// <summary>
        /// Retrieves a configuration defined in a resource of the supplied assembly.
        /// </summary>
        /// <param name="resourceName">The name of the resource that contains the configuration.</param>
        /// <param name="assembly">The assembly that contains the given resource.</param>
        /// <returns>The configuration defined in the assembly that contains the given resource.</returns>
        public static Config FromResource(string resourceName, Assembly assembly)
        {
            using(Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                Debug.Assert(stream != null, "stream != null");
                using (var reader = new StreamReader(stream))
                {
                    string result = reader.ReadToEnd();

                    return ParseString(result);
                }
            }
        }
    }
}
