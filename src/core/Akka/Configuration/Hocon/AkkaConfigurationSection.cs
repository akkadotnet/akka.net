//-----------------------------------------------------------------------
// <copyright file="AkkaConfigurationSection.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Configuration;

namespace Akka.Configuration.Hocon
{
    public class AkkaConfigurationSection : ConfigurationSection
    {
        private const string ConfigurationPropertyName = "hocon";
        private Config _akkaConfig;

        public Config AkkaConfig
        {
            get { return _akkaConfig ?? (_akkaConfig = ConfigurationFactory.ParseString(Hocon.Content)); }
        }

        [ConfigurationProperty(ConfigurationPropertyName, IsRequired = true)]
        public HoconConfigurationElement Hocon
        {
            get { return (HoconConfigurationElement) base[ConfigurationPropertyName]; }
            set { base[ConfigurationPropertyName] = value; }
        }
    }
}
