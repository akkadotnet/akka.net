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
            get { return (HoconConfigurationElement)base[ConfigurationPropertyName]; }
            set { base[ConfigurationPropertyName] = value; }
        }
    }
}