using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.MultiNodeTestRunner.Shared.Reporting;
using JetBrains.TeamCity.ServiceMessages;
using JetBrains.TeamCity.ServiceMessages.Write.Special;

namespace Akka.MultiNodeTestRunner.Shared.Sinks
{
    public class TeamCityMessageSinkActor : TestCoordinatorEnabledMessageSink
    {
        private readonly ITeamCityWriter _teamCityWriter;
        private IActorRef _teamCityTestSuiteWriterActor;
        public TeamCityMessageSinkActor(ITeamCityWriter teamCityWriter, bool useTestCoordinator) : base(useTestCoordinator)
        {
            _teamCityWriter = teamCityWriter;
        }

        protected override void AdditionalReceives()
        {
            throw new NotImplementedException();
        }

        protected override void HandleTestRunTree(TestRunTree tree)
        {
            throw new NotImplementedException();
        }

        protected override void ReceiveFactData(FactData data)
        {
            throw new NotImplementedException();
        }

        protected override void HandleNewSpec(BeginNewSpec newSpec)
        {
            _teamCityTestSuiteWriterActor =
                Context.ActorOf(Props.Create(() => new TeamCityTestSuiteWriterActor(_teamCityWriter)));
            _teamCityTestSuiteWriterActor.Tell(newSpec);
            base.HandleNewSpec(newSpec);
        }

        protected override void HandleRunnerMessage(LogMessageForTestRunner node)
        {
            _teamCityTestSuiteWriterActor.Tell(node);
        }
    }

    public class TeamCityTestSuiteWriterActor : ReceiveActor
    {
        private readonly ITeamCityWriter _teamCityWriter;
        private ITeamCityTestsSubWriter _teamCityTestSuiteWriter;
        private IActorRef _teamCityFlowWriter;
        public TeamCityTestSuiteWriterActor(ITeamCityWriter teamCityWriter)
        {
            _teamCityWriter = teamCityWriter;

            Receive<BeginNewSpec>(msg => WriteTestSuiteStart(msg));
            Receive<LogMessageForTestRunner>(msg => WriteTestStdOut(msg));
        }

        private void WriteTestSuiteStart(BeginNewSpec beginNewSpec)
        {
            _teamCityTestSuiteWriter =
                _teamCityWriter.OpenTestSuite($"{beginNewSpec.ClassName}.{beginNewSpec.MethodName}");
            _teamCityFlowWriter =
                Context.ActorOf(Props.Create(() => new TeamCityTestFlowWriterActor(_teamCityTestSuiteWriter)));
            _teamCityFlowWriter.Tell(beginNewSpec);
        }

        private void WriteTestStdOut(LogMessageForTestRunner msg)
        {
            _teamCityFlowWriter.Tell(msg);
        }
    }

    public class TeamCityTestFlowWriterActor : ReceiveActor
    {
        private readonly ITeamCityTestsSubWriter _teamCityTestSuiteWriter;
        private ITeamCityTestsSubWriter _teamCityFlowWriter;
        private IActorRef _teamCityTestWriterActor;

        public TeamCityTestFlowWriterActor(ITeamCityTestsSubWriter teamCityTestSuiteWriter)
        {
            _teamCityTestSuiteWriter = teamCityTestSuiteWriter;

            Receive<BeginNewSpec>(msg => WriteTestFlowStart(msg));
            Receive<LogMessageForTestRunner>(msg => WriteTestStdOut(msg));
        }

        private void WriteTestFlowStart(BeginNewSpec beginNewSpec)
        {
            _teamCityFlowWriter =
                _teamCityTestSuiteWriter.OpenFlow();
            _teamCityTestWriterActor =
                Context.ActorOf(Props.Create(() => new TeamCityTestWriterActor(_teamCityFlowWriter, beginNewSpec)));
        }

        private void WriteTestStdOut(LogMessageForTestRunner msg)
        {
            _teamCityTestWriterActor.Tell(msg);
        }
    }

    public class TeamCityTestWriterActor : ReceiveActor
    {
        private readonly ITeamCityTestsSubWriter _teamCityFlowWriter;
        private ITeamCityTestWriter _teamCityTestWriter;

        public TeamCityTestWriterActor(ITeamCityTestsSubWriter teamCityFlowWriter, BeginNewSpec beginNewSpec)
        {
            _teamCityFlowWriter = teamCityFlowWriter;
            _teamCityTestWriter = _teamCityFlowWriter.OpenTest($"{beginNewSpec.ClassName}.{beginNewSpec.MethodName}");

            Receive<string>(msg => WriteTestStandardOut(msg));
        }

        private void WriteTestStandardOut(string stdOutMessage)
        {
            _teamCityTestWriter.WriteStdOutput(stdOutMessage);
        }
    }


    /// <summary>
    /// <see cref="IMessageSink"/> implementation that writes directly to the console.
    /// </summary>
    public class TeamCityMessageSink : MessageSink
    {
        public TeamCityMessageSink(ITeamCityWriter teamCityWriter)
            : base(Props.Create(() => new TeamCityMessageSinkActor(teamCityWriter, true)))
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
