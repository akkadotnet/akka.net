//-----------------------------------------------------------------------
// <copyright file="HoconConfigurationElement.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#if CONFIGURATION
using System.Configuration;

namespace Akka.Configuration.Hocon
{
    /// <summary>
    /// This class represents a custom HOCON (Human-Optimized Config Object Notation)
    /// node within a configuration file.
    /// <code>
    /// <![CDATA[
    /// <?xml version="1.0" encoding="utf-8" ?>
    /// <configuration>
    ///   <configSections>
    ///     <section name="akka" type="Akka.Configuration.Hocon.AkkaConfigurationSection, Akka" />
    ///   </configSections>
    ///   <akka>
    ///     <hocon>
    ///     ...
    ///     </hocon>
    ///   </akka>
    /// </configuration>
    /// ]]>
    /// </code>
    /// </summary>
    public class HoconConfigurationElement : CDataConfigurationElement
    {
        /// <summary>
        /// Gets or sets the HOCON configuration string contained in the hocon node.
        /// </summary>
        [ConfigurationProperty(ContentPropertyName, IsRequired = true, IsKey = true)]
        public string Content
        {
            get { return (string) base[ContentPropertyName]; }
            set { base[ContentPropertyName] = value; }
        }
    }
}
#endif
