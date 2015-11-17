//-----------------------------------------------------------------------
// <copyright file="ClusterConfigFactory.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Diagnostics;
using System.IO;
using Akka.Configuration;

namespace Akka.Cluster.Configuration
{
    /// <summary>
    /// This class contains methods used to retrieve cluster configuration options from this assembly's resources.
    ///
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    internal static class ClusterConfigFactory
    {
        /// <summary>
        /// Retrieves the default cluster options that Akka.NET uses when no configuration has been defined.
        /// </summary>
        /// <returns>The configuration that contains default values for all cluster options.</returns>
        public static Config Default()
        {
            return FromResource("Akka.Cluster.Configuration.Cluster.conf");
        }

        /// <summary>
        /// Retrieves a configuration defined in a resource of the current executing assembly.
        /// </summary>
        /// <param name="resourceName">The name of the resource that contains the configuration.</param>
        /// <returns>The configuration defined in the current executing assembly.</returns>
        internal static Config FromResource(string resourceName)
        {
            var assembly = typeof(ClusterConfigFactory).Assembly;

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                Debug.Assert(stream != null, "stream != null");
                using (var reader = new StreamReader(stream))
                {
                    var result = reader.ReadToEnd();

                    return ConfigurationFactory.ParseString(result);
                }
            }
        }
    }
}
