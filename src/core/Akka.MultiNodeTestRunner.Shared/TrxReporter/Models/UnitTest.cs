//-----------------------------------------------------------------------
// <copyright file="UnitTest.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Xml.Linq;
using static Akka.MultiNodeTestRunner.TrxReporter.Models.XmlHelper;

namespace Akka.MultiNodeTestRunner.TrxReporter.Models
{
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

        public XElement Serialize() => Elem("UnitTest",
            Attr("id", Id),
            Attr("name", Name),
            Attr("storage", Storage),
            Elem("Execution",
                Attr("id", ExecutionId)
            ),
            Elem("TestMethod",
                Attr("codeBase", Storage),
                Attr("adapterTypeName", AdapterTypeName),
                Attr("className", ClassName),
                Attr("name", Name)
            )
        );
    }
}
