//-----------------------------------------------------------------------
// <copyright file="HoconConfigurationElement.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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

