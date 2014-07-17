using System.Configuration;
using System.Xml;

namespace Akka.Configuration.Hocon
{
    public abstract class CDataConfigurationElement : ConfigurationElement
    {
        protected const string ContentPropertyName = "content";

        protected override void DeserializeElement(XmlReader reader, bool serializeCollectionKey)
        {
            foreach (ConfigurationProperty configurationProperty in Properties)
            {
                var name = configurationProperty.Name;
                if (name == ContentPropertyName)
                {
                    var contentString = reader.ReadString();
                    base[name] = contentString.Trim();
                }
                else
                {
                    var attributeValue = reader.GetAttribute(name);
                    base[name] = attributeValue;
                }
            }
            reader.ReadEndElement();
        }
    }
}