//-----------------------------------------------------------------------
// <copyright file="TestRun.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using static Akka.MultiNodeTestRunner.TrxReporter.Models.XmlHelper;

namespace Akka.MultiNodeTestRunner.TrxReporter.Models
{
    public class TestRun : ITestEntity
    {
        public TestRun(string name)
        {
            Name = name;
        }

        public Identifier Id { get; } = Identifier.Create();
        public string Name { get; }
        public string RunUser { get; set; }
        public Times Times { get; } = new Times();
        public TestList TestList { get; } = new TestList("Results Not in a List");
        public IList<UnitTest> UnitTests { get; } = new List<UnitTest>();
        public Output Output { get; set; }

        public void Log(string item)
        {
            if (Output == null)
            {
                Output = new Output();
            }

            Output.DebugTrace.Add(item);
            Output.StdOut.Add(item);
        }

        public UnitTest AddUnitTest(string className, string name, string storage)
        {
            var unitTest = new UnitTest(className, name, TestList.Id, storage);
            UnitTests.Add(unitTest);

            return unitTest;
        }

        public XElement Serialize()
        {
            return Elem("TestRun",
                Attr("id", Id),
                Attr("name", Name),
                Attr("runUser", RunUser),
                Times,
                // TestSettings
                ElemList("Results", UnitTests.SelectMany(x => x.Results)),
                ElemList("TestDefinitions", UnitTests),
                ElemList("TestEntries", UnitTests.Select(x => new TestEntry(x.Id, x.ExecutionId, x.TestListId))),
                Elem("TestLists",
                    TestList
                ),
                new ResultSummary(UnitTests, Output)
            );
        }
    }
}
