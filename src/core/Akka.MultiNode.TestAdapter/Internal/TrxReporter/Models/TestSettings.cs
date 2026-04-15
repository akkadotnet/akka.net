// -----------------------------------------------------------------------
//  <copyright file="TestSettings.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Xml.Linq;
using static Akka.MultiNode.TestAdapter.Internal.TrxReporter.Models.XmlHelper;

namespace Akka.MultiNode.TestAdapter.Internal.TrxReporter.Models
{
    internal class TestSettings : ITestEntity
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