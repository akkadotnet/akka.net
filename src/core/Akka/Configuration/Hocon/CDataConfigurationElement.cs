//-----------------------------------------------------------------------
// <copyright file="CDataConfigurationElement.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
                string name = configurationProperty.Name;
                if (name == ContentPropertyName)
                {
                    string contentString = reader.ReadString();
                    base[name] = contentString.Trim();
                }
                else
                {
                    string attributeValue = reader.GetAttribute(name);
                    base[name] = attributeValue;
                }
            }
            reader.ReadEndElement();
        }
    }
}

