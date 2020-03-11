//-----------------------------------------------------------------------
// <copyright file="RemoteConfigFactory.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Diagnostics;
using System.IO;
using System.Reflection;
using Akka.Configuration;

namespace Akka.Remote.Configuration
{
    /// <summary>
    /// This class contains methods used to retrieve remote configuration options from this assembly's resources.
    ///
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    internal static class RemoteConfigFactory
    {
        /// <summary>
        /// Retrieves the default remote options that Akka.NET uses when no configuration has been defined.
        /// </summary>
        /// <returns>The configuration that contains default values for all remote options.</returns>
        public static Config Default()
        {
            return FromResource("Akka.Remote.Configuration.Remote.conf");
        }

        /// <summary>
        /// Retrieves a configuration defined in a resource of the current executing assembly.
        /// </summary>
        /// <param name="resourceName">The name of the resource that contains the configuration.</param>
        /// <returns>The configuration defined in the current executing assembly.</returns>
        internal static Config FromResource(string resourceName)
        {
            var assembly = typeof(RemoteConfigFactory).GetTypeInfo().Assembly;

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
