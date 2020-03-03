//-----------------------------------------------------------------------
// <copyright file="AkkaConfigurationSection.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#if CONFIGURATION
using System.Configuration;
using Hocon; using Akka.Configuration;

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
    public class AkkaConfigurationSection : HoconConfigurationSection
    {
        /// <summary>
        /// Retrieves a <see cref="Config"/> from the contents of the
        /// custom akka node within a configuration file.
        /// </summary>
        public Config AkkaConfig
        {
            get { return base.Config; }
        }

    }
}
#endif
