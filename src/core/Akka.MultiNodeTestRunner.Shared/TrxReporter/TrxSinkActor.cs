//-----------------------------------------------------------------------
// <copyright file="TrxSinkActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Akka.MultiNodeTestRunner.Shared.Reporting;
using Akka.MultiNodeTestRunner.Shared.Sinks;
using Akka.MultiNodeTestRunner.TrxReporter.Models;

namespace Akka.MultiNodeTestRunner.Shared.AzureDevOps
{
    public class TrxSinkActor : TestCoordinatorEnabledMessageSink
    {
        public TrxSinkActor(string suiteName, string userName, string computerName, bool useTestCoordinator)
            : base(useTestCoordinator)
        {
            _computerName = computerName;
            _testRun = new TestRun(suiteName)
            {
                RunUser = userName
            };
        }

        private readonly string _computerName;
        private readonly TestRun _testRun;
        private SpecSession _session;

        protected override void AdditionalReceives()
        {
            
        }

        protected override void HandleTestRunTree(TestRunTree tree)
        {
        }

        protected override void ReceiveFactData(FactData data)
        {
        }

        protected override void HandleEndSpec(EndSpec endSpec)
        {
            base.HandleEndSpec(endSpec);

            _session.OnEnd(endSpec);
            ReportSpec(_session, _testRun, _computerName, endSpec.Log);

            _session = null;
        }

        protected override void HandleNewSpec(BeginNewSpec newSpec)
        {
            base.HandleNewSpec(newSpec);

            _session = new SpecSession();
            _session.OnBegin(newSpec);
        }

        protected override void HandleNodeMessageFragment(LogMessageFragmentForNode logMessage)
        {
            base.HandleNodeMessageFragment(logMessage);

            _session.OnMessage(logMessage);
        }

        protected override void HandleNodeSpecFail(NodeCompletedSpecWithFail nodeFail)
        {
            base.HandleNodeSpecFail(nodeFail);

            _session.OnFailure(nodeFail);
        }

        protected override void HandleNodeSpecPass(NodeCompletedSpecWithSuccess nodeSuccess)
        {
            base.HandleNodeSpecPass(nodeSuccess);

            _session.OnSuccess(nodeSuccess);
        }

        protected override void HandleRunnerMessage(LogMessageForTestRunner node)
        {
            base.HandleRunnerMessage(node);
            
            _testRun.Log($"{node.When:G} [{node.Level}] {node.LogSource} : {node.Message}");
        }

        protected override void HandleTestRunEnd(EndTestRun endTestRun)
        {
            base.HandleTestRunEnd(endTestRun);

            var doc = new XDocument(
                _testRun.Serialize()
            );

            doc.Save(Path.Combine(Directory.GetCurrentDirectory(), $@"mntr-{DateTime.UtcNow:yyyy'-'MM'-'dd'T'HH'-'mm'-'ss'-'fffffffK}.trx"));
        }

        private static void ReportSpec(SpecSession session, TestRun testRun, string computerName, SpecLog log)
        {
            var begin = session.Begin.Value;
            var beginTime = session.Begin.Time;

            var test = testRun.AddUnitTest(begin.ClassName, begin.MethodName, $"{begin.ClassName}.{begin.MethodName}");
            var specResult = test.AddResult(begin.MethodName, computerName);

            var nodeResults = new Dictionary<int, UnitTestResult>();

            ReportNodes(begin, specResult, beginTime, nodeResults);
            ReportSuccess(session, nodeResults);
            ReportFailure(session, nodeResults, log);

            specResult.Outcome = GetCombinedTestOutcome(nodeResults.Values);
            specResult.StartTime = beginTime;
            specResult.EndTime = session.End.Time;

            ReportTestMessages(session, nodeResults, specResult, log);
        }

        private static void ReportFailure(SpecSession session, Dictionary<int, UnitTestResult> nodeResults, SpecLog log)
        {
            foreach (var (time, message) in session.Fails)
            {
                var result = nodeResults[message.NodeIndex];
                result.Outcome = TestOutcome.Failed;
                result.EndTime = time;

                result.Output = new Output();
                result.Output.StdErr.Add(message.Message);
                
                var nodeLog = log.NodeLogs.Find(n => n.NodeIndex == message.NodeIndex);
                if (nodeLog.Log != null)
                    result.Output.StdErr.AddRange(nodeLog.Log);
                
                result.Output.DebugTrace.Add(message.Message);
                result.Output.ErrorInfo = new ErrorInfo() { Message = message.Message };
            }
        }

        private static void ReportSuccess(SpecSession session, Dictionary<int, UnitTestResult> nodeResults)
        {
            foreach (var (time, message) in session.Successes)
            {
                var result = nodeResults[message.NodeIndex];
                result.Outcome = TestOutcome.Passed;
                result.EndTime = time;
                result.Output = new Output();

                result.Output.StdOut.Add(message.Message);
                result.Output.DebugTrace.Add(message.Message);
            }
        }

        private static void ReportNodes(BeginNewSpec begin, UnitTestResult specResult, DateTime beginTime,
            Dictionary<int, UnitTestResult> nodeResults)
        {
            foreach (var node in begin.Nodes)
            {
                var result = specResult.AddChildResult(node.Role);
                if (!string.IsNullOrWhiteSpace(node.SkipReason))
                {
                    result.Outcome = TestOutcome.NotExecuted;
                    result.StartTime = beginTime;
                    result.EndTime = beginTime;
                }
                else
                {
                    result.Outcome = TestOutcome.InProgress;
                    result.StartTime = beginTime;
                }

                nodeResults.Add(node.Node, result);
            }
        }

        private static void ReportTestMessages(SpecSession session, Dictionary<int, UnitTestResult> nodeResults, UnitTestResult specResult, SpecLog log)
        {
            foreach (var (_, message) in session.Messages)
            {
                var textMessage = $"[{message.When:G}] {message.Message}";

                Output output;
                if (nodeResults.TryGetValue(message.NodeIndex, out var result))
                {
                    if (result.Output == null)
                    {
                        result.Output = new Output();
                    }

                    output = result.Output;
                }
                else
                {
                    if (specResult.Output == null)
                    {
                        specResult.Output = new Output();
                    }

                    output = specResult.Output;
                }

                output.StdOut.Add(textMessage);
                output.DebugTrace.Add(textMessage);
            }

            specResult.Output = specResult.Output ?? new Output();
            specResult.Output.StdErr.AddRange(log.AggregatedTimelineLog);
        }

        private static TestOutcome GetCombinedTestOutcome(IEnumerable<UnitTestResult> results)
        {
            var outcomes = results.Select(x => x.Outcome).Distinct().ToArray();

            return outcomes.Length == 1
                ? outcomes[0]
                : outcomes.Any(x => x == TestOutcome.Failed)
                    ? TestOutcome.Failed
                    : TestOutcome.Inconclusive;
        }
    }
}
