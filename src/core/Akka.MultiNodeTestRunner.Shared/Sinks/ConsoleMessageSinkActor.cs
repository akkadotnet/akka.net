//-----------------------------------------------------------------------
// <copyright file="ConsoleMessageSinkActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Text;
using Akka.Actor;
using Akka.Event;
using Akka.MultiNodeTestRunner.Shared.Reporting;

namespace Akka.MultiNodeTestRunner.Shared.Sinks
{
    /// <summary>
    /// <see cref="MessageSinkActor"/> implementation that logs all of its output directly to the <see cref="Console"/>.
    /// 
    /// Has no persistence capabilities. Can optionally use a <see cref="TestRunCoordinator"/> to provide total "end of test" reporting.
    /// </summary>
    public class ConsoleMessageSinkActor : TestCoordinatorEnabledMessageSink
    {
        private readonly bool _teamcityOutput;

        public ConsoleMessageSinkActor(bool useTestCoordinator, bool teamcityOutput = false)
            : base(useTestCoordinator)
        {
            _teamcityOutput = teamcityOutput;
        }

        #region Message handling

        protected override void AdditionalReceives()
        {
            Receive<FactData>(data => ReceiveFactData(data));
        }

        protected override void ReceiveFactData(FactData data)
        {
            PrintSpecRunResults(data);
        }

        private void PrintSpecRunResults(FactData data)
        {
            var formattedName = TeamCityEscape(data.FactName);

            if (_teamcityOutput)
                Console.WriteLine("##teamcity[testStarted name='{0}']", formattedName);

            var builder = new StringBuilder();

            builder.AppendLine(string.Format("Results for {0}", data.FactName));

            builder.AppendLine(string.Format("Start time: {0}", new DateTime(data.StartTime, DateTimeKind.Utc)));
            foreach (var node in data.NodeFacts)
            {
                builder.AppendLine(string.Format(" --> Node {0}: {1} [{2} elapsed]", node.Value.NodeIndex,
                    node.Value.Passed.GetValueOrDefault(false) ? "PASS" : "FAIL", node.Value.Elapsed));
            }

            builder.AppendLine(string.Format("End time: {0}",
                new DateTime(data.EndTime.GetValueOrDefault(DateTime.UtcNow.Ticks), DateTimeKind.Utc)));

            builder.AppendLine(string.Format("FINAL RESULT: {0} after {1}.",
                data.Passed.GetValueOrDefault(false) ? "PASS" : "FAIL", data.Elapsed));

            var output = builder.ToString();
            SplitLinesAndWriteSpecMessage(output);

            if (_teamcityOutput)
                WriteTeamcityTestOutput(formattedName, output);

            if (data.Passed.GetValueOrDefault(false))
            {
                if (_teamcityOutput)
                    Console.WriteLine("##teamcity[testFinished name='{0}' duration='{1}']",
                        formattedName,
                        (int)(data.Elapsed.TotalMilliseconds));
                return;
            }

            WriteSpecMessage("Failure messages by Node");
            builder = new StringBuilder();

            foreach (var node in data.NodeFacts)
            {
                if (node.Value.Passed.GetValueOrDefault(false)) continue;

                builder.AppendLine(string.Format("<----------- BEGIN NODE {0} ----------->", node.Key));
                foreach (var resultMessage in node.Value.ResultMessages)
                {
                    builder.AppendLine(String.Format(" --> {0}", resultMessage.Message));
                }
                if (node.Value.ResultMessages == null || node.Value.ResultMessages.Count == 0)
                {
                    builder.AppendLine("[received no messages - SILENT FAILURE].");
                }
                builder.AppendLine(string.Format("<----------- END NODE {0} ----------->", node.Key));
            }

            output = builder.ToString();
            SplitLinesAndWriteSpecMessage(output);

            if (_teamcityOutput)
            {
                Console.WriteLine("##teamcity[testFailed name='{0}' details='{1}']",
                    formattedName,
                    TeamCityEscape(output));

                Console.WriteLine("##teamcity[testFinished name='{0}' duration='{1}']",
                    formattedName,
                    (int)(data.Elapsed.TotalMilliseconds));
            }
        }

        private void SplitLinesAndWriteSpecMessage(string output)
        {
            output
                .Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)
                .ToList()
                .ForEach(WriteSpecMessage);
        }

        protected override void HandleNodeSpecFail(NodeCompletedSpecWithFail nodeFail)
        {
            WriteSpecFail(nodeFail.NodeIndex, nodeFail.Message);

            base.HandleNodeSpecFail(nodeFail);
        }

        protected override void HandleTestRunEnd(EndTestRun endTestRun)
        {
            WriteSpecMessage("Test run complete.");

            base.HandleTestRunEnd(endTestRun);
        }

