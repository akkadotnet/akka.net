//-----------------------------------------------------------------------
// <copyright file="CDataConfigurationElement.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#if CONFIGURATION
using System.Configuration;
using System.Xml;

namespace Akka.Configuration.Hocon
{
    /// <summary>
    /// This class represents the base implementation for retrieving text from
    /// an XML CDATA node within a configuration file.
    /// <code>
    /// <![CDATA[
    /// <?xml version="1.0" encoding="utf-8" ?>
    /// <configuration>
    ///   <configSections>
    ///     <section name="akka" type="Akka.Configuration.Hocon.AkkaConfigurationSection, Akka" />
    ///   </configSections>
    ///   <akka>
    ///     <hocon>
    ///       &lt;![CDATA[
    ///       ...
    ///       ]]&gt;
    ///     </hocon>
    ///   </akka>
    /// </configuration>
    /// ]]>
    /// </code>
    /// </summary>
    public abstract class CDataConfigurationElement : ConfigurationElement
    {
        /// <summary>
        /// The name of the property that contains the content to deserialize.
        /// </summary>
        protected const string ContentPropertyName = "content";

        /// <summary>
        /// Deserializes the text located in a CDATA node of the configuration file.
        /// </summary>
        /// <param name="reader">The <see cref="System.Xml.XmlReader" /> that reads from the configuration file.</param>
        /// <param name="serializeCollectionKey"><c>true</c> to serialize only the collection key properties; otherwise, <c>false</c>.</param>
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
#endif
