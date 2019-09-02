// -----------------------------------------------------------------------
//  <copyright file="UnitTest.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

namespace Akka.MultiNodeTestRunner.AzureDevOps.Models
{
    using System.Collections.Generic;
    using System.Xml.Linq;

    public class UnitTest : ITestEntity
    {
        public UnitTest(string className, string name, Identifier testListId, string storage)
        {
            ClassName = className;
            Name = name;
            TestListId = testListId;
            Storage = storage;
        }

        public Identifier Id { get; } = Identifier.Create();
        public Identifier TestListId { get; }
        public Identifier ExecutionId { get; } = Identifier.Create();
        
        public string Name { get; }
        public string Storage { get; }
        public string AdapterTypeName => "executor://xunit/MulitNodeTestRunner";
        public string ClassName { get; }

        public List<UnitTestResult> Results = new List<UnitTestResult>();

        public UnitTestResult AddResult(string name, string computerName)
        {
            var result = new UnitTestResult(Id, ExecutionId, TestListId, name, computerName);
            Results.Add(result);
            return result;
        }

        public XElement Serialize() => XmlHelper.Elem("UnitTest",
            XmlHelper.Attr("id", Id),
            XmlHelper.Attr("name", Name),
            XmlHelper.Attr("storage", Storage),
            XmlHelper.Elem("Execution",
                XmlHelper.Attr("id", ExecutionId)
            ),
            XmlHelper.Elem("TestMethod",
                XmlHelper.Attr("codeBase", Storage),
                XmlHelper.Attr("adapterTypeName", AdapterTypeName),
                XmlHelper.Attr("className", ClassName),
                XmlHelper.Attr("name", Name)
            )
        );
    }
}