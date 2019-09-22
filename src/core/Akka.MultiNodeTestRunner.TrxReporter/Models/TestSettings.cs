// -----------------------------------------------------------------------
//  <copyright file="TestSettings.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

namespace Akka.MultiNodeTestRunner.AzureDevOps.Models
{
    using System.Xml.Linq;

    public class TestSettings : ITestEntity
    {
        public TestSettings(string name)
        {
            Name = name;
        }

        public Identifier Id { get; } = Identifier.Create();
        public string Name { get; }

        public XElement Serialize() => XmlHelper.Elem("TestSettings",
            XmlHelper.Attr("id", Id),
            XmlHelper.Attr("name", Name)
        );
    }
}