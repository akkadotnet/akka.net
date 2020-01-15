//-----------------------------------------------------------------------
// <copyright file="TestSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Xml.Linq;
using static Akka.MultiNodeTestRunner.TrxReporter.Models.XmlHelper;

namespace Akka.MultiNodeTestRunner.TrxReporter.Models
{
    public class TestSettings : ITestEntity
    {
        public TestSettings(string name)
        {
            Name = name;
        }

        public Identifier Id { get; } = Identifier.Create();
        public string Name { get; }

        public XElement Serialize() => Elem("TestSettings",
            Attr("id", Id),
            Attr("name", Name)
        );
    }
}
