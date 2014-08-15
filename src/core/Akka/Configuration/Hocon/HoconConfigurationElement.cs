using System.Configuration;

namespace Akka.Configuration.Hocon
{
    public class HoconConfigurationElement : CDataConfigurationElement
    {
        [ConfigurationProperty(ContentPropertyName, IsRequired = true, IsKey = true)]
        public string Content
        {
            get { return (string) base[ContentPropertyName]; }
            set { base[ContentPropertyName] = value; }
        }
    }
}