        protected override void HandleTestRunTree(TestRunTree tree)
        {
            var passedSpecs = tree.Specs.Count(x => x.Passed.GetValueOrDefault(false));
            WriteSpecMessage(string.Format("Test run completed in [{0}] with {1}/{2} specs passed.", tree.Elapsed, passedSpecs, tree.Specs.Count()));
            foreach (var factData in tree.Specs)
            {
                PrintSpecRunResults(factData);
            }
        }

        protected override void HandleNewSpec(BeginNewSpec newSpec)
        {
            WriteSpecMessage(string.Format("Beginning spec {0}.{1} on {2} nodes", newSpec.ClassName, newSpec.MethodName, newSpec.Nodes.Count));

            base.HandleNewSpec(newSpec);
        }

        protected override void HandleEndSpec(EndSpec endSpec)
        {
            WriteSpecMessage("Spec completed.");

            base.HandleEndSpec(endSpec);
        }

        protected override void HandleNodeMessage(LogMessageForNode logMessage)
        {
            WriteNodeMessage(logMessage);

            base.HandleNodeMessage(logMessage);
        }

        protected override void HandleNodeMessageFragment(LogMessageFragmentForNode logMessage)
        {
            WriteNodeMessage(logMessage);

            base.HandleNodeMessageFragment(logMessage);
        }

        protected override void HandleRunnerMessage(LogMessageForTestRunner node)
        {
            WriteRunnerMessage(node);

            base.HandleRunnerMessage(node);
        }

        protected override void HandleNodeSpecPass(NodeCompletedSpecWithSuccess nodeSuccess)
        {
            WriteSpecPass(nodeSuccess.NodeIndex, nodeSuccess.Message);

            base.HandleNodeSpecPass(nodeSuccess);
        }

        #endregion

        #region Console output methods

        /// <summary>
        /// Used to print a spec status message (spec starting, finishing, failed, etc...)
        /// </summary>
        private void WriteSpecMessage(string message)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("[RUNNER][{0}]: {1}", DateTime.UtcNow.ToShortTimeString(), message);
            Console.ResetColor();
        }

        private void WriteSpecPass(int nodeIndex, string message)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[NODE{0}][{1}]: SPEC PASSED: {2}", nodeIndex, DateTime.UtcNow.ToShortTimeString(), message);
            Console.ResetColor();
        }

        private void WriteSpecFail(int nodeIndex, string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[NODE{0}][{1}]: SPEC FAILED: {2}", nodeIndex, DateTime.UtcNow.ToShortTimeString(), message);
            Console.ResetColor();
        }

        private void WriteRunnerMessage(LogMessageForTestRunner nodeMessage)
        {
            Console.ForegroundColor = ColorForLogLevel(nodeMessage.Level);
            Console.WriteLine(nodeMessage.ToString());
            Console.ResetColor();
        }

        private void WriteNodeMessage(LogMessageForNode nodeMessage)
        {
            Console.ForegroundColor = ColorForLogLevel(nodeMessage.Level);
            Console.WriteLine(nodeMessage.ToString());
            Console.ResetColor();
        }

        private void WriteNodeMessage(LogMessageFragmentForNode nodeMessage)
        {
            Console.WriteLine(nodeMessage.ToString());
        }

        private void WriteTeamcityTestOutput(string testName, string details)
        {
            Console.WriteLine("##teamcity[testStdOut name='{0}' out='{1}']",
                testName,
                TeamCityEscape(details));
        }

        private static ConsoleColor ColorForLogLevel(LogLevel level)
        {
            var color = ConsoleColor.DarkGray;
            switch (level)
            {
                case LogLevel.DebugLevel:
                    color = ConsoleColor.Gray;
                    break;
                case LogLevel.InfoLevel:
                    color = ConsoleColor.White;
                    break;
                case LogLevel.WarningLevel:
                    color = ConsoleColor.Yellow;
                    break;
                case LogLevel.ErrorLevel:
                    color = ConsoleColor.Red;
                    break;
            }

            return color;
        }

        #endregion

        static string TeamCityEscape(string value)
        {
            if (value == null)
                return String.Empty;

            return value.Replace("|", "||")
                        .Replace("'", "|'")
                        .Replace("\r", "|r")
                        .Replace("\n", "|n")
                        .Replace("]", "|]")
                        .Replace("[", "|[")
                        .Replace("\u0085", "|x")
                        .Replace("\u2028", "|l")
                        .Replace("\u2029", "|p");
        }
    }

    /// <summary>
    /// <see cref="IMessageSink"/> implementation that writes directly to the console.
    /// </summary>
    public class ConsoleMessageSink : MessageSink
    {
        public ConsoleMessageSink(bool teamcityOutput = false)
            : base(Props.Create(() => new ConsoleMessageSinkActor(true, teamcityOutput)))
        {
        }

        protected override void HandleUnknownMessageType(string message)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("Unknown message: {0}", message);
            Console.ResetColor();
        }
    }
}

