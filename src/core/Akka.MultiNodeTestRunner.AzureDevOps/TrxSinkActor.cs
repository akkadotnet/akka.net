// -----------------------------------------------------------------------
//  <copyright file="TrxSinkActor.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

namespace Akka.MultiNodeTestRunner.Shared.AzureDevOps
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Xml.Linq;
    using MultiNodeTestRunner.AzureDevOps.Models;
    using Newtonsoft.Json;
    using Reporting;
    using Sinks;

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
            _testRun.Log("HandleTestRunTree : " + JsonConvert.SerializeObject(tree));
        }

        protected override void ReceiveFactData(FactData data)
        {
            _testRun.Log("ReceiveFactData : " + JsonConvert.SerializeObject(data));
        }

        protected override void HandleEndSpec(EndSpec endSpec)
        {
            base.HandleEndSpec(endSpec);

            _session.OnEnd(endSpec);
            ReportSpec(_session, _testRun, _computerName);

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

        protected override void HandleSinkTerminate(BeginSinkTerminate terminate)
        {
            base.HandleSinkTerminate(terminate);
        }

        protected override void HandleTestRunEnd(EndTestRun endTestRun)
        {
            base.HandleTestRunEnd(endTestRun);

            var doc = new XDocument(
                _testRun.Serialize()
            );

            doc.Save(Path.Combine(Directory.GetCurrentDirectory(), $@"mntr-{DateTime.UtcNow.ToString("O").Replace(":", "_")}.trx"));
        }

        private static void ReportSpec(SpecSession session, TestRun testRun, string computerName)
        {
            var begin = session.Begin.Value;
            var beginTime = session.Begin.Time;

            var test = testRun.AddUnitTest(begin.ClassName, begin.MethodName, $"{begin.ClassName}.{begin.MethodName}");
            var specResult = test.AddResult(begin.MethodName, computerName);

            var nodeResults = new Dictionary<int, UnitTestResult>();
            foreach (var node in begin.Nodes)
            {
                var result = specResult.AddChildResult(node.Role);
                if (!string.IsNullOrWhiteSpace(node.SkipReason))
                {
                    result.Outcome = TestOutcome.NotExecuted;
                    result.StartTime = beginTime;
                    result.EndTime = beginTime;
                    result.Duration = TimeSpan.Zero;
                }
                else
                {
                    result.Outcome = TestOutcome.InProgress;
                    result.StartTime = beginTime;
                }

                nodeResults.Add(node.Node, result);
            }

            foreach (var (time, message) in session.Successes)
            {
                var result = nodeResults[message.NodeIndex];
                result.Outcome = TestOutcome.Passed;
                result.EndTime = time;
                result.Duration = time - result.StartTime;
                result.Output = new Output
                {
                    StdOut = message.Message
                };
            }

            foreach (var (time, message) in session.Fails)
            {
                var result = nodeResults[message.NodeIndex];
                result.Outcome = TestOutcome.Failed;
                result.EndTime = time;
                result.Duration = time - result.StartTime;
                result.Output = new Output
                {
                    StdErr = message.Message
                };
            }

            var outcomes = nodeResults.Values.Select(x => x.Outcome).Distinct().ToArray();

            specResult.Outcome = outcomes.Length == 1
                ? outcomes[0]
                : outcomes.Any(x => x == TestOutcome.Failed)
                    ? TestOutcome.Failed
                    : TestOutcome.Inconclusive;

            specResult.StartTime = beginTime;
            specResult.EndTime = session.End.Time;
            specResult.Duration = specResult.EndTime - specResult.StartTime;

            foreach (var (_, message) in session.Messages)
            {
                if (nodeResults.TryGetValue(message.NodeIndex, out var result))
                {
                    if (result.Output == null)
                    {
                        result.Output = new Output();
                    }
                    result.Output.TextMessages.Add($"[{message.When:G}] {message.Message}");
                }
                else
                {
                    if (specResult.Output == null)
                    {
                        specResult.Output = new Output();
                    }

                    specResult.Output.TextMessages.Add($"[{message.When:G}] {message.Message}");
                }
            }
        }
    }
}
