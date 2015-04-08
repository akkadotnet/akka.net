//-----------------------------------------------------------------------
// <copyright file="ConfigurationFactory.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Akka.Configuration.Hocon;

namespace Akka.Configuration
{
    /// <summary>
    ///     Class ConfigurationFactory.
    /// </summary>
    public class ConfigurationFactory
    {
        /// <summary>
        ///     Gets the empty.
        /// </summary>
        /// <value>The empty.</value>
        public static Config Empty
        {
            get { return ParseString(""); }
        }

        /// <summary>
        ///     Parses the string.
        /// </summary>
        /// <param name="hocon">The json.</param>
        /// <returns>Config.</returns>
        public static Config ParseString(string hocon)
        {
            HoconRoot res = Parser.Parse(hocon);
            return new Config(res);
        }

        /// <summary>
        ///     Loads this instance.
        /// </summary>
        /// <returns>Config.</returns>
        public static Config Load()
        {
            var section = (AkkaConfigurationSection)ConfigurationManager.GetSection("akka") ?? new AkkaConfigurationSection();
            var config = section.AkkaConfig;

            return config;
        }

        /// <summary>
        ///     Defaults this instance.
        /// </summary>
        /// <returns>Config.</returns>
        public static Config Default()
        {
            return FromResource("Akka.Configuration.Pigeon.conf");
        }

        /// <summary>
        ///     Froms the resource.
        /// </summary>
        /// <param name="resourceName">Name of the resource.</param>
        /// <returns>Config.</returns>
        internal static Config FromResource(string resourceName)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();

            return FromResource(resourceName, assembly);
        }

        public static Config FromResource(string resourceName, object instanceInAssembly)
        {
            var type = instanceInAssembly as Type;
            if(type != null)
                return FromResource(resourceName, type.Assembly);
            var assembly = instanceInAssembly as Assembly;
            if(assembly != null)
                return FromResource(resourceName, assembly);
            return FromResource(resourceName, instanceInAssembly.GetType().Assembly);
        }

        public static Config FromResource<TypeInAssembly>(string resourceName)
        {
            return FromResource(resourceName, typeof(TypeInAssembly).Assembly);
        }

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

