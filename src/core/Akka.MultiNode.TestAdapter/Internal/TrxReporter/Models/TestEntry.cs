// -----------------------------------------------------------------------
//  <copyright file="TestEntry.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Xml.Linq;
using static Akka.MultiNode.TestAdapter.Internal.TrxReporter.Models.XmlHelper;

namespace Akka.MultiNode.TestAdapter.Internal.TrxReporter.Models
{
    internal class TestEntry : ITestEntity
    {
        public TestEntry(Identifier testId, Identifier executionId, Identifier testListId)
        {
            TestId = testId;
            ExecutionId = executionId;
            TestListId = testListId;
        }

        public Identifier TestId { get; }
        public Identifier ExecutionId { get; }
        public Identifier TestListId { get; }

        public XElement Serialize() => Elem("TestEntry",
            Attr("testId", TestId),
            Attr("executionId", ExecutionId),
            Attr("testListId", TestListId)
        );
    }
}