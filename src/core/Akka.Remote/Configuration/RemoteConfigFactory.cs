﻿//-----------------------------------------------------------------------
// <copyright file="RemoteConfigFactory.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Diagnostics;
using System.IO;
using Akka.Configuration;

namespace Akka.Remote.Configuration
{
    /// <summary>
    /// Internal class used for loading remote configuration values
    /// </summary>
    internal static class RemoteConfigFactory
    {
        /// <summary>
        /// Defaults this instance.
        /// </summary>
        /// <returns>Config.</returns>
        public static Config Default()
        {
            return FromResource("Akka.Remote.Configuration.Remote.conf");
        }

        /// <summary>
        /// Froms the resource.
        /// </summary>
        /// <param name="resourceName">Name of the resource.</param>
        /// <returns>Config.</returns>
        internal static Config FromResource(string resourceName)
        {
            var assembly = typeof (RemoteConfigFactory).Assembly;

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

