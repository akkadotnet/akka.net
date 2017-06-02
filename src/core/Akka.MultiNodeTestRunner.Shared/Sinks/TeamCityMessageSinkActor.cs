using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.MultiNodeTestRunner.Shared.Reporting;

namespace Akka.MultiNodeTestRunner.Shared.Sinks
{
    public class TeamCityMessageSinkActor : TestCoordinatorEnabledMessageSink
    {
        public TeamCityMessageSinkActor(bool useTestCoordinator) : base(useTestCoordinator)
        {
        }
        #region Message handling

        protected override void AdditionalReceives()
        {
        }

        protected override void ReceiveFactData(FactData data)
        {

        }

        protected override void HandleNodeSpecFail(NodeCompletedSpecWithFail nodeFail)
        {
            WriteSpecFail(nodeFail.NodeIndex, nodeFail.NodeRole, nodeFail.Message);

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
            WriteSpecMessage(
                $"Test run completed in [{tree.Elapsed}] with {passedSpecs}/{tree.Specs.Count()} specs passed.");
        }

        protected override void HandleNewSpec(BeginNewSpec newSpec)
        {
            WriteSpecMessage($"##teamcity[testStarted name=\'{TeamCityEscape($"{newSpec.ClassName}.{newSpec.MethodName}")}\' flowId=\'{TeamCityEscape($"{newSpec.ClassName}.{newSpec.MethodName}")}\' captureStandardOutput=\'true\']");

            base.HandleNewSpec(newSpec);
        }

        protected override void HandleEndSpec(EndSpec endSpec)
        {
            if (endSpec.ClassName != null && endSpec.MethodName != null)
            {
                WriteSpecMessage(
                    $"##teamcity[testFinished name=\'{TeamCityEscape($"{endSpec.ClassName}.{endSpec.MethodName}")}\' flowId=\'{TeamCityEscape($"{endSpec.ClassName}.{endSpec.MethodName}")}\']");
            }
            else
            {
                throw new ArgumentNullException(nameof(endSpec), "EndSpec message must have non-null ClassName and MethodName");
            }

            base.HandleEndSpec(endSpec);
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
            WriteSpecPass(nodeSuccess.NodeIndex, nodeSuccess.NodeRole, nodeSuccess.Message);

            base.HandleNodeSpecPass(nodeSuccess);
        }

        #endregion

        #region Console output methods

        /// <summary>
        /// Used to print a spec status message (spec starting, finishing, failed, etc...)
        /// </summary>
        protected virtual void WriteSpecMessage(string message)
        {
            Console.WriteLine(message);
        }

        protected virtual void WriteSpecPass(int nodeIndex, string nodeRole, string message)
        {
            Console.WriteLine(
                $"##teamcity[testStdOut name=\'{TeamCityEscape($"[NODE{nodeIndex}:{nodeRole}]")}\' out=\'{TeamCityEscape(message)}\'");
        }

        private void WriteSpecFail(int nodeIndex, string nodeRole, string message)
        {
            Console.WriteLine(
                $"##teamcity[testFailed name=\'{TeamCityEscape($"[NODE{nodeIndex}:{nodeRole}]")}\' out=\'{TeamCityEscape(message)}\'");
        }

        private void WriteRunnerMessage(LogMessageForTestRunner nodeMessage)
        {
            Console.WriteLine(nodeMessage.ToString());
        }

        private void WriteNodeMessage(LogMessageFragmentForNode nodeMessage)
        {
            Console.WriteLine(nodeMessage.ToString());
        }


        private static string TeamCityEscape(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            return input.Replace("|", "||")
                .Replace("'", "|'")
                .Replace("\n", "|n")
                .Replace("\r", "|r")
                .Replace(char.ConvertFromUtf32(int.Parse("0086", NumberStyles.HexNumber)), "|x")
                .Replace(char.ConvertFromUtf32(int.Parse("2028", NumberStyles.HexNumber)), "|l")
                .Replace(char.ConvertFromUtf32(int.Parse("2029", NumberStyles.HexNumber)), "|p")
                .Replace("[", "|[")
                .Replace("]", "|]");
        }

        #endregion
    }

    /// <summary>
    /// <see cref="IMessageSink"/> implementation that writes directly to the console.
    /// </summary>
    public class TeamCityMessageSink : MessageSink
    {
        public TeamCityMessageSink()
            : base(Props.Create(() => new TeamCityMessageSinkActor(true)))
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
