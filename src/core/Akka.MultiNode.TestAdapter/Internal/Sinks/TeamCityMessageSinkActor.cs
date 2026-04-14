//-----------------------------------------------------------------------
// <copyright file="TeamCityMessageSinkActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.MultiNode.TestAdapter.Internal.Reporting;
using JetBrains.TeamCity.ServiceMessages.Write.Special;

namespace Akka.MultiNode.TestAdapter.Internal.Sinks
{
    internal class TeamCityMessageSinkActor : TestCoordinatorEnabledMessageSink
    {
        private readonly ITeamCityWriter _teamCityWriter;
        private readonly ITeamCityTestsSubWriter _teamCityTestSuiteWriter;

        private ITeamCityTestsSubWriter _teamCityFlowWriter;
        private ITeamCityTestWriter _teamCityTestWriter;

        public TeamCityMessageSinkActor(Action<string> writer, string suiteName,
            bool useTestCoordinator) : base(useTestCoordinator)
        {
            _teamCityWriter = new TeamCityServiceMessages().CreateWriter(writer);
            _teamCityTestSuiteWriter = _teamCityWriter.OpenTestSuite(suiteName);
        }

        protected override void AdditionalReceives()
        {
        }

        protected override void HandleTestRunTree(TestRunTree tree)
        {
        }

        protected override void ReceiveFactData(FactData data)
        {
        }

        protected override void HandleNewSpec(BeginNewSpec beginNewSpec)
        {         
            _teamCityFlowWriter = _teamCityTestSuiteWriter.OpenFlow();
            _teamCityTestWriter = _teamCityFlowWriter.OpenTest($"{beginNewSpec.ClassName}.{beginNewSpec.MethodName}");

            base.HandleNewSpec(beginNewSpec);
        }

        protected override void HandleRunnerMessage(LogMessageForTestRunner node)
        {
            _teamCityTestWriter?.WriteStdOutput(node.Message);

            base.HandleRunnerMessage(node);
        }

        protected override void HandleNodeMessageFragment(LogMessageFragmentForNode logMessage)
        {
            _teamCityTestWriter?.WriteStdOutput(logMessage.Message);

            base.HandleNodeMessageFragment(logMessage);
        }

        protected override void HandleNodeSpecPass(NodeCompletedSpecWithSuccess nodeSuccess)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            _teamCityTestWriter?.WriteStdOutput(
                $"[NODE{nodeSuccess.NodeIndex}:{nodeSuccess.NodeRole}][{DateTime.UtcNow.ToShortTimeString()}]: SPEC PASSED: {nodeSuccess.Message}");
            Console.ResetColor();
            
            base.HandleNodeSpecPass(nodeSuccess);
        }

        protected override void HandleNodeSpecFail(NodeCompletedSpecWithFail nodeFail)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            _teamCityTestWriter?.WriteFailed(
                $"[NODE{nodeFail.NodeIndex}:{nodeFail.NodeRole}][{DateTime.UtcNow.ToShortTimeString()}]: SPEC FAILED: {nodeFail.Message}", "");
            Console.ResetColor();

            base.HandleNodeSpecFail(nodeFail);
        }

        protected override void HandleEndSpec(EndSpec endSpec)
        {
            _teamCityTestWriter?.Dispose();
            _teamCityFlowWriter?.Dispose();

            base.HandleEndSpec(endSpec);
        }
    }

    /// <summary>
    /// <see cref="IMessageSink"/> implementation that writes directly to the console.
    /// </summary>
    internal class TeamCityMessageSink : MessageSink
    {
        public TeamCityMessageSink(Action<string> writer, string suiteName)
            : base(Props.Create(() => new TeamCityMessageSinkActor(writer, suiteName, true)))
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
