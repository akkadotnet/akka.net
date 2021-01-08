//-----------------------------------------------------------------------
// <copyright file="AkkaConfigurationSection.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#if CONFIGURATION
using System.Configuration;

namespace Akka.Configuration.Hocon
{
    /// <summary>
    /// This class represents a custom akka node within a configuration file.
    /// <code>
    /// <![CDATA[
    /// <?xml version="1.0" encoding="utf-8" ?>
    /// <configuration>
    ///   <configSections>
    ///     <section name="akka" type="Akka.Configuration.Hocon.AkkaConfigurationSection, Akka" />
    ///   </configSections>
    ///   <akka>
    ///   ...
    ///   </akka>
    /// </configuration>
    /// ]]>
    /// </code>
    /// </summary>
    public class AkkaConfigurationSection : ConfigurationSection
    {
        private const string ConfigurationPropertyName = "hocon";
        private Config _akkaConfig;

        /// <summary>
        /// Retrieves a <see cref="Config"/> from the contents of the
        /// custom akka node within a configuration file.
        /// </summary>
        public Config AkkaConfig
        {
            get { return _akkaConfig ?? (_akkaConfig = ConfigurationFactory.ParseString(Hocon.Content)); }
        }

        /// <summary>
        /// Retrieves the HOCON (Human-Optimized Config Object Notation)
        /// configuration string from the custom akka node.
        /// <code>
        /// <![CDATA[
        /// <?xml version="1.0" encoding="utf-8" ?>
        /// <configuration>
        ///   <configSections>
        ///     <section name="akka" type="Akka.Configuration.Hocon.AkkaConfigurationSection, Akka" />
        ///   </configSections>
        ///   <akka>
        ///      <hocon>
        ///      ...
        ///      </hocon>
        ///   </akka>
        /// </configuration>
        /// ]]>
        /// </code>
        /// </summary>
        [ConfigurationProperty(ConfigurationPropertyName, IsRequired = true)]
        public HoconConfigurationElement Hocon
        {
            get { return (HoconConfigurationElement) base[ConfigurationPropertyName]; }
            set { base[ConfigurationPropertyName] = value; }
        }
    }
}
#endif
