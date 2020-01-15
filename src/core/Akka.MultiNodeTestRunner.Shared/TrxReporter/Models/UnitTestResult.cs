//-----------------------------------------------------------------------
// <copyright file="UnitTestResult.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Xml.Linq;
using static Akka.MultiNodeTestRunner.TrxReporter.Models.XmlHelper;

namespace Akka.MultiNodeTestRunner.TrxReporter.Models
{
    public class UnitTestResult : ITestEntity
    {
        public UnitTestResult(Identifier testId, Identifier executionId, Identifier testListId, string testName, string computerName)
        {
            TestId = testId;
            ExecutionId = executionId;
            RelativeResultsDirectory = executionId;
            TestName = testName;
            ComputerName = computerName;
            TestListId = testListId;

            var now = DateTime.UtcNow;
            StartTime = now;
            EndTime = now;
        }

        public static readonly Identifier TEST_TYPE = Identifier.Create(new Guid("fc0e28d9-ef63-4031-b8b7-1b8cd96208d4"));

        public Identifier TestId { get; }
        public Identifier ExecutionId { get; }
        public string TestName { get; }

        public string ComputerName { get; }
        public TimeSpan Duration => EndTime - StartTime;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public Identifier TestType => TEST_TYPE;
        public TestOutcome Outcome { get; set; } = TestOutcome.NotExecuted;
        public Identifier TestListId { get; }
        public Identifier RelativeResultsDirectory { get; }
        public List<UnitTestResult> InnerResults { get; } = new List<UnitTestResult>();

        public Output Output { get; set; }

        public UnitTestResult AddChildResult(string name)
        {
            var result = new UnitTestResult(TestId, ExecutionId, TestListId, name, ComputerName);
            InnerResults.Add(result);
            return result;
        }

        public XElement Serialize()
        {
            return Elem("UnitTestResult",
                Attr("executionId", ExecutionId),
                Attr("testId", TestId),
                Attr("testName", TestName),
                Attr("computerName", ComputerName),
                Attr("duration", Duration.ToString("c")),
                Attr("startTime", StartTime.ToString("O")),
                Attr("endTime", EndTime.ToString("O")),
                Attr("testType", TestType),
                Attr("outcome", Enum.GetName(typeof(TestOutcome), Outcome)),
                Attr("testListId", TestListId),
                Attr("relativeResultsDirectory", RelativeResultsDirectory),
                Output,
                ElemList("InnerResults", InnerResults)
            );
        }
    }
}